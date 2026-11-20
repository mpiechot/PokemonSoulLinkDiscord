using SoulLink;

namespace SoulLink.Client;

/// <summary>Runs the normal observation rules in a private database for the current local playtest.</summary>
internal sealed class LocalRunTracker : IDisposable
{
    private const string Guild = "local-playtest";
    private readonly string database = Path.Combine(Path.GetTempPath(), $"soullink-preview-{Guid.NewGuid():N}.sqlite");
    private readonly AutomationEngine engine;
    private readonly Registration registration;
    private SessionInfo session;
    private GameSnapshot? previous;
    private long sequence;

    public LocalRunTracker(TimeProvider clock)
    {
        this.engine = new AutomationEngine(new AutomationStore(this.database), clock);
        var run = this.engine.Initialize(Guild, 0, "Lokaler Spieltest", DeathRule.ConfirmFaint);
        this.registration = this.engine.Join(new JoinRequest(run.Invitation, "Du", Guid.NewGuid(), new string('0', 64), "AXVD"));
        this.session = this.engine.Connect(this.registration, new Hello(this.registration.ClientId));
        this.sequence = this.session.NextSequence;
    }

    public Guid PlayerId => this.registration.PlayerId;

    public ClientRunView View() => DashboardProjection.FromRun(this.engine.View(this.registration));

    public bool Observe(GameSnapshot snapshot)
    {
        bool baseline = this.previous is null;
        if (this.previous is { } old &&
            (old.Epoch != snapshot.Epoch || snapshot.Frame < old.Frame || snapshot.PlaySeconds + 1 < old.PlaySeconds))
        {
            this.engine.Rebaseline(Guild, this.registration.RunId, this.registration.PlayerId);
            this.session = this.engine.Connect(this.registration, new Hello(this.registration.ClientId));
            this.sequence = this.session.NextSequence;
            baseline = true;
        }
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
        if (this.previous is null) this.engine.ChangeRun(Guild, this.registration.RunId, "start");
        this.sequence = result.NextSequence;
        this.previous = snapshot;
        return true;
    }

    public CatchCheck CheckCatch(string name) => this.engine.CheckCatch(this.registration, name);

    public void ChangeArena(int number, bool complete) => this.engine.ChangeArena(this.registration, number, complete);

    public void ResolveFaint(Guid pokemonId, Guid incidentId, bool confirm, string? reason) =>
        this.engine.ResolveFaint(this.registration, pokemonId, incidentId, confirm, reason);

    private static bool Changed(GameSnapshot previous, GameSnapshot next) =>
        previous.Position != next.Position || previous.MapGroup != next.MapGroup || previous.MapNumber != next.MapNumber ||
        previous.InBattle != next.InBattle || previous.BadgeMask != next.BadgeMask || !previous.Pokemon.SequenceEqual(next.Pokemon) ||
        next.Pokemon.Any(p => p is { Hp: 0, IsEgg: false });

    public void Dispose() => File.Delete(this.database);
}
