using SoulLink;

namespace SoulLink.Client;

/// <summary>Runs the normal observation rules in a private database scoped to a local game.</summary>
internal sealed class LocalRunTracker : IDisposable
{
    private const string Guild = "local-playtest";
    private readonly string? temporaryDatabase;
    private readonly AutomationEngine engine;
    private readonly Registration registration;
    private SessionInfo session;
    private GameSnapshot? previous;
    private GameSnapshot? latest;
    private bool started;
    private long sequence;

    public LocalRunTracker(TimeProvider clock, string? database = null)
    {
        if (database is null)
            database = this.temporaryDatabase = Path.Combine(Path.GetTempPath(), $"soullink-preview-{Guid.NewGuid():N}.sqlite");
        this.engine = new AutomationEngine(new AutomationStore(database), clock);
        var run = this.engine.Runs(Guild).SingleOrDefault() ?? this.engine.Initialize(Guild, 0, "Lokaler Spieltest", DeathRule.ConfirmFaint);
        this.started = run.Phase != RunPhase.Lobby;
        if (run.Players.SingleOrDefault() is { } player)
        {
            this.registration = new Registration(run.Id, player.Id, player.ClientId);
            this.engine.Rebaseline(Guild, run.Id, player.Id);
        }
        else
        {
            this.registration = this.engine.Join(new JoinRequest(run.Invitation, "Du", Guid.NewGuid(), new string('0', 64), "AXVD"));
        }
        this.session = this.engine.Connect(this.registration, new Hello(this.registration.ClientId));
        this.sequence = this.session.NextSequence;
    }

    public Guid PlayerId => this.registration.PlayerId;

    public ClientRunView View() => DashboardProjection.FromRun(this.engine.View(this.registration));

    public bool Observe(GameSnapshot snapshot)
    {
        bool baseline = this.previous is null;
        if (this.latest is { } old && TimelineChanged(old, snapshot))
        {
            this.engine.Rebaseline(Guild, this.registration.RunId, this.registration.PlayerId);
            this.session = this.engine.Connect(this.registration, new Hello(this.registration.ClientId));
            this.sequence = this.session.NextSequence;
            baseline = true;
        }
        this.latest = snapshot;
        try { this.engine.Heartbeat(this.registration, this.session.SessionId); }
        catch (UnauthorizedAccessException)
        {
            this.engine.Rebaseline(Guild, this.registration.RunId, this.registration.PlayerId);
            this.session = this.engine.Connect(this.registration, new Hello(this.registration.ClientId));
            this.sequence = this.session.NextSequence;
            baseline = true;
        }
        if (!baseline && this.previous is { } previous &&
            (previous.Frame == snapshot.Frame || !Changed(previous, snapshot))) return false;
        var result = this.engine.Observe(this.registration, new Observation(Guid.NewGuid(), this.session.SessionId, this.sequence, baseline, snapshot));
        if (!result.Accepted) return false;
        if (!this.started)
        {
            this.engine.ChangeRun(Guild, this.registration.RunId, "start");
            this.started = true;
        }
        this.sequence = result.NextSequence;
        this.previous = snapshot;
        return true;
    }

    public CatchCheck CheckCatch(string name) => this.engine.CheckCatch(this.registration, name);

    public void AddRule(string text) => this.engine.AddRule(this.registration, text);

    public void ChangeArena(int number, bool complete) => this.engine.ChangeArena(this.registration, number, complete);

    public void ResolveFaint(Guid pokemonId, Guid incidentId, bool confirm, string? reason) =>
        this.engine.ResolveFaint(this.registration, pokemonId, incidentId, confirm, reason);

    private static bool TimelineChanged(GameSnapshot previous, GameSnapshot next) =>
        previous.Epoch != next.Epoch || next.Frame < previous.Frame ||
        next.PlaySeconds + 1 < previous.PlaySeconds ||
        // At ordinary GBA speed, play time cannot advance this far in so few frames.
        next.PlaySeconds - previous.PlaySeconds > (next.Frame - previous.Frame) / 50 + 2;

    private static bool Changed(GameSnapshot previous, GameSnapshot next) =>
        previous.Position != next.Position || previous.MapGroup != next.MapGroup || previous.MapNumber != next.MapNumber ||
        previous.Money != next.Money ||
        previous.InBattle != next.InBattle || previous.BadgeMask != next.BadgeMask || !previous.Pokemon.SequenceEqual(next.Pokemon) ||
        previous.Battle?.Key != next.Battle?.Key ||
        previous.Battle?.CatchEncounter != next.Battle?.CatchEncounter ||
        !(previous.Battle?.Combatants ?? []).SequenceEqual(next.Battle?.Combatants ?? []) ||
        !(previous.Faints ?? []).SequenceEqual(next.Faints ?? []) ||
        !(previous.WildBattles ?? []).SequenceEqual(next.WildBattles ?? []) ||
        next.Pokemon.Any(p => p is { Hp: 0, IsEgg: false });

    public void Dispose()
    {
        if (this.temporaryDatabase is not null) File.Delete(this.temporaryDatabase);
    }
}
