using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using PokeSoulLinkBot.Core.Configuration;
using SoulLink;

namespace PokeSoulLinkBot.Automation;

/// <summary>Allows only authenticated, run-bound observation messages; no write command exists.</summary>
public sealed class AutomationHub : Hub
{
    private readonly AutomationEngine engine;
    private readonly IOptions<SoulLinkOptions> options;

    /// <summary>Initializes the run-bound transport adapter.</summary>
    public AutomationHub(AutomationEngine engine, IOptions<SoulLinkOptions> options)
    {
        this.engine = engine;
        this.options = options;
    }

    /// <inheritdoc />
    public override async Task OnConnectedAsync()
    {
        try
        {
            var http = this.Context.GetHttpContext() ?? throw new UnauthorizedAccessException();
            string authorization = http.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.Ordinal))
            {
                throw new UnauthorizedAccessException();
            }

            var registration = this.engine.Authenticate(authorization[7..]);
            this.Context.Items["registration"] = registration;
            await base.OnConnectedAsync();
        }
        catch (UnauthorizedAccessException)
        {
            this.Context.Abort();
        }
    }

    /// <summary>Starts a session requiring a fresh inventory baseline.</summary>
    public SessionInfo Hello(Hello hello) => this.engine.Connect(this.Registration(), hello);

    /// <summary>Accepts a sequenced, idempotent game observation.</summary>
    public ObservationResult Observe(Observation observation)
    {
        if (!this.options.Value.Enabled || !this.options.Value.EnableReadTracking)
        {
            throw new HubException("Read tracking is disabled.");
        }

        var now = DateTimeOffset.UtcNow;
        if (this.Context.Items.TryGetValue("lastObservation", out object? last) &&
            now - (DateTimeOffset)last! < TimeSpan.FromMilliseconds(150))
        {
            throw new HubException("Observation rate exceeded.");
        }

        this.Context.Items["lastObservation"] = now;
        return this.engine.Observe(this.Registration(), observation);
    }

    /// <summary>Updates liveness independently of game changes.</summary>
    public string Heartbeat(Guid sessionId) => this.engine.Heartbeat(this.Registration(), sessionId);

    /// <summary>Updates session liveness and reports whether the bridge has current game data.</summary>
    public string HeartbeatWithBridge(Guid sessionId, bool available)
        => this.engine.Heartbeat(this.Registration(), sessionId, available);

    /// <summary>Returns only the caller's run.</summary>
    public AutomationRun Status()
    {
        var run = this.engine.View(this.Registration());
        foreach (var player in run.Players)
        {
            player.DisplayArea = player.LastSnapshot?.Area;
            player.LastSnapshot = null;
            player.Connection = this.engine.Connection(player);
        }

        return run;
    }

    private Registration Registration() => this.Context.Items["registration"] as Registration
        ?? throw new HubException("Authentication required.");
}
