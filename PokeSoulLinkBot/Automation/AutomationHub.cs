using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using PokeSoulLinkBot.Core.Configuration;
using SoulLink;

namespace PokeSoulLinkBot.Automation;

/// <summary>Exposes authenticated run observations and scoped run actions.</summary>
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

    /// <inheritdoc />
    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (this.Context.Items.TryGetValue("registration", out object? registration) && registration is Registration player &&
            this.Context.Items.TryGetValue("sessionId", out object? session) && session is Guid sessionId)
        {
            this.engine.Disconnect(player, sessionId);
        }

        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Starts a session requiring a fresh inventory baseline.</summary>
    public SessionInfo Hello(Hello hello)
    {
        var session = this.engine.Connect(this.Registration(), hello);
        this.Context.Items["sessionId"] = session.SessionId;
        return session;
    }

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

    /// <summary>Checks the catch family against the caller's run.</summary>
    public CatchCheck CheckCatch(string name) => this.engine.CheckCatch(this.Registration(), name);

    /// <summary>Adds a shared rule to the caller's run.</summary>
    public void AddRule(string text)
    {
        this.RequireReadTracking();
        try
        {
            this.engine.AddRule(this.Registration(), text);
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            throw new HubException(error.Message);
        }
    }

    /// <summary>Updates shared arena progress for the caller's run.</summary>
    public void ChangeArena(int number, bool complete)
    {
        this.RequireReadTracking();
        this.engine.ChangeArena(this.Registration(), number, complete);
    }

    /// <summary>Confirms or dismisses the registered player's pending faint.</summary>
    public void ResolveFaint(Guid pokemonId, Guid incidentId, bool confirm, string? reason)
    {
        this.RequireReadTracking();
        this.engine.ResolveFaint(this.Registration(), pokemonId, incidentId, confirm, reason);
    }

    /// <summary>Returns only the caller's run.</summary>
    public AutomationRun Status()
    {
        var registration = this.Registration();
        var run = this.engine.View(registration);
        foreach (var player in run.Players)
        {
            player.DisplayArea = player.LastSnapshot?.Area;
            player.DisplayPosition = player.LastSnapshot?.Position;
            player.DisplayBadges = player.LastSnapshot?.BadgeMask;
            if (player.Id != registration.PlayerId)
            {
                foreach (var pokemon in player.Pokemon)
                {
                    pokemon.PendingDeath = null;
                    pokemon.FaintBattle = null;
                }
            }

            player.LastSnapshot = null;
            player.Connection = this.engine.Connection(player);
        }

        return run;
    }

    private Registration Registration() => this.Context.Items["registration"] as Registration
        ?? throw new HubException("Authentication required.");

    private void RequireReadTracking()
    {
        if (!this.options.Value.Enabled || !this.options.Value.EnableReadTracking)
        {
            throw new HubException("Read tracking is disabled.");
        }
    }
}
