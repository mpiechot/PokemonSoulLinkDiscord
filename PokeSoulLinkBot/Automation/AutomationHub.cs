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
    private readonly RunChangeBroadcast updates;

    /// <summary>Initializes the run-bound transport adapter.</summary>
    public AutomationHub(AutomationEngine engine, IOptions<SoulLinkOptions> options, RunChangeBroadcast updates)
    {
        this.engine = engine;
        this.options = options;
        this.updates = updates;
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
            this.updates.Unregister(player.PlayerId, this.Context.ConnectionId);
            this.updates.Notify(player.RunId);
        }

        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>Starts a session requiring a fresh inventory baseline.</summary>
    public SessionInfo Hello(Hello hello)
    {
        var player = this.Registration();
        var session = this.engine.Connect(player, hello);
        this.Context.Items["sessionId"] = session.SessionId;
        this.updates.Register(player.PlayerId, this.Context.ConnectionId);
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
    {
        var player = this.Registration();
        string result = this.engine.Heartbeat(player, sessionId, available);
        if (!this.Context.Items.TryGetValue("bridgeAvailable", out object? previous) || !Equals(previous, available))
        {
            this.Context.Items["bridgeAvailable"] = available;
            this.updates.Notify(player.RunId);
        }

        return result;
    }

    /// <summary>Checks the catch family against the caller's run.</summary>
    public CatchCheck CheckCatch(string name) => this.engine.CheckCatch(this.Registration(), name);

    /// <summary>Requests ROM details from a participant for one of their known Pokémon.</summary>
    public async Task<PokemonRomDetails?> PokemonDetails(Guid playerId, int species)
    {
        var requester = this.Registration();
        if (requester.PlayerId == playerId || !SpeciesCatalog.IsValid(species) ||
            !this.engine.View(requester).Players.Any(player => player.Id == playerId && this.engine.Connection(player) == "Ready" &&
                (player.Pokemon.Any(mon => !mon.State.IsEgg && mon.State.Species == species) ||
                    player.LastSnapshot?.Battle?.ForSpectator().Combatants.Any(mon => mon.Species == species) == true)) ||
            this.updates.Peer(playerId) is not { } connection)
        {
            return null;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(this.Context.ConnectionAborted);
        timeout.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var details = await this.Clients.Client(connection).InvokeAsync<PokemonRomDetails?>("ReadPokemonDetails", species, timeout.Token);
            return details?.IsValid() == true ? details : null;
        }
        catch (Exception error) when (error is HubException or OperationCanceledException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Adds a shared rule to the caller's run.</summary>
    public void AddRule(string text) => this.UpdateRules(registration => this.engine.AddRule(registration, text));

    /// <summary>Replaces or removes an existing shared rule in the caller's run.</summary>
    public void ChangeRule(string current, string? replacement) =>
        this.UpdateRules(registration => this.engine.ChangeRule(registration, current, replacement));

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

    /// <summary>Finishes a wiped run for the player who lost its last link.</summary>
    public void CompleteWipedRun(Guid incidentId, string? reason)
    {
        this.RequireReadTracking();
        try
        {
            this.engine.CompleteWipedRun(this.Registration(), incidentId, reason);
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            throw new HubException(error.Message);
        }
    }

    /// <summary>Returns only the caller's run.</summary>
    public async Task<AutomationRun> Subscribe()
    {
        await this.Groups.AddToGroupAsync(this.Context.ConnectionId, RunChangeBroadcast.Group(this.Registration().RunId), this.Context.ConnectionAborted);
        return this.Status();
    }

    /// <summary>Returns only the caller's run.</summary>
    public AutomationRun Status()
    {
        var registration = this.Registration();
        var run = this.engine.View(registration);
        if (run.PendingEnd?.PlayerId != registration.PlayerId)
        {
            run.PendingEnd = null;
        }

        foreach (var player in run.Players)
        {
            player.Connection = this.engine.Connection(player);
            player.DisplayArea = player.LastSnapshot?.Area;
            player.DisplayPosition = player.LastSnapshot?.Position;
            player.DisplayBadges = player.LastSnapshot?.BadgeMask;
            player.DisplayMoney = player.LastSnapshot?.Money;
            player.DisplayBattle = run.Phase == RunPhase.Active && player.Connection == "Ready" ? player.LastSnapshot?.Battle : null;
            if (player.Id == registration.PlayerId && player.Connection == "Ready")
            {
                player.DisplayCatchChecks = EncounterCatchChecks.ForPlayer(run, player);
            }

            if (player.Id != registration.PlayerId)
            {
                player.DisplayBattle = player.DisplayBattle?.ForSpectator();
                foreach (var pokemon in player.Pokemon)
                {
                    pokemon.PendingDeath = null;
                    pokemon.FaintBattle = null;
                }
            }

            player.LastSnapshot = null;
        }

        return run;
    }

    private void UpdateRules(Action<Registration> update)
    {
        this.RequireReadTracking();
        try
        {
            update(this.Registration());
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException)
        {
            throw new HubException(error.Message);
        }
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
