using Microsoft.Data.Sqlite;
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
    public void StandaloneGroupsRetainTheirOwnLobbyRulesAndRunWhenSwitching()
    {
        var groupOne = this.engine.CreateGroup("Gruppe eins");
        var groupTwo = this.engine.CreateGroup("Gruppe zwei");
        var one = this.engine.CreateGroupRun(groupOne.Id, new GroupRunRequest("Rubin eins"));
        var two = this.engine.CreateGroupRun(groupTwo.Id, new GroupRunRequest("Rubin zwei"));
        var first = JoinGroup(one, "Erster");
        var second = JoinGroup(two, "Zweiter");
        this.engine.ChangeGroupRun(groupOne.Id, one.Id, "start");
        this.engine.AddRule(first, "Regel für Gruppe eins");
        this.engine.ChangeGroupRun(groupOne.Id, one.Id, "pause");
        Assert.Equal(RunPhase.Lobby, Assert.Single(this.engine.GroupRuns(groupTwo.Id)).Phase);
        this.engine.ChangeGroupRun(groupTwo.Id, two.Id, "start");
        this.engine.AddRule(second, "Regel für Gruppe zwei");
        this.engine.ChangeGroupRun(groupTwo.Id, two.Id, "pause");
        this.engine.ChangeGroupRun(groupOne.Id, one.Id, "load");

        Assert.Equal(RunPhase.Active, Assert.Single(this.engine.GroupRuns(groupOne.Id)).Phase);
        Assert.Equal(RunPhase.Paused, Assert.Single(this.engine.GroupRuns(groupTwo.Id)).Phase);
        Assert.Equal("Regel für Gruppe eins", Assert.Single(this.engine.View(first).AdditionalRules));
        Assert.Equal("Regel für Gruppe zwei", Assert.Single(this.engine.View(second).AdditionalRules));
        Assert.Throws<UnauthorizedAccessException>(() => this.engine.ChangeGroupRun(groupOne.Id, two.Id, "end"));

        var open = this.engine.CreateGroupRun(groupTwo.Id, new GroupRunRequest("Lobby behalten"));
        this.engine.CreateGroupRun(groupTwo.Id, new GroupRunRequest("Andere Lobby"));
        this.engine.ChangeGroupRun(groupTwo.Id, open.Id, "load");
        Assert.Contains(this.engine.GroupRuns(groupTwo.Id), run => run.Id == open.Id && run.Invitation == open.Invitation && run.Selected);

        Registration JoinGroup(HostRun run, string name)
        {
            var registered = this.engine.Join(new JoinRequest(run.Invitation, name, Guid.NewGuid(),
                Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), "AXVD"));
            var session = this.engine.Connect(registered, new Hello(registered.ClientId));
            Assert.True(this.engine.Observe(registered, new Observation(Guid.NewGuid(), session.SessionId, 1, true, Snapshot(1, Mon(1)))).Accepted);
            return registered;
        }
    }

    [Fact]
    public void VersionOneBackupMigrationsPreserveRegistrationsAndRunData()
    {
        var initial = this.engine.Initialize("legacy", 100, "Existing run");
        var registration = this.Join(initial, "Registered").Registration;
        this.engine.AddRule(registration, "Vor dem Update gespeichert");
        string database = Path.Combine(this.directory, "runs.sqlite");
        using (var connection = new SqliteConnection($"Data Source={database};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE run_groups; PRAGMA user_version=1;";
            command.ExecuteNonQuery();
        }
        string backup = Path.Combine(this.directory, "backup.sqlite");
        AutomationStore.Backup(database, backup);
        Assert.Throws<IOException>(() => AutomationStore.Backup(database, backup));

        foreach (string path in new[] { database, backup })
        {
            var upgraded = new AutomationEngine(new AutomationStore(path));
            Assert.Equal("Vor dem Update gespeichert", Assert.Single(upgraded.View(registration).AdditionalRules));
            Assert.Empty(upgraded.Groups());
            Assert.Equal("Neue Gruppe", upgraded.CreateGroup("Neue Gruppe").Name);
        }
    }

    [Fact]
    public void AdditionalRulesBelongToTheRegisteredRunAndSurviveAReopen()
    {
        var run = this.engine.Initialize("guild", 0, "Test");
        var first = this.Join(run, "Player one").Registration;
        var partner = this.Join(run, "Player two").Registration;
        var otherRun = this.engine.Initialize("other guild", 0, "Other run");
        var outsider = this.Join(otherRun, "Player three").Registration;

        this.engine.AddRule(first, "  Kein Meisterball im Kampf.  ");
        this.engine.AddRule(partner, "KEIN MEISTERBALL IM KAMPF.");
        Assert.Throws<UnauthorizedAccessException>(() => this.engine.AddRule(outsider with { RunId = run.Id }, "Andere Regel"));
        Assert.Throws<InvalidDataException>(() => this.engine.AddRule(first, "Zeile eins\nZeile zwei"));

        var reopened = new AutomationEngine(new AutomationStore(Path.Combine(this.directory, "runs.sqlite")));
        Assert.Equal("Kein Meisterball im Kampf.", Assert.Single(reopened.View(partner).AdditionalRules));
        Assert.Empty(reopened.View(outsider).AdditionalRules);
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

    [Fact]
    public void OnlyDisconnectingTheCurrentSessionMakesThePlayerOffline()
    {
        var (_, client) = this.Active();
        var current = this.engine.Connect(client.Registration, new Hello(client.Registration.ClientId));

        this.engine.Disconnect(client.Registration, client.Session.SessionId);
        Assert.Equal("Synchronizing", this.engine.Heartbeat(client.Registration, current.SessionId));

        this.engine.Disconnect(client.Registration, current.SessionId);
        Assert.Equal("Offline", this.engine.Connection(this.engine.View(client.Registration).Players.Single()));
        Assert.Throws<UnauthorizedAccessException>(() => this.engine.Heartbeat(client.Registration, current.SessionId));
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
    public void ConfirmationKeepsBattleContextAndMarksAllPlayersInTheLinkDeadOnce()
    {
        var run = this.engine.Initialize("guild", 0, "Test", DeathRule.ConfirmFaint);
        var first = this.Join(run, "Anne");
        var second = this.Join(run, "Max");
        foreach (var client in new[] { first, second }) this.Send(client, 1, true, Snapshot(1, Mon(1)));
        this.engine.ChangeRun("guild", run.Id, "start");
        foreach (var client in new[] { first, second }) this.Send(client, 2, false, Snapshot(2, Mon(1), Mon(2)));
        var trace = new BattleTrace(1, 25, 33, "Felizia", FinishingBlow: true);
        var knockedOut = Snapshot(3, Mon(1), Mon(2) with { Hp = 0 });
        this.Send(first, 3, false, knockedOut with { InBattle = true, Faints = [trace] });
        var before = this.engine.View(first.Registration);
        Assert.False(before.Encounters.Single().Dead);
        var incident = Assert.Single(before.Players.Single(p => p.Id == first.Registration.PlayerId).Pokemon,
            p => p.PendingDeath is not null).PendingDeath!;
        Assert.Equal(trace, incident.Battle);
        Assert.Throws<InvalidOperationException>(() => this.engine.ResolveFaint(second.Registration, incident.PokemonId, incident.IncidentId, true, ""));
        this.engine.ResolveFaint(first.Registration, incident.PokemonId, Guid.NewGuid(), true, "Unpassender Vorfall");
        Assert.False(this.engine.View(first.Registration).Encounters.Single().Dead);
        this.engine.ResolveFaint(first.Registration, incident.PokemonId, incident.IncidentId, true, "Volltreffer");
        this.engine.ResolveFaint(first.Registration, incident.PokemonId, incident.IncidentId, true, "Wiederholung");
        var final = this.engine.View(first.Registration);
        Assert.True(final.Encounters.Single().Dead);
        Assert.All(final.Players, p => Assert.Single(p.Pokemon, m => m.Dead));
        var death = Assert.Single(final.History, item => item.Kind == "death");
        var details = Assert.IsType<EventDetails>(death.Details);
        Assert.Equal("Anne", details.Pokemon!.Player);
        Assert.Equal("Max", Assert.Single(details.Members!).Player);
        Assert.Equal(16, death.Area);
        Assert.Equal(incident.At, death.At);
        Assert.Equal(trace, details.Battle);
        Assert.Equal("Volltreffer", details.Reason);
        var reason = death.Message;
        Assert.Contains("Volltreffer", reason);
        Assert.Contains("Pikachu", reason);
        Assert.Contains("Tackle", reason);
    }

    [Fact]
    public void FeedKeepsEachCatchVerdictAndCompleteLinkMembers()
    {
        var run = this.engine.Initialize("guild", 0, "Test");
        var first = this.Join(run, "Anne");
        var second = this.Join(run, "Max");
        foreach (var client in new[] { first, second }) this.Send(client, 1, true, Snapshot(1, Mon(1) with { Species = 1 }));
        this.engine.ChangeRun("guild", run.Id, "start");
        this.Send(first, 2, false, Snapshot(2, Mon(1) with { Species = 1 }, Mon(2) with { Species = 25 }));
        this.Send(second, 2, false, Snapshot(2, Mon(1) with { Species = 1 }, Mon(3, slot: 1) with { Species = 26 }));
        var history = this.engine.View(first.Registration).History;
        var catches = history.Where(e => e.Kind == "caught").Select(e => e.Details!).ToArray();
        Assert.Equal(2, catches.Length);
        Assert.Null(catches[0].CatchCheck!.PreviouslyCaught);
        Assert.Equal("Pikachu", catches[1].CatchCheck!.PreviouslyCaught);
        Assert.All(catches, c => Assert.True(c.AreaAvailable == true));
        var linked = Assert.Single(history, e => e.Kind == "linked");
        Assert.Equal(new[] { new EventPokemon("Anne", 25), new EventPokemon("Max", 26) }, linked.Details!.Members);
    }

    [Fact]
    public void ArenaProgressIsPlayerScopedAndCanOverrideTheCurrentBadgeReading()
    {
        var (_, player) = this.Active();
        this.Send(player, 2, false, Snapshot(2, Mon(1)) with { BadgeMask = 1 });
        this.engine.ChangeArena(player.Registration, 1, false);
        Assert.Equal(0, this.engine.View(player.Registration).Players.Single().ArenaProgress);
        this.engine.ChangeArena(player.Registration, 1, true);
        this.engine.ChangeArena(player.Registration, 2, true);
        this.engine.ChangeArena(player.Registration, 2, true);
        var state = this.engine.View(player.Registration);
        Assert.Equal(3, state.Players.Single().ArenaProgress);
        Assert.Equal(2, state.History.Count(item => item.Kind == "arena-completed"));
        this.engine.ChangeArena(player.Registration, 2, false);
        Assert.Equal(1, this.engine.View(player.Registration).Players.Single().ArenaProgress);
    }

    [Fact]
    public void CatchCheckAcceptsGermanAndEnglishAndIncludesDefeatedEvolutionFamilies()
    {
        var (_, player) = this.Active(DeathRule.ZeroHpOutsideBattle);
        this.Send(player, 2, false, Snapshot(2, Mon(1), Mon(2) with { Species = 25, MetArea = 7 }));
        this.engine.SetDeath("guild", player.Registration.RunId, 7, true);
        var english = this.engine.CheckCatch(player.Registration, "Raichu");
        Assert.Equal("Pikachu", english.PreviouslyCaught);
        Assert.Equal("Blütenburg City", english.Area);
        Assert.Equal("Trasla", this.engine.CheckCatch(player.Registration, "Ralts").Pokemon);
        Assert.Equal(this.engine.CheckCatch(player.Registration, "Ralts"), this.engine.CheckCatch(player.Registration, "Trasla"));
        Assert.Null(this.engine.CheckCatch(player.Registration, "Glumanda").PreviouslyCaught);
    }

    [Fact]
    public void CatchReplaySeparatesItsMetAreaFromTheLaterPlayerPosition()
    {
        var (_, player) = this.Active();
        var point = new MapPosition(5, 7, 30, 20, true);
        this.Send(player, 2, false, Snapshot(2, Mon(1), Mon(2) with { MetArea = 17 }) with { Position = point });
        this.Send(player, 3, false, Snapshot(3, Mon(1), Mon(2) with { MetArea = 17 }, Mon(3, slot: 2)) with { Position = point with { X = 6 } });
        using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={Path.Combine(this.directory, "runs.sqlite")};Pooling=False");
        db.Open();
        using var query = db.CreateCommand();
        query.CommandText = "SELECT count(*) FROM map_history";
        Assert.Equal(2L, query.ExecuteScalar());
        query.CommandText = "SELECT document FROM replay_events ORDER BY rowid";
        using var rows = query.ExecuteReader();
        Assert.True(rows.Read());
        Assert.Null(Protocol.Deserialize<AutomationEvent>(rows.GetString(0)).Position);
        Assert.True(rows.Read());
        Assert.Equal(6, Protocol.Deserialize<AutomationEvent>(rows.GetString(0)).Position!.X);
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
        LayoutId = "fixture",
        Epoch = "fixture",
        Frame = frame,
        PlaySeconds = frame,
        Area = 16,
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
