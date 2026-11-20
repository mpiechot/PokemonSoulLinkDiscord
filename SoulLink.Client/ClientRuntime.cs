using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
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
    private readonly TimeProvider clock;
    private readonly JoinForm? autoJoin;
    private DateTimeOffset nextJoinAttempt;
    private readonly object stateGate = new();
    private readonly Queue<string> log = new();
    private HubConnection? connection;
    private SessionInfo? session;
    private GameSnapshot? latest;
    private string? lastFingerprint;
    private bool baseline = true;
    private bool blocked;
    private long sequence;
    private string state = "Not registered";
    private DateTimeOffset? bridgeSeen;
    private ClientRunView? run;
    private ClientRunView? localView;
    private LocalRunTracker? localTracker;
    private bool bridgeReconnected;
    private readonly TaskCompletionSource<int> bridgeReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ClientRuntime(IConfiguration configuration, TimeProvider? clock = null, JoinForm? autoJoin = null)
    {
        this.clock = clock ?? TimeProvider.System;
        this.autoJoin = autoJoin;
        this.allowSyntheticFrames = configuration.GetValue("AllowSyntheticFrames", false);
        this.enableLocalRun = configuration.GetValue("EnableLocalRun", false);
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
                this.localTracker ??= new LocalRunTracker(this.clock);
                if (this.localTracker.Observe(snapshot)) this.localView = this.localTracker.View();
            }
        }
    }

    public void ResetLocalRun()
    {
        lock (this.stateGate)
        {
            this.localTracker?.Dispose();
            this.localTracker = null;
            this.localView = null;
        }
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

    public Task ChangeArenaAsync(Guid runId, int number, bool complete) =>
        this.RunActionAsync(runId, t => t.ChangeArena(number, complete), c => c.InvokeAsync("ChangeArena", number, complete));

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

    public async Task JoinAsync(JoinForm form)
    {
        ValidateJoin(form);
        await this.connectionGate.WaitAsync();
        try
        {
            if (this.settings.Registration is not null) throw new InvalidOperationException("Already registered. Use a separate SettingsPath for another player or run.");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.PostAsJsonAsync(form.Server.TrimEnd('/') + "/api/soullink/join",
                new JoinRequest(form.Invitation.Trim(), form.Name.Trim(), this.settings.ClientId, this.settings.Token, "AXVD", Marker: form.Marker), Protocol.Json);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Join rejected. Check invitation, player name, edition and lobby state.");
            var registration = await response.Content.ReadFromJsonAsync<Registration>(Protocol.Json)
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
        await this.ReconnectAsync();
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

    public async Task ReconnectAsync()
    {
        await this.connectionGate.WaitAsync();
        try
        {
            await this.CloseConnectionAsync();
            this.blocked = false;
        }
        finally { this.connectionGate.Release(); }
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        await base.StartAsync(cancellationToken);
        await this.bridgeReady.Task.WaitAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bridge = this.ReadBridgeAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (bridge.IsCompleted) await bridge;
                if (this.GetPendingJoin() is { } pending)
                {
                    try { await this.JoinAsync(pending); }
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
                    this.Note("Server unavailable or message rejected (" + exception.GetType().Name + "). Reconnecting without replay.");
                    await this.CloseConnectionAsync();
                }
                finally { this.connectionGate.Release(); }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
        finally
        {
            await this.CloseConnectionAsync();
            await bridge;
        }
    }

    private async Task TickAsync(CancellationToken cancellationToken)
    {
        if (this.settings.Registration is null) return;
        bool reconnect;
        lock (this.stateGate)
        {
            reconnect = this.bridgeReconnected;
            this.bridgeReconnected = false;
        }
        if (reconnect) await this.CloseConnectionAsync();
        if (this.connection is null)
        {
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
            await this.connection.StartAsync(cancellationToken);
            this.session = await this.connection.InvokeAsync<SessionInfo>("Hello", new Hello(this.settings.ClientId), cancellationToken);
            this.sequence = this.session.NextSequence;
            this.baseline = true;
            this.lastFingerprint = null;
            this.SetState("Synchronizing — waiting for mGBA");
        }
        GameSnapshot? snapshot;
        bool fresh;
        lock (this.stateGate)
        {
            snapshot = this.latest;
            fresh = this.bridgeSeen is not null && this.clock.GetUtcNow() - this.bridgeSeen < TimeSpan.FromSeconds(8);
        }
        await this.connection.InvokeAsync<string>("HeartbeatWithBridge", this.session!.SessionId, fresh, cancellationToken);
        if (snapshot is not null && fresh && !this.blocked)
        {
            // Time/frame continue changing; only meaningful observations cross the network.
            string fingerprint = Protocol.Serialize(new
            {
                snapshot.Pokemon,
                snapshot.MapGroup,
                snapshot.MapNumber,
                snapshot.Area,
                snapshot.Position,
                snapshot.InBattle,
                snapshot.Faints,
                snapshot.Epoch,
                snapshot.BadgeMask
            });
            bool pendingDeath = snapshot.Pokemon.Any(p => p.Hp == 0 && p.Container == Placement.Party);
            if (this.baseline || fingerprint != this.lastFingerprint || pendingDeath)
            {
                var observation = new Observation(Guid.NewGuid(), this.session.SessionId, this.sequence, this.baseline, snapshot);
                var result = await this.connection.InvokeAsync<ObservationResult>("Observe", observation, cancellationToken);
                this.SetState(result.State + " — " + result.Message);
                if (result.Accepted)
                {
                    this.sequence = result.NextSequence;
                    this.baseline = false;
                    this.lastFingerprint = fingerprint;
                }
                else if (result.State == "OutOfSync") this.blocked = true;
            }
        }
        var status = await this.connection.InvokeAsync<AutomationRun>("Status", cancellationToken);
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
                lock (this.stateGate)
                {
                    this.bridgeReconnected = true;
                    this.bridgeSeen = null;
                }
                this.Note("mGBA bridge connected on loopback.");
                try
                {
                    await foreach (string line in JsonLines.ReadAsync(peer.GetStream(), cancellationToken))
                    {
                        this.AcceptFrame(Protocol.Deserialize<BridgeFrame>(line));
                    }
                    lock (this.stateGate) this.bridgeSeen = null;
                }
                catch (Exception exception) when (exception is InvalidDataException or JsonException or FormatException or IOException or SocketException)
                {
                    this.Note("Bridge rejected: " + exception.Message[..Math.Min(exception.Message.Length, 180)]);
                    lock (this.stateGate) this.bridgeSeen = null;
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
