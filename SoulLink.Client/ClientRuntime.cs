using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using SoulLink;

namespace SoulLink.Client;

public sealed record JoinForm(string Server, string Invitation, string Name, PlayerMarker Marker = PlayerMarker.Circle);

public sealed record ClientSettings
{
    public string Server { get; init; } = "http://127.0.0.1:5080";
    public string Name { get; init; } = "";
    public Guid ClientId { get; init; } = Guid.NewGuid();
    public string Token { get; init; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public Registration? Registration { get; init; }
    public PlayerMarker Marker { get; init; }
    public JoinForm? PendingJoin { get; init; }
}

/// <summary>One local bridge, one authenticated server session, and no offline event queue.</summary>
public sealed class ClientRuntime : BackgroundService
{
    private readonly string settingsPath;
    private readonly int bridgePort;
    private readonly SemaphoreSlim connectionGate = new(1);
    private ClientSettings settings;
    private readonly bool allowSyntheticFrames;
    private readonly bool enableLocalRun;
    private readonly string? localRunDirectory;
    private readonly TimeProvider clock;
    private readonly JoinForm? autoJoin;
    private DateTimeOffset nextJoinAttempt;
    private readonly object stateGate = new();
    private readonly ItemBridge items = new();
    private string? startedBridgeSession;
    private readonly Queue<string> log = new();
    private HubConnection? connection;
    private SessionInfo? session;
    private GameSnapshot? latest;
    private GameSnapshot? evaluated;
    private string? lastFingerprint;
    private bool baseline = true;
    private bool pushUpdates;
    private bool? reportedBridge;
    private int runDirty;
    private int reconnectFailures;
    private DateTimeOffset nextReconnect;
    private DateTimeOffset nextObservation;
    private DateTimeOffset nextHeartbeat;
    private DateTimeOffset nextRepair;
    private long sequence;
    private string state = "Not registered";
    private DateTimeOffset? bridgeSeen;
    private ClientRunView? run;
    private ClientRunView? localView;
    private LocalRunTracker? localTracker;
    private string? localRomHash;
    private string? romPath;
    private uint? localTrainerId;
    private bool bridgeReconnected;
    private readonly TaskCompletionSource<int> bridgeReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ClientRuntime(IConfiguration configuration, TimeProvider? clock = null, JoinForm? autoJoin = null)
    {
        this.clock = clock ?? TimeProvider.System;
        this.autoJoin = autoJoin;
        this.allowSyntheticFrames = configuration.GetValue("AllowSyntheticFrames", false);
        this.enableLocalRun = configuration.GetValue("EnableLocalRun", false);
        this.localRunDirectory = configuration["LocalRunDirectory"];
        this.bridgePort = configuration.GetValue("BridgePort", 5091);
        this.settingsPath = Path.GetFullPath(configuration["SettingsPath"] ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoulLink", "client.json"));
        this.settings = File.Exists(this.settingsPath) ? Protocol.Deserialize<ClientSettings>(File.ReadAllText(this.settingsPath)) : new ClientSettings();
        this.SaveSettings(this.settings);
    }

    public ClientView Status()
    {
        lock (this.stateGate)
        {
            bool live = this.bridgeSeen is not null && this.clock.GetUtcNow() - this.bridgeSeen < TimeSpan.FromSeconds(10);
            var pending = this.settings.PendingJoin ?? this.autoJoin;
            bool local = this.run is null && this.localView is not null;
            return new ClientView(
                this.settings.Registration is null ? pending?.Name ?? this.settings.Name : this.settings.Name,
                this.settings.Registration is null ? pending?.Server ?? this.settings.Server : this.settings.Server,
                this.settings.Registration is not null,
                this.state, this.bridgeReady.Task.IsCompletedSuccessfully ? this.bridgeReady.Task.Result : this.bridgePort,
                live ? "Online" : "Waiting for mGBA", this.bridgeSeen,
                live ? this.latest : null,
                local ? this.localView : this.run, this.log.ToArray(), this.allowSyntheticFrames,
                LocalPlayerId: local ? this.localTracker?.PlayerId : this.settings.Registration?.PlayerId,
                Marker: pending?.Marker ?? this.settings.Marker, IsLocalRun: local);
        }
    }

    internal void AcceptFrame(BridgeFrame frame)
    {
        if (!this.allowSyntheticFrames && frame.LayoutId.StartsWith("synthetic", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Entwicklungsdaten werden im Spieler-Client nicht angenommen.");
        var snapshot = frame.Parse();
        lock (this.stateGate)
        {
            this.latest = snapshot;
            this.bridgeSeen = this.clock.GetUtcNow();
            if (this.enableLocalRun)
            {
                if (this.localTrainerId != frame.TrainerId)
                {
                    this.ClearLocalRun();
                    this.localTrainerId = frame.TrainerId;
                }
                this.localTracker ??= new LocalRunTracker(this.clock,
                    this.localRunDirectory is { } directory && this.localRomHash is { } rom && this.localTrainerId is uint trainer
                        ? Path.Combine(directory, $"{rom}-{trainer:X8}.sqlite") : null);
                if (this.localTracker.Observe(snapshot)) this.localView = this.localTracker.View();
            }
        }
    }

    public async Task SelectLocalGameAsync(string romPath)
    {
        string? romHash = null;
        if (this.enableLocalRun && this.localRunDirectory is not null)
        {
            using var rom = File.OpenRead(romPath);
            romHash = Convert.ToHexString(await SHA256.HashDataAsync(rom).ConfigureAwait(false));
        }
        lock (this.stateGate)
        {
            this.ClearLocalRun();
            this.romPath = romPath;
            this.localRomHash = romHash;
            this.localTrainerId = null;
        }
    }

    public void ResetLocalRun()
    {
        lock (this.stateGate)
        {
            this.ClearLocalRun();
            this.romPath = null;
            this.localRomHash = null;
            this.localTrainerId = null;
        }
    }

    private void ClearLocalRun()
    {
        this.localTracker?.Dispose();
        this.localTracker = null;
        this.localView = null;
    }

    public override void Dispose()
    {
        base.Dispose();
        this.ResetLocalRun();
    }

    public async Task<CatchCheck> CheckCatchAsync(Guid runId, string name)
    {
        lock (this.stateGate)
        {
            if (this.localView?.Id == runId && this.localTracker is { } tracker)
                return tracker.CheckCatch(name);
        }
        await this.connectionGate.WaitAsync();
        try { return await this.RequireConnection(runId).InvokeAsync<CatchCheck>("CheckCatch", name); }
        finally { this.connectionGate.Release(); }
    }

    public async Task<PokemonRomDetails?> PartnerPokemonDetailsAsync(Guid runId, Guid player, int species, CancellationToken cancellationToken)
    {
        try
        {
            return await this.RequireConnection(runId).InvokeAsync<PokemonRomDetails?>("PokemonDetails", player, species, cancellationToken);
        }
        catch (Exception error) when (error is InvalidOperationException or HubException or IOException)
        {
            return null;
        }
    }

    private PokemonRomDetails? ReadPokemonDetails(int species)
    {
        string? path;
        lock (this.stateGate) path = this.romPath;
        if (path is null) return null;
        try
        {
            using var rom = File.OpenRead(path);
            return RubyRomDetails.Read(rom, species);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public Task ChangeArenaAsync(Guid runId, int number, bool complete) =>
        this.RunActionAsync(runId, t => t.ChangeArena(number, complete), c => c.InvokeAsync("ChangeArena", number, complete));

    public Task AddRuleAsync(Guid runId, string text) =>
        this.RunActionAsync(runId, t => t.AddRule(text), c => c.InvokeAsync("AddRule", text));

    public string BeginLocalItemSession() => this.items.Begin();

    public bool OwnedBridgeReady => this.items.MatchesSession(Volatile.Read(ref this.startedBridgeSession));

    public bool CanAddPcItems => this.items.Ready;

    public Task<int> AddPcItemsAsync(PcItem item, CancellationToken cancellationToken = default) =>
        this.items.AddAsync(item, cancellationToken);

    public Task ResolveFaintAsync(Guid runId, Guid pokemonId, Guid incidentId, bool confirm, string? reason) =>
        this.RunActionAsync(runId, t => t.ResolveFaint(pokemonId, incidentId, confirm, reason),
            c => c.InvokeAsync("ResolveFaint", pokemonId, incidentId, confirm, reason));

    private async Task RunActionAsync(Guid runId, Action<LocalRunTracker> localAction, Func<HubConnection, Task> remoteAction)
    {
        lock (this.stateGate)
        {
            if (this.localView?.Id == runId && this.localTracker is { } tracker)
            {
                localAction(tracker);
                this.localView = tracker.View();
                return;
            }
        }
        await this.connectionGate.WaitAsync();
        try
        {
            var connected = this.RequireConnection(runId);
            await remoteAction(connected);
            var dashboard = DashboardProjection.FromRun(await connected.InvokeAsync<AutomationRun>("Status"));
            lock (this.stateGate) this.run = dashboard;
        }
        finally { this.connectionGate.Release(); }
    }

    private HubConnection RequireConnection(Guid runId)
    {
        lock (this.stateGate)
        {
            return this.run?.Id == runId && this.connection is { State: HubConnectionState.Connected } connected
                ? connected : throw new InvalidOperationException("Der gewählte Run ist gerade nicht verbunden.");
        }
    }

    public async Task JoinAsync(JoinForm form, CancellationToken cancellationToken = default)
    {
        ValidateJoin(form);
        await this.connectionGate.WaitAsync(cancellationToken);
        try
        {
            if (this.settings.Registration is not null) throw new InvalidOperationException("Already registered. Use a separate SettingsPath for another player or run.");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.PostAsJsonAsync(form.Server.TrimEnd('/') + "/api/soullink/join",
                new JoinRequest(form.Invitation.Trim(), form.Name.Trim(), this.settings.ClientId, this.settings.Token, "AXVD", Marker: form.Marker),
                Protocol.Json, cancellationToken);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Join rejected. Check invitation, player name, edition and lobby state.");
            var registration = await response.Content.ReadFromJsonAsync<Registration>(Protocol.Json, cancellationToken)
                ?? throw new InvalidDataException("Server returned an empty registration.");
            lock (this.stateGate)
            {
                this.SaveSettings(this.settings with
                {
                    Registration = registration,
                    Server = form.Server.TrimEnd('/'),
                    Name = form.Name.Trim(),
                    Marker = form.Marker,
                    PendingJoin = null,
                });
            }
            this.Note("Registration saved. Connecting to the server.");
        }
        finally { this.connectionGate.Release(); }
        await this.ReconnectAsync(cancellationToken);
    }

    public async Task QueueJoinAsync(JoinForm form)
    {
        ValidateJoin(form);
        await this.connectionGate.WaitAsync();
        try
        {
            if (this.settings.Registration is not null) return;
            lock (this.stateGate)
            {
                this.SaveSettings(this.settings with { PendingJoin = form with { Name = form.Name.Trim() } });
                this.nextJoinAttempt = DateTimeOffset.MinValue;
            }
        }
        finally { this.connectionGate.Release(); }
    }

    private static void ValidateJoin(JoinForm form)
    {
        if (string.IsNullOrWhiteSpace(form.Name)) throw new InvalidDataException("Bitte gib deinen Spielernamen ein.");
        PlayerBundle.Validate(new PlayerProfile(Guid.NewGuid(), form.Server, form.Invitation, form.Name.Trim()));
        if (!Enum.IsDefined(form.Marker)) throw new InvalidDataException("Ungültiges Karten-Icon.");
    }

    private JoinForm? GetPendingJoin()
    {
        lock (this.stateGate)
        {
            var pending = this.settings.PendingJoin ?? this.autoJoin;
            var now = this.clock.GetUtcNow();
            if (this.settings.Registration is not null || pending is null || now < this.nextJoinAttempt) return null;
            this.nextJoinAttempt = now.AddSeconds(10);
            return pending;
        }
    }

    public async Task ReconnectAsync(CancellationToken cancellationToken = default)
    {
        await this.connectionGate.WaitAsync(cancellationToken);
        try
        {
            this.nextReconnect = DateTimeOffset.MinValue;
            this.reconnectFailures = 0;
            await this.CloseConnectionAsync();
        }
        finally { this.connectionGate.Release(); }
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await base.StartAsync(cancellationToken);
        await this.bridgeReady.Task.WaitAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.Run(() => this.RunAsync(stoppingToken));

    private async Task RunAsync(CancellationToken stoppingToken)
    {
        var bridge = this.ReadBridgeAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(250));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (bridge.IsCompleted) await bridge;
                if (this.GetPendingJoin() is { } pending)
                {
                    try { await this.JoinAsync(pending, stoppingToken); }
                    catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException or InvalidDataException or IOException or UnauthorizedAccessException)
                    {
                        this.SetState("Anmeldung noch nicht möglich — wird automatisch erneut versucht.");
                        this.Note("Prepared registration unavailable (" + exception.GetType().Name + ").");
                    }
                }
                await this.connectionGate.WaitAsync(stoppingToken);
                try { await this.TickAsync(stoppingToken); }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    this.SetState("Offline — tracking paused");
                    this.Note("Run-Verbindung unterbrochen (" + exception.GetType().Name + "). Neuer Versuch folgt automatisch.");
                    await this.CloseConnectionAsync();
                    this.reconnectFailures = Math.Min(this.reconnectFailures + 1, 5);
                    this.nextReconnect = this.clock.GetUtcNow().AddMilliseconds(300 * (1 << this.reconnectFailures) + Random.Shared.Next(250));
                }
                finally { this.connectionGate.Release(); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            try { await this.CloseConnectionAsync(); }
            finally { await bridge; }
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        if (this.settings.Registration is null) return;
        bool bridgeRestarted;
        lock (this.stateGate)
        {
            bridgeRestarted = this.bridgeReconnected;
            this.bridgeReconnected = false;
        }
        if (bridgeRestarted)
        {
            this.baseline = true;
            this.evaluated = null;
        }
        if (this.connection is null)
        {
            if (this.clock.GetUtcNow() < this.nextReconnect) return;
            this.connection = new HubConnectionBuilder().WithUrl(this.settings.Server + "/soullink", options =>
            {
                options.AccessTokenProvider = () => Task.FromResult<string?>(this.settings.Token);
                // Headers carry credentials, never query strings.
                options.Transports = Microsoft.AspNetCore.Http.Connections.HttpTransportType.WebSockets;
                options.SkipNegotiation = true;
                options.ApplicationMaxBufferSize = 4 * 1024 * 1024;
                options.TransportMaxBufferSize = Protocol.MaxMessageBytes * 2;
            }).AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions = Protocol.Json;
            }).Build();
            this.connection.On("RunChanged", () => Interlocked.Exchange(ref this.runDirty, 1));
            this.connection.On<int, PokemonRomDetails?>("ReadPokemonDetails", this.ReadPokemonDetails);
            await this.connection.StartAsync(cancellationToken);
            this.session = await this.connection.InvokeAsync<SessionInfo>("Hello", new Hello(this.settings.ClientId), cancellationToken);
            this.sequence = this.session.NextSequence;
            this.SetState("Synchronizing — waiting for mGBA");
            try
            {
                this.UpdateRun(await this.connection.InvokeAsync<AutomationRun>("Subscribe", cancellationToken));
                this.pushUpdates = true;
                this.nextRepair = this.clock.GetUtcNow().AddSeconds(8);
            }
            catch (HubException)
            {
                // Servers predating subscriptions still provide the same authoritative status.
                this.pushUpdates = false;
            }

            this.reconnectFailures = 0;
        }
        GameSnapshot? snapshot;
        bool fresh;
        lock (this.stateGate)
        {
            snapshot = this.latest;
            fresh = this.bridgeSeen is not null && this.clock.GetUtcNow() - this.bridgeSeen < TimeSpan.FromSeconds(8);
        }
        var now = this.clock.GetUtcNow();
        if (this.reportedBridge != fresh || now >= this.nextHeartbeat)
        {
            await this.connection.InvokeAsync<string>("HeartbeatWithBridge", this.session!.SessionId, fresh, cancellationToken);
            this.reportedBridge = fresh;
            this.nextHeartbeat = now.AddSeconds(3);
        }
        if (snapshot is not null && fresh && now >= this.nextObservation && !ReferenceEquals(this.evaluated, snapshot))
        {
            this.evaluated = snapshot;
            // Time/frame continue changing; only meaningful observations cross the network.
            string fingerprint = Protocol.Serialize(new
            {
                snapshot.Pokemon,
                snapshot.MapGroup,
                snapshot.MapNumber,
                snapshot.Area,
                snapshot.Position,
                snapshot.InBattle,
                snapshot.Battle,
                snapshot.Faints,
                snapshot.WildBattles,
                snapshot.Epoch,
                snapshot.TrainerId,
                snapshot.BadgeMask,
                snapshot.RubyStarterWindow
            });
            bool pendingDeath = snapshot.Pokemon.Any(p => p.Hp == 0 && p.Container == Placement.Party);
            if (this.baseline || fingerprint != this.lastFingerprint || pendingDeath)
            {
                this.nextObservation = this.clock.GetUtcNow().AddMilliseconds(200);
                var observation = new Observation(Guid.NewGuid(), this.session!.SessionId, this.sequence, this.baseline, snapshot);
                var result = await this.connection.InvokeAsync<ObservationResult>("Observe", observation, cancellationToken);
                this.SetState(result.State + " — " + result.Message);
                if (result.Accepted)
                {
                    this.sequence = result.NextSequence;
                    this.baseline = false;
                    this.lastFingerprint = fingerprint;
                }
                else if (result.State == "Reconnecting")
                {
                    await this.CloseConnectionAsync();
                    return;
                }
                else if (result.State == "OutOfSync")
                {
                    this.lastFingerprint = null;
                }
            }
        }
        if (Interlocked.Exchange(ref this.runDirty, 0) == 1 || now >= this.nextRepair)
        {
            this.UpdateRun(await this.connection.InvokeAsync<AutomationRun>("Status", cancellationToken));
            this.nextRepair = now.AddSeconds(this.pushUpdates ? 8 : 1);
        }
    }

    private void UpdateRun(AutomationRun status)
    {
        var dashboard = DashboardProjection.FromRun(status);
        lock (this.stateGate) this.run = dashboard;
    }

    private async Task ReadBridgeAsync(CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, this.bridgePort);
        try
        {
            listener.Start(1);
            this.bridgeReady.TrySetResult(((IPEndPoint)listener.LocalEndpoint).Port);
        }
        catch (SocketException exception)
        {
            this.bridgeReady.TrySetException(exception);
            throw;
        }
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var peer = await listener.AcceptTcpClientAsync(cancellationToken);
                var stream = peer.GetStream();
                lock (this.stateGate)
                {
                    this.bridgeReconnected = true;
                    this.bridgeSeen = null;
                }
                this.Note("mGBA bridge connected on loopback.");
                try
                {
                    string? frameError = null;
                    await foreach (string line in JsonLines.ReadAsync(stream, cancellationToken))
                    {
                        try
                        {
                            var frame = Protocol.Deserialize<BridgeFrame>(line);
                            if (frame is { Version: Protocol.Version, Type: "bridge-ready" })
                            {
                                if (this.items.MatchesSession(frame.ItemSession)) Volatile.Write(ref this.startedBridgeSession, frame.ItemSession);
                                continue;
                            }
                            if (frame is { Version: Protocol.Version, Type: "pc-item-result" })
                            {
                                this.items.AcceptResult(stream, frame);
                                continue;
                            }
                            this.AcceptFrame(frame);
                            this.items.Observe(stream, frame);
                            if (frameError is not null) this.Note("mGBA sendet wieder gültige Spieldaten.");
                            frameError = null;
                        }
                        catch (Exception exception) when (exception is InvalidDataException or JsonException or FormatException)
                        {
                            string detail = exception.Message[..Math.Min(exception.Message.Length, 180)];
                            if (frameError != detail) this.Note("Spieldaten-Snapshot verworfen: " + detail);
                            frameError = detail;
                        }
                    }
                    lock (this.stateGate) this.bridgeSeen = null;
                }
                catch (Exception exception) when (exception is InvalidDataException or IOException or SocketException)
                {
                    this.Note("mGBA-Verbindung getrennt: " + exception.Message[..Math.Min(exception.Message.Length, 180)]);
                    lock (this.stateGate) this.bridgeSeen = null;
                }
                finally
                {
                    Volatile.Write(ref this.startedBridgeSession, null);
                    this.items.Disconnect(stream);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        finally { listener.Stop(); }
    }

    private async Task CloseConnectionAsync()
    {
        if (this.connection is not null) await this.connection.DisposeAsync();
        this.connection = null;
        this.session = null;
        this.baseline = true;
        this.evaluated = null;
        this.lastFingerprint = null;
        this.reportedBridge = null;
        this.nextRepair = DateTimeOffset.MinValue;
        lock (this.stateGate) this.run = null;
    }

    private void SaveSettings(ClientSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(this.settingsPath)!);
        string temporary = this.settingsPath + ".tmp";
        File.WriteAllText(temporary, Protocol.Serialize(settings));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, this.settingsPath, true);
        this.settings = settings;
    }

    private void SetState(string value) { lock (this.stateGate) this.state = value; }

    private void Note(string message)
    {
        lock (this.stateGate)
        {
            this.log.Enqueue(DateTimeOffset.Now.ToString("HH:mm:ss") + " " + message);
            while (this.log.Count > 20) this.log.Dequeue();
        }
    }
}
