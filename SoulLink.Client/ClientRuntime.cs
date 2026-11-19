using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.SignalR.Client;
using SoulLink;

namespace SoulLink.Client;

public sealed record JoinForm(string Server, string Invitation, string Name);

public sealed class ClientSettings
{
    public string Server { get; set; } = "http://127.0.0.1:5080";
    public string Name { get; set; } = "";
    public Guid ClientId { get; set; } = Guid.NewGuid();
    public string Token { get; set; } = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public Registration? Registration { get; set; }
}

/// <summary>One local bridge, one authenticated server session, and no offline event queue.</summary>
public sealed class ClientRuntime : BackgroundService
{
    private readonly string settingsPath;
    private readonly int bridgePort;
    private readonly SemaphoreSlim connectionGate = new(1);
    private readonly ClientSettings settings;
    private readonly bool allowSyntheticFrames;
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
    private AutomationRun? run;
    private bool bridgeReconnected;
    private readonly TaskCompletionSource bridgeReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public ClientRuntime(IConfiguration configuration, TimeProvider? clock = null, JoinForm? autoJoin = null)
    {
        this.clock = clock ?? TimeProvider.System;
        this.autoJoin = autoJoin;
        this.allowSyntheticFrames = configuration.GetValue("AllowSyntheticFrames", false);
        this.bridgePort = configuration.GetValue("BridgePort", 5091);
        this.settingsPath = Path.GetFullPath(configuration["SettingsPath"] ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoulLink", "client.json"));
        this.settings = File.Exists(this.settingsPath) ? Protocol.Deserialize<ClientSettings>(File.ReadAllText(this.settingsPath)) : new ClientSettings();
        this.SaveSettings();
    }

    public ClientView Status()
    {
        lock (this.stateGate)
        {
            bool live = this.bridgeSeen is not null && this.clock.GetUtcNow() - this.bridgeSeen < TimeSpan.FromSeconds(10);
            return new ClientView(
                this.settings.Registration is null ? this.autoJoin?.Name ?? this.settings.Name : this.settings.Name,
                this.settings.Registration is null ? this.autoJoin?.Server ?? this.settings.Server : this.settings.Server,
                this.settings.Registration is not null,
                this.state, this.bridgePort, live ? "Online" : "Waiting for mGBA", this.bridgeSeen,
                live ? this.latest : null,
                this.run is null ? null : new ClientRunView(this.run.Name, this.run.Phase, this.run.Id,
                    this.run.Players.Select(p => new ClientPlayerView(p.Name, p.Id, p.Diagnostic, p.Connection)).ToArray(),
                    StatusPresenter.Lines(this.run, p => p.Connection).ToArray(), this.run.History.TakeLast(12).ToArray()),
                this.log.ToArray(), this.allowSyntheticFrames);
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
        }
    }

    public async Task JoinAsync(JoinForm form)
    {
        if (!Uri.TryCreate(form.Server, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidDataException("Use an HTTPS server URL; HTTP is allowed only on loopback.");
        await this.connectionGate.WaitAsync();
        try
        {
            if (this.settings.Registration is not null) throw new InvalidOperationException("Already registered. Use a separate SettingsPath for another player or run.");
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var response = await http.PostAsJsonAsync(form.Server.TrimEnd('/') + "/api/soullink/join",
                new JoinRequest(form.Invitation.Trim(), form.Name.Trim(), this.settings.ClientId, this.settings.Token, "AXVD"), Protocol.Json);
            if (!response.IsSuccessStatusCode) throw new InvalidOperationException("Join rejected. Check invitation, player name, edition and lobby state.");
            this.settings.Registration = await response.Content.ReadFromJsonAsync<Registration>(Protocol.Json)
                ?? throw new InvalidDataException("Server returned an empty registration.");
            this.settings.Server = form.Server.TrimEnd('/');
            this.settings.Name = form.Name.Trim();
            this.SaveSettings();
            this.Note("Registration saved. Connecting to the server.");
        }
        finally { this.connectionGate.Release(); }
        await this.ReconnectAsync();
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
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                if (bridge.IsCompleted) await bridge;
                if (this.settings.Registration is null && this.autoJoin is not null && this.clock.GetUtcNow() >= this.nextJoinAttempt)
                {
                    this.nextJoinAttempt = this.clock.GetUtcNow().AddSeconds(10);
                    try { await this.JoinAsync(this.autoJoin); }
                    catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
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
        await this.connection.InvokeAsync<string>("Heartbeat", this.session!.SessionId, cancellationToken);
        GameSnapshot? snapshot;
        bool fresh;
        lock (this.stateGate)
        {
            snapshot = this.latest;
            fresh = this.bridgeSeen is not null && this.clock.GetUtcNow() - this.bridgeSeen < TimeSpan.FromSeconds(8);
        }
        if (snapshot is not null && fresh && !this.blocked)
        {
            // Time/frame continue changing; only meaningful observations cross the network.
            string fingerprint = Protocol.Serialize(new { snapshot.Pokemon, snapshot.MapGroup, snapshot.MapNumber, snapshot.Area, snapshot.InBattle, snapshot.Epoch });
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
        lock (this.stateGate) this.run = status;
    }

    private async Task ReadBridgeAsync(CancellationToken cancellationToken)
    {
        var listener = new TcpListener(IPAddress.Loopback, this.bridgePort);
        try
        {
            listener.Start(1);
            this.bridgeReady.TrySetResult();
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

    private void SaveSettings()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(this.settingsPath)!);
        string temporary = this.settingsPath + ".tmp";
        File.WriteAllText(temporary, Protocol.Serialize(this.settings));
        if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(temporary, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        File.Move(temporary, this.settingsPath, true);
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
