using SoulLink;
using SoulLink.Demo;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class AutomationTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "soullink-tests-" + Guid.NewGuid().ToString("N"));
    private readonly AutomationStore store;
    private readonly AutomationEngine engine;

    public AutomationTests()
    {
        this.store = new AutomationStore(Path.Combine(this.directory, "runs.sqlite"));
        this.engine = new AutomationEngine(this.store);
    }

    [Fact]
    public void ThreePlayersCreateExactlyOneDurableGroupIncludingDirectBoxCatch()
    {
        var run = this.engine.Initialize("guild", 123, "Test");
        var clients = Enumerable.Range(0, 3).Select(i => this.Join(run, "Player " + i)).ToArray();
        foreach (var client in clients) this.Send(client, 1, true, Snapshot(1, Mon(1)));
        this.engine.ChangeRun("guild", run.Id, "start");
        foreach (var client in clients)
        {
            var snapshot = Snapshot(2, Mon(1), Mon(2, box: client == clients[2]));
            var observation = new Observation(Guid.NewGuid(), client.Session.SessionId, 2, false, snapshot);
            Assert.True(this.engine.Observe(client.Registration, observation).Accepted);
            Assert.True(this.engine.Observe(client.Registration, observation).Accepted);
        }
        var persisted = new AutomationEngine(new AutomationStore(Path.Combine(this.directory, "runs.sqlite"))).Runs("guild").Single();
        Assert.NotNull(Assert.Single(persisted.Encounters).GroupId);
        Assert.Equal(3, persisted.History.Count(e => e.Kind == "caught"));
        Assert.Single(persisted.History, e => e.Kind == "linked");
        Assert.Equal(4, this.store.Execute(tx => tx.Notifications()).Count);
        Assert.Contains(StatusPresenter.Lines(persisted, _ => "Offline"), line => line.Contains("TEAM CONFLICT"));
    }

    [Fact]
    public void EvolutionNicknameAndPartyBoxMovementPreservePokemonAndLink()
    {
        var (run, client) = this.Active();
        this.Send(client, 2, false, Snapshot(2, Mon(1), Mon(2)));
        var before = this.engine.View(client.Registration);
        var id = before.Players.Single().Pokemon.Single(p => p.State.Identity == Mon(2).Identity).Id;
        this.Send(client, 3, false, Snapshot(3, Mon(1), Mon(2, box: true) with { Species = 281, Nickname = "Renamed" }));
        var after = this.engine.View(client.Registration);
        Assert.Equal(2, after.Players.Single().Pokemon.Count);
        Assert.Equal(id, after.Encounters.Single().Members[client.Registration.PlayerId]);
        Assert.Single(after.History, e => e.Kind == "caught");
        Assert.Single(after.History, e => e.Kind == "evolved");
    }

    [Fact]
    public void CatchWithFullPartyIsFoundInBox()
    {
        var run = this.engine.Initialize("guild", 0, "Test");
        var client = this.Join(run, "Player");
        var party = Enumerable.Range(1, 6).Select(i => Mon((uint)i, slot: i - 1)).ToArray();
        this.Send(client, 1, true, Snapshot(1, party));
        this.engine.ChangeRun("guild", run.Id, "start");
        this.Send(client, 2, false, Snapshot(2, party.Append(Mon(7, box: true)).ToArray()));
        Assert.Single(this.engine.View(client.Registration).History, e => e.Kind == "caught");
    }

    [Fact]
    public void SequenceGapBlocksAutomationAcrossReconnectUntilExplicitRebaseline()
    {
        var (run, client) = this.Active();
        var result = this.Send(client, 4, false, Snapshot(2, Mon(1), Mon(2)));
        Assert.Equal("OutOfSync", result.State);
        var session = this.engine.Connect(client.Registration, new Hello(client.Registration.ClientId));
        var retry = this.engine.Observe(client.Registration, new Observation(Guid.NewGuid(), session.SessionId, 2, true, Snapshot(2, Mon(1))));
        Assert.False(retry.Accepted);
        this.engine.Rebaseline("guild", run.Id, client.Registration.PlayerId);
        session = this.engine.Connect(client.Registration, new Hello(client.Registration.ClientId));
        Assert.True(this.engine.Observe(client.Registration, new Observation(Guid.NewGuid(), session.SessionId, 2, true, Snapshot(2, Mon(1), Mon(2)))).Accepted);
        Assert.Empty(this.engine.View(client.Registration).Encounters);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("clock")]
    [InlineData("experience")]
    [InlineData("epoch")]
    public void RollbacksAndMissingPokemonPauseWithoutFalseEvents(string kind)
    {
        var (_, client) = this.Active();
        var snapshot = kind switch
        {
            "missing" => Snapshot(2),
            "clock" => Snapshot(2, Mon(1)) with { PlaySeconds = 0, Frame = 0 },
            "experience" => Snapshot(2, Mon(1) with { Experience = 1 }),
            _ => Snapshot(2, Mon(1)) with { Epoch = "different" },
        };
        Assert.Equal("OutOfSync", this.Send(client, 2, false, snapshot).State);
        Assert.Empty(this.engine.View(client.Registration).History);
    }

    [Fact]
    public void ServerRestartRequiresBaselineAndDoesNotReplayOfflineCatches()
    {
        var (_, client) = this.Active();
        var restarted = new AutomationEngine(this.store);
        Assert.Throws<UnauthorizedAccessException>(() => restarted.Observe(client.Registration,
            new Observation(Guid.NewGuid(), client.Session.SessionId, 2, false, Snapshot(2, Mon(1)))));
        var session = restarted.Connect(client.Registration, new Hello(client.Registration.ClientId));
        Assert.False(restarted.Observe(client.Registration, new Observation(Guid.NewGuid(), session.SessionId, 2, false, Snapshot(2, Mon(1)))).Accepted);
        Assert.Equal("OutOfSync", restarted.Observe(client.Registration,
            new Observation(Guid.NewGuid(), session.SessionId, 2, true, Snapshot(2, Mon(1), Mon(2)))).State);
    }

    [Fact]
    public void ReconnectWithSameInventoryContinuesWithoutImportingCatches()
    {
        var (_, client) = this.Active();
        var session = this.engine.Connect(client.Registration, new Hello(client.Registration.ClientId));
        Assert.True(this.engine.Observe(client.Registration, new Observation(Guid.NewGuid(), session.SessionId, 2, true, Snapshot(2, Mon(1)))).Accepted);
        Assert.True(this.engine.Observe(client.Registration, new Observation(Guid.NewGuid(), session.SessionId, 3, false, Snapshot(3, Mon(1), Mon(2)))).Accepted);
        Assert.Single(this.engine.View(client.Registration).History, e => e.Kind == "caught");
    }

    [Fact]
    public void ExtraCatchAndMissedEncounterNeverCreateAdditionalLinks()
    {
        var (run, client) = this.Active();
        this.engine.SetEncounterFailed("guild", run.Id, 17);
        this.Send(client, 2, false, Snapshot(2, Mon(1), Mon(2) with { MetArea = 17 }));
        Assert.Null(this.engine.View(client.Registration).Encounters.Single().GroupId);
        this.Send(client, 3, false, Snapshot(3, Mon(1), Mon(2) with { MetArea = 17 }, Mon(3, slot: 2)));
        this.Send(client, 4, false, Snapshot(4, Mon(1), Mon(2) with { MetArea = 17 }, Mon(3, slot: 2), Mon(4, slot: 3)));
        Assert.Equal(2, this.engine.View(client.Registration).Encounters.Count);
        Assert.Equal(2, this.engine.View(client.Registration).History.Count(e => e.Kind == "extra-catch"));
    }

    [Theory]
    [InlineData(DeathRule.Disabled, false)]
    [InlineData(DeathRule.ZeroHpOutsideBattle, true)]
    public void DeathIsConfigurableRequiresStableZeroHpAndDoesNotReviveOnHealing(DeathRule rule, bool expected)
    {
        var (run, client) = this.Active(rule);
        this.Send(client, 2, false, Snapshot(2, Mon(1), Mon(2)));
        var zero = Snapshot(3, Mon(1), Mon(2) with { Hp = 0 });
        this.Send(client, 3, false, zero with { InBattle = true });
        Assert.False(this.engine.View(client.Registration).Encounters.Single().Dead);
        this.Send(client, 4, false, zero with { Frame = 4, PlaySeconds = 4 });
        Assert.False(this.engine.View(client.Registration).Encounters.Single().Dead);
        this.Send(client, 5, false, zero with { Frame = 5, PlaySeconds = 5 });
        this.Send(client, 6, false, Snapshot(6, Mon(1), Mon(2)));
        Assert.Equal(expected, this.engine.View(client.Registration).Encounters.Single().Dead);
    }

    [Fact]
    public void OwnershipAndRunLifecycleAreIsolated()
    {
        var (first, client) = this.Active();
        var other = this.engine.Initialize("other-guild", 0, "Other");
        Assert.Throws<UnauthorizedAccessException>(() => this.engine.ChangeRun("other-guild", first.Id, "end"));
        Assert.Throws<UnauthorizedAccessException>(() => this.engine.Authenticate("wrong"));
        Assert.Throws<UnauthorizedAccessException>(() => this.engine.Connect(client.Registration, new Hello(Guid.NewGuid())));
        Assert.Throws<UnauthorizedAccessException>(() => this.engine.Join(new JoinRequest(first.Invitation, "Late", Guid.NewGuid(), new string('A', 64), "AXVD")));
        var next = this.engine.Initialize("guild", 0, "Next");
        Assert.Equal(RunPhase.Paused, this.engine.Runs("guild").Single(r => r.Id == first.Id).Phase);
        Assert.Equal("Paused", this.Send(client, 2, false, Snapshot(2, Mon(1))).State);
        this.engine.ChangeRun("guild", first.Id, "load");
        Assert.Single(this.engine.Runs("guild"));
        Assert.Single(this.engine.Runs("other-guild"));
    }

    [Fact]
    public void DuplicateIdentityAndSlotAreRejectedWithoutMutation()
    {
        var (_, client) = this.Active();
        Assert.Throws<InvalidDataException>(() => this.Send(client, 2, false, Snapshot(2, Mon(1), Mon(1))));
        Assert.Throws<InvalidDataException>(() => this.Send(client, 2, false, Snapshot(2, Mon(1), Mon(2, slot: 0))));
        Assert.Equal(1, this.engine.View(client.Registration).Players.Single().Sequence);
    }

    [Fact]
    public void PersistenceTransactionRollsBackUnacknowledgedMutations()
    {
        var run = this.engine.Initialize("guild", 0, "Original");
        Assert.Throws<IOException>(() => this.store.Execute<bool>(tx =>
        {
            var copy = tx.Run(run.Id);
            copy.Name = "Uncommitted";
            tx.Save(copy);
            throw new IOException("Simulated failure.");
        }));
        Assert.Equal("Original", this.engine.Runs("guild").Single().Name);
    }

    [Fact]
    public void SameRegistrationRetryIsIdempotentAndTokenIsNotStoredInRun()
    {
        var run = this.engine.Initialize("guild", 0, "Test");
        var request = new JoinRequest(run.Invitation, "Player", Guid.NewGuid(), new string('A', 64), "AXVD");
        var first = this.engine.Join(request);
        Assert.Equal(first, this.engine.Join(request));
        Assert.DoesNotContain(request.Token, Protocol.Serialize(this.engine.View(first)));
    }

    [Fact]
    public void ConfirmedDeathAndUndoPreserveMembershipAndPokemonIdentity()
    {
        var (run, client) = this.Active();
        this.Send(client, 2, false, Snapshot(2, Mon(1), Mon(2)));
        var before = this.engine.View(client.Registration).Encounters.Single();
        this.engine.SetDeath("guild", run.Id, 16, true);
        Assert.True(this.engine.View(client.Registration).Encounters.Single().Dead);
        Assert.Throws<UnauthorizedAccessException>(() => this.engine.SetDeath("other", run.Id, 16, false));
        this.engine.SetDeath("guild", run.Id, 16, false);
        var after = this.engine.View(client.Registration).Encounters.Single();
        Assert.False(after.Dead);
        Assert.Equal(before.GroupId, after.GroupId);
        Assert.Equal(before.Members[client.Registration.PlayerId], after.Members[client.Registration.PlayerId]);
    }

    [Fact]
    public void ReusingOneFaintedFrameCannotConfirmADeath()
    {
        var (_, client) = this.Active(DeathRule.ZeroHpOutsideBattle);
        this.Send(client, 2, false, Snapshot(2, Mon(1), Mon(2)));
        var zero = Snapshot(3, Mon(1), Mon(2) with { Hp = 0 });
        this.Send(client, 3, false, zero);
        Assert.False(this.Send(client, 4, false, zero).Accepted);
        Assert.False(this.engine.View(client.Registration).Encounters.Single().Dead);
        this.Send(client, 4, false, zero with { Frame = 4, PlaySeconds = 4 });
        Assert.True(this.engine.View(client.Registration).Encounters.Single().Dead);
    }

    public void Dispose() => Directory.Delete(this.directory, true);

    private static PokemonState Mon(uint pid, bool box = false, int? slot = null) =>
        Gen3Parser.Parse(SyntheticPokemon.Bytes(pid, 280, box: box), box ? Placement.Box : Placement.Party, slot ?? (pid == 1 ? 0 : 1), box ? 0 : null)!;

    private static GameSnapshot Snapshot(long frame, params PokemonState[] pokemon) => new()
    {
        LayoutId = "fixture", Epoch = "fixture", Frame = frame, PlaySeconds = frame, Area = 16,
        Pokemon = pokemon.ToList(),
    };

    private (AutomationRun Run, TestClient Client) Active(DeathRule rule = DeathRule.Disabled)
    {
        var run = this.engine.Initialize("guild", 0, "Test", rule);
        var client = this.Join(run, "Player");
        this.Send(client, 1, true, Snapshot(1, Mon(1)));
        this.engine.ChangeRun("guild", run.Id, "start");
        return (run, client);
    }

    private TestClient Join(AutomationRun run, string name)
    {
        Guid id = Guid.NewGuid();
        var registration = this.engine.Join(new JoinRequest(run.Invitation, name, id, Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), "AXVD"));
        return new TestClient(registration, this.engine.Connect(registration, new Hello(id)));
    }

    private ObservationResult Send(TestClient client, long sequence, bool baseline, GameSnapshot snapshot) =>
        this.engine.Observe(client.Registration, new Observation(Guid.NewGuid(), client.Session.SessionId, sequence, baseline, snapshot));

    private sealed record TestClient(Registration Registration, SessionInfo Session);
}
