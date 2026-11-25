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
    public void RunEditionUsesTheRegisteredGameProfile()
    {
        var run = this.engine.Initialize("editions", 0, "Rubin", edition: "rubin");
        Assert.Equal("ruby", run.Edition);
        Assert.Throws<InvalidDataException>(() => this.engine.Initialize("editions", 0, "Saphir", edition: "sapphire"));
        Assert.Throws<InvalidDataException>(() => this.engine.Join(new JoinRequest(run.Invitation, "Foreign", Guid.NewGuid(), new string('A', 64), "AXPD")));

        var player = this.Join(run, "Local");
        var observation = Snapshot(1);
        Assert.Throws<InvalidDataException>(() => this.Send(player, 1, true, observation with { GameCode = "AXPD" }));
        Assert.True(this.Send(player, 1, true, observation).Accepted);
    }

    [Fact]
    public void AppearanceBelongsToTheRegisteredPlayerAndSupportsExistingProfiles()
    {
        var run = this.engine.Initialize("appearance", 0, "Rubin");
        var existing = this.Join(run, "First");
        var newcomer = this.engine.Join(new JoinRequest(run.Invitation, "Second", Guid.NewGuid(),
            Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), "AXVD", Marker: PlayerMarker.Heart, Color: 0x49DDE4));

        Assert.Null(this.engine.View(existing.Registration).Players.Single(p => p.Id == existing.Registration.PlayerId).Color);
        Assert.Throws<InvalidDataException>(() => this.engine.UpdateAppearance(existing.Registration, PlayerMarker.Circle, 0x1000000));
        this.engine.UpdateAppearance(existing.Registration, PlayerMarker.Star, 0xFFD372);
        var peers = this.engine.View(newcomer).Players;
        Assert.Contains(peers, player => player.Id == existing.Registration.PlayerId && player is { Marker: PlayerMarker.Star, Color: 0xFFD372 });
        Assert.Contains(peers, player => player.Id == newcomer.PlayerId && player is { Marker: PlayerMarker.Heart, Color: 0x49DDE4 });
        this.engine.UpdateAppearance(existing.Registration, PlayerMarker.Star, null);
        Assert.Null(this.engine.View(newcomer).Players.Single(p => p.Id == existing.Registration.PlayerId).Color);
    }

    [Fact]
    public void ProfileEditsDuringAnActiveRunKeepTheRegistrationAndRejectDuplicateNames()
    {
        var run = this.engine.Initialize("profile", 0, "Rubin");
        var player = this.Join(run, "First");
        var partner = this.Join(run, "Second");
        Assert.True(this.Send(player, 1, true, Snapshot(1)).Accepted);
        Assert.True(this.Send(partner, 1, true, Snapshot(1)).Accepted);
        this.engine.ChangeRun("profile", run.Id, "start");

        Assert.Throws<InvalidDataException>(() =>
            this.engine.UpdateAppearance(player.Registration, PlayerMarker.Star, 0xFFD372, "  "));
        Assert.Throws<InvalidDataException>(() =>
            this.engine.UpdateAppearance(player.Registration, PlayerMarker.Star, 0xFFD372, "@First"));
        Assert.Throws<InvalidOperationException>(() =>
            this.engine.UpdateAppearance(player.Registration, PlayerMarker.Star, 0xFFD372, " second "));
        Assert.Contains(this.engine.View(partner.Registration).Players, entry =>
            entry.Id == player.Registration.PlayerId && entry is { Name: "First", Marker: PlayerMarker.Circle, Color: null });

        this.engine.UpdateAppearance(player.Registration, PlayerMarker.Star, 0xFFD372, "  First Trainer  ");
        var reloaded = new AutomationEngine(this.store).View(partner.Registration);
        Assert.Equal(RunPhase.Active, reloaded.Phase);
        Assert.Contains(reloaded.Players, entry => entry.Id == player.Registration.PlayerId && entry.ClientId == player.Registration.ClientId &&
            entry is { Name: "First Trainer", Marker: PlayerMarker.Star, Color: 0xFFD372 });
        Assert.Contains(reloaded.Players, entry => entry.Id == partner.Registration.PlayerId && entry.Name == "Second");
    }

    [Fact]
    public void OverworldPosesOnlyReachOtherCurrentSessionsOnTheSameRunAndMap()
    {
        var run = this.engine.Initialize("avatar", 0, "Together");
        var first = this.Join(run, "First");
        var second = this.Join(run, "Second");
        var outsider = this.Join(this.engine.Initialize("other", 0, "Separate"), "Outsider");
        var pose = new OverworldPose(0, 16, 100, 200, 0, -3, 89, 2, 1, 3, 2, 1);

        Assert.Empty(this.engine.ExchangeOverworld(first.Registration, first.Session.SessionId, pose));
        Assert.Empty(this.engine.ExchangeOverworld(outsider.Registration, outsider.Session.SessionId, pose));
        Assert.Throws<UnauthorizedAccessException>(() => this.engine.ExchangeOverworld(
            outsider.Registration with { RunId = run.Id }, outsider.Session.SessionId, pose));
        Assert.Empty(this.engine.ExchangeOverworld(second.Registration, second.Session.SessionId, pose with { MapGroup = 1 }));
        Assert.Equal(new OverworldPlayer(first.Registration.PlayerId, pose),
            Assert.Single(this.engine.ExchangeOverworld(second.Registration, second.Session.SessionId, pose)));

        var next = this.engine.Connect(first.Registration, new Hello(first.Registration.ClientId));
        Assert.Throws<UnauthorizedAccessException>(() => this.engine.ExchangeOverworld(first.Registration, first.Session.SessionId, pose));
        Assert.Empty(this.engine.ExchangeOverworld(second.Registration, second.Session.SessionId, pose));
        this.engine.ExchangeOverworld(first.Registration, next.SessionId, pose);
        this.engine.Disconnect(first.Registration, first.Session.SessionId);
        Assert.Single(this.engine.ExchangeOverworld(second.Registration, second.Session.SessionId, pose));
        this.engine.Disconnect(first.Registration, next.SessionId);
        Assert.Empty(this.engine.ExchangeOverworld(second.Registration, second.Session.SessionId, pose));
    }

    [Fact]
    public void TradeIsScopedToOneLivingLinkAndCompletesOnlyWithBothDurableReceipts()
    {
        var run = this.engine.Initialize("trades", 0, "Rubin");
        var first = this.Join(run, "First");
        var second = this.Join(run, "Second");
        var firstId = first.Registration.PlayerId;
        var secondId = second.Registration.PlayerId;
        var machoke = TradeRecord(8, 67);
        var pikachu = TradeRecord(9, 25, box: true);
        Assert.True(this.Send(first, 1, true, Snapshot(1, Mon(1)) with { TrainerId = 101 }).Accepted);
        Assert.True(this.Send(second, 1, true, Snapshot(1, Mon(2)) with { TrainerId = 102 }).Accepted);
        this.engine.ChangeRun("trades", run.Id, "start");
        Assert.True(this.Send(first, 2, false, Snapshot(2, Mon(1), Parse(machoke)) with { TrainerId = 101 }).Accepted);
        Assert.True(this.Send(second, 2, false, Snapshot(2, Mon(2), Parse(pikachu)) with { TrainerId = 102 }).Accepted);
        var original = this.engine.View(first.Registration);
        var link = Assert.Single(original.Encounters);
        var offer = new TradeOffer(link.Id, link.Members[firstId], secondId, link.Members[secondId]);
        Assert.Throws<InvalidOperationException>(() => this.engine.OfferTrade(first.Registration, offer with { EncounterId = Guid.NewGuid() }));

        this.engine.OfferTrade(first.Registration, offer);
        var pending = this.engine.View(second.Registration).Trade!;
        Assert.Throws<InvalidOperationException>(() => this.engine.RespondToTrade(first.Registration, pending.Id, true));
        Assert.Null(this.engine.WorkForTrade(first.Registration, pending.Id));
        this.engine.RespondToTrade(second.Registration, pending.Id, false);
        Assert.Equal(PokemonTradePhase.Declined, this.engine.View(first.Registration).Trade!.Phase);

        this.engine.OfferTrade(first.Registration, offer);
        Guid id = this.engine.View(first.Registration).Trade!.Id;
        this.engine.RespondToTrade(second.Registration, id, true);
        this.engine.PrepareTrade(first.Registration, id, machoke);
        Assert.Null(this.engine.WorkForTrade(second.Registration, id)!.Incoming);
        this.engine.PrepareTrade(second.Registration, id, pikachu);
        Assert.Equal(pikachu, this.engine.WorkForTrade(first.Registration, id)!.Incoming);
        Assert.Equal("Trading", this.Send(first, 3, false, Snapshot(3, Mon(1))).State);
        Assert.Throws<InvalidOperationException>(() => this.engine.ChangeRun("trades", run.Id, "pause"));
        this.engine.ConfirmTrade(first.Registration, id, TradeRecord(9, 25));
        Assert.Equal(link.Members, Assert.Single(this.engine.View(first.Registration).Encounters).Members);

        var restarted = new AutomationEngine(this.store);
        Assert.Null(restarted.WorkForTrade(first.Registration, id));
        Assert.Equal(machoke, restarted.WorkForTrade(second.Registration, id)!.Incoming);
        var evolved = TradeRecord(8, 68, box: true);
        restarted.ConfirmTrade(second.Registration, id, evolved);
        restarted.ConfirmTrade(second.Registration, id, evolved);
        var completed = restarted.View(second.Registration);
        Assert.Equal(PokemonTradePhase.Completed, completed.Trade!.Phase);
        var swapped = Assert.Single(completed.Encounters);
        Assert.Equal(link.GroupId, swapped.GroupId);
        Assert.Equal(link.Members[firstId], swapped.Members[secondId]);
        Assert.Equal(link.Members[secondId], swapped.Members[firstId]);
        Assert.Equal(68, completed.Players.Single(p => p.Id == secondId).Pokemon.Single(p => p.Id == swapped.Members[secondId]).State.Species);
        Assert.Single(completed.History, entry => entry.Kind == "trade");
        var resumed = restarted.Connect(first.Registration, new Hello(first.Registration.ClientId));
        var oldSave = Snapshot(3, Mon(1), Parse(machoke)) with { TrainerId = 101 };
        Assert.Equal("OutOfSync", restarted.Observe(first.Registration, new Observation(Guid.NewGuid(), resumed.SessionId, 3, true, oldSave)).State);
        var newSave = Snapshot(4, Mon(1), Parse(TradeRecord(9, 25))) with { TrainerId = 101 };
        Assert.True(restarted.Observe(first.Registration, new Observation(Guid.NewGuid(), resumed.SessionId, 3, true, newSave)).Accepted);
        Assert.Equal(original.History.Count(entry => entry.Kind == "caught"), restarted.View(first.Registration).History.Count(entry => entry.Kind == "caught"));

        static RawPokemon TradeRecord(uint pid, int species, bool box = false)
        {
            byte[] record = SyntheticPokemon.Bytes(pid, species, box: box);
            if (!box) record[85] = 0xFF;
            return new RawPokemon(Convert.ToHexString(record), box ? Placement.Box : Placement.Party, 1, box ? 0 : null);
        }

        static PokemonState Parse(RawPokemon record) =>
            Gen3Parser.Parse(Convert.FromHexString(record.Hex), record.Container, record.Slot, record.Box)!;
    }

    [Fact]
    public void OverworldPosesExpireWithoutInventoriesAndCanBeClearedImmediately()
    {
        var clock = new OverworldClock();
        var engineWithClock = new AutomationEngine(this.store, clock);
        var run = engineWithClock.Initialize("overworld", 0, "Rubin");
        var first = Register("First");
        var second = Register("Second");
        var pose = new OverworldPose(0, 16, 100, 200, 0, 0, 0, 2, 1, 4, 1, 2);
        Assert.Throws<InvalidDataException>(() => engineWithClock.ExchangeOverworld(first.Registration, first.Session.SessionId, pose with { Graphics = 255 }));
        engineWithClock.ExchangeOverworld(first.Registration, first.Session.SessionId, pose);
        Assert.Single(engineWithClock.ExchangeOverworld(second.Registration, second.Session.SessionId, pose));
        clock.Elapsed += TimeSpan.FromSeconds(2);
        Assert.Empty(engineWithClock.ExchangeOverworld(second.Registration, second.Session.SessionId, pose));
        engineWithClock.ExchangeOverworld(first.Registration, first.Session.SessionId, pose);
        Assert.Empty(engineWithClock.ExchangeOverworld(first.Registration, first.Session.SessionId, null));
        Assert.Empty(engineWithClock.ExchangeOverworld(second.Registration, second.Session.SessionId, pose));
        Assert.All(engineWithClock.View(second.Registration).Players, player => Assert.Null(player.LastSnapshot));

        TestClient Register(string name)
        {
            var id = Guid.NewGuid();
            var token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var registration = engineWithClock.Join(new JoinRequest(run.Invitation, name, id, token, "AXVD"));
            return new TestClient(registration, engineWithClock.Connect(registration, new Hello(id)));
        }
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
    public void RubyStartersObservedInLobbyAndActiveRunCompleteOneSeparateLink()
    {
        var run = this.engine.Initialize("fresh", 0, "Rubin");
        var first = this.Join(run, "First");
        Assert.True(this.Send(first, 1, true, Snapshot(1)).Accepted);
        Assert.True(this.Send(first, 2, false, Snapshot(2, Mon(1)) with { RubyStarterWindow = true }).Accepted);
        var lobby = this.engine.View(first.Registration);
        Assert.Null(Assert.Single(lobby.Encounters).GroupId);
        Assert.Empty(lobby.History);

        var second = this.Join(run, "Second");
        Assert.True(this.Send(second, 1, true, Snapshot(1)).Accepted);
        this.engine.ChangeRun("fresh", run.Id, "start");
        Assert.Null(Assert.Single(this.engine.View(first.Registration).Encounters).GroupId);
        Assert.True(this.Send(second, 2, false, Snapshot(2, Mon(1)) with { RubyStarterWindow = true }).Accepted);

        var active = this.engine.View(second.Registration);
        var starter = Assert.Single(active.Encounters);
        Assert.Equal(Encounter.StarterArea, starter.Area);
        Assert.NotNull(starter.GroupId);
        Assert.Equal(2, starter.Members.Count);
        Assert.Equal("linked", Assert.Single(active.History).Kind);
        Assert.All(active.Players, p => Assert.True(Assert.Single(p.Pokemon).Imported));
    }

    [Fact]
    public void HostCanMoveAndRemoveLinkMembersAtomicallyWithoutChangingTheirInventories()
    {
        var group = this.engine.CreateGroup("Spielrunde");
        var other = this.engine.CreateGroup("Andere Runde");
        var run = this.engine.CreateGroupRun(group.Id, new GroupRunRequest("Rubin"));
        var invitation = new AutomationRun { Invitation = run.Invitation };
        var first = this.Join(invitation, "Erster");
        var second = this.Join(invitation, "Zweiter");
        foreach (var client in new[] { first, second })
            this.Send(client, 1, true, Snapshot(1, Mon(1), Mon(2) with { MetArea = 17 }));
        this.engine.ChangeGroupRun(group.Id, run.Id, "start");

        var initial = this.engine.GroupLinkEditor(group.Id, run.Id);
        var starters = initial.Players.ToDictionary(player => player.Id, player => player.Pokemon.Single(mon => mon.MetArea == 16).Id);
        var route = initial.Players.ToDictionary(player => player.Id, player => player.Pokemon.Single(mon => mon.MetArea == 17).Id);
        var changes = new EditLinksRequest(initial.Revision, [new(Encounter.StarterArea, starters), new(17, route)]);
        Assert.Throws<UnauthorizedAccessException>(() => this.engine.EditGroupLinks(other.Id, run.Id, changes));
        Assert.Throws<InvalidDataException>(() => this.engine.EditGroupLinks(group.Id, run.Id,
            changes with { Links = [new(Encounter.StarterArea, starters), new(17, starters)] }));
        Assert.Empty(this.engine.GroupLinkEditor(group.Id, run.Id).Links);

        this.engine.EditGroupLinks(group.Id, run.Id, changes);
        var saved = this.engine.View(second.Registration);
        Assert.All(saved.Encounters, link => Assert.NotNull(link.GroupId));
        Assert.All(saved.Players, player => Assert.Equal(2, player.Pokemon.Count));
        Assert.Throws<InvalidOperationException>(() => this.engine.EditGroupLinks(group.Id, run.Id, changes));

        this.engine.SetDeath($"group:{group.Id:N}", run.Id, 17, dead: true);
        var locked = this.engine.GroupLinkEditor(group.Id, run.Id);
        Assert.True(locked.Links.Single(link => link.Area == 17).Locked);
        Assert.Throws<InvalidDataException>(() => this.engine.EditGroupLinks(group.Id, run.Id,
            new EditLinksRequest(locked.Revision, [new(Encounter.StarterArea, route)])));
        this.engine.EditGroupLinks(group.Id, run.Id, new EditLinksRequest(locked.Revision,
            [new(Encounter.StarterArea, new Dictionary<Guid, Guid> { [first.Registration.PlayerId] = starters[first.Registration.PlayerId] })]));
        var reopened = new AutomationEngine(new AutomationStore(Path.Combine(this.directory, "runs.sqlite")));
        var restored = reopened.View(second.Registration);
        Assert.Null(restored.Encounters.Single(link => link.Area == Encounter.StarterArea).GroupId);
        Assert.True(restored.Encounters.Single(link => link.Area == 17).Dead);
        Assert.DoesNotContain(restored.History, item => item.Kind == "caught");
    }

    [Fact]
    public void ExplicitStarterLinkPreservesRoute101AndSharesConfirmedDeaths()
    {
        var group = this.engine.CreateGroup("Three players");
        var other = this.engine.CreateGroup("Different players");
        var run = this.engine.CreateGroupRun(group.Id, new GroupRunRequest("Rubin", DeathRule.ZeroHpOutsideBattle));
        var invitation = new AutomationRun { Invitation = run.Invitation };
        var clients = Enumerable.Range(0, 3).Select(i => this.Join(invitation, "Player " + i)).ToArray();
        foreach (var client in clients) this.Send(client, 1, true, Snapshot(1, Mon(1)));
        this.engine.ChangeGroupRun(group.Id, run.Id, "start");

        var candidates = this.engine.GroupStarterCandidates(group.Id, run.Id);
        var picks = candidates.ToDictionary(p => p.Id, p => Assert.Single(p.Pokemon).Id);
        Assert.Throws<UnauthorizedAccessException>(() => this.engine.LinkGroupStarters(other.Id, run.Id, new StarterLinkRequest(picks)));
        Assert.Throws<InvalidDataException>(() => this.engine.LinkGroupStarters(group.Id, run.Id, new StarterLinkRequest([])));
        Assert.Empty(this.engine.View(clients[0].Registration).Encounters);
        this.engine.LinkGroupStarters(group.Id, run.Id, new StarterLinkRequest(picks));
        this.engine.LinkGroupStarters(group.Id, run.Id, new StarterLinkRequest(picks));

        foreach (var client in clients) this.Send(client, 2, false, Snapshot(2, Mon(1), Mon(2)));
        var afterCatch = this.engine.View(clients[0].Registration);
        Assert.Equal(2, afterCatch.Encounters.Count);
        var starter = Assert.Single(afterCatch.Encounters, e => e.Area == Encounter.StarterArea);
        var route = Assert.Single(afterCatch.Encounters, e => e.Area == 16);
        Assert.NotNull(starter.GroupId);
        Assert.NotNull(route.GroupId);
        Assert.Equal(picks, starter.Members);
        Assert.Single(afterCatch.History, e => e.Kind == "linked" && e.Area == Encounter.StarterArea);
        Assert.Equal(3, afterCatch.History.Count(e => e.Kind == "caught"));

        this.Send(clients[0], 3, false, Snapshot(3, Mon(1) with { Hp = 0 }, Mon(2)));
        this.Send(clients[0], 4, false, Snapshot(4, Mon(1) with { Hp = 0 }, Mon(2)));
        var reopened = new AutomationEngine(new AutomationStore(Path.Combine(this.directory, "runs.sqlite")));
        var persisted = reopened.View(clients[1].Registration);
        Assert.True(persisted.Encounters.Single(e => e.Area == Encounter.StarterArea).Dead);
        Assert.False(persisted.Encounters.Single(e => e.Area == 16).Dead);
        Assert.All(persisted.Players, p => Assert.True(p.Pokemon.Single(m => m.Id == picks[p.Id]).Dead));
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
            command.CommandText = "DROP TABLE timeline_events; DROP TABLE discord_run_archive; DROP TABLE run_groups; PRAGMA user_version=1;";
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
    public void ParticipantsCanEditAndRemoveRulesWithoutOverwritingAConcurrentChange()
    {
        var run = this.engine.Initialize("guild", 0, "Test");
        var first = this.Join(run, "Player one").Registration;
        var partner = this.Join(run, "Player two").Registration;
        var outsider = this.Join(this.engine.Initialize("other", 0, "Other"), "Outsider").Registration;
        this.engine.AddRule(first, "Alte Regel");
        this.engine.AddRule(first, "Unveränderte Regel");

        Assert.Throws<UnauthorizedAccessException>(() => this.engine.ChangeRule(outsider with { RunId = run.Id }, "Alte Regel", null));
        Assert.Throws<InvalidOperationException>(() => this.engine.ChangeRule(partner, "Alte Regel", "UNVERÄNDERTE REGEL"));
        Assert.Throws<InvalidDataException>(() => this.engine.ChangeRule(partner, "Alte Regel", "Zwei\nZeilen"));
        this.engine.ChangeRule(partner, "Alte Regel", "Neue Regel");
        Assert.Throws<InvalidOperationException>(() => this.engine.ChangeRule(first, "Alte Regel", null));
        Assert.Equal(["Neue Regel", "Unveränderte Regel"], this.engine.View(first).AdditionalRules);
        this.engine.ChangeRule(first, "Neue Regel", null);

        var reopened = new AutomationEngine(new AutomationStore(Path.Combine(this.directory, "runs.sqlite")));
        Assert.Equal("Unveränderte Regel", Assert.Single(reopened.View(partner).AdditionalRules));
        this.engine.ChangeRun("guild", run.Id, "end");
        Assert.Throws<InvalidOperationException>(() => this.engine.ChangeRule(partner, "Unveränderte Regel", null));
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
    public void LiveDamageOnlyComparesTheSameCombatantInTheSameBattle()
    {
        var initial = new BattleStatus("epoch:10", "Felizia",
            [new(0, 25, 22, 22, 101), new(1, 74, 28, 28, 201), new(3, 74, 21, 21, 203)]);
        var changed = initial with
        {
            Combatants = [new(0, 25, 12, 22, 101), new(1, 74, 11, 28, 202), new(3, 74, 0, 21, 203)],
        };
        Assert.True(changed.IsValid());
        Assert.Equal([(0, 10), (3, 21)], changed.DamageSince(initial));
        Assert.Empty((changed with { Key = "epoch-next:10" }).DamageSince(initial));
        Assert.Empty(initial.DamageSince(changed));
        Assert.False((changed with { Combatants = [changed.Combatants[0], changed.Combatants[0]] }).IsValid());
    }

    [Fact]
    public void BattleProjectionParsesValidBridgeDataAndIgnoresItOffTheFieldOfBattle()
    {
        var frame = SyntheticPokemon.Frame(24, SyntheticPokemon.Raw(101, 25)) with
        {
            InBattle = true,
            Battle = new RawBattleStatus(12, null, [new(1, 74, 28, 28, 9)]),
        };
        var battle = Assert.IsType<BattleStatus>(frame.Parse().Battle);
        Assert.Equal($"{frame.Epoch}:12", battle.Key);
        Assert.Equal(74, Assert.Single(battle.Combatants).Species);
        Assert.Null((frame with { InBattle = false }).Parse().Battle);
        Assert.Null((frame with { Battle = frame.Battle with { Started = 25 } }).Parse().Battle);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(0, 0)]
    [InlineData(1250, 1250)]
    [InlineData(1000000, null)]
    public void WalletIgnoresInvalidValuesAndKeepsZero(int? received, int? expected)
    {
        var frame = SyntheticPokemon.Frame(1, SyntheticPokemon.Raw(101, 25)) with { Money = received };
        Assert.Equal(expected, frame.Parse().Money);
    }

    [Fact]
    public void FirstEncounterSpectatorsGetHealthAndTheSpeciesIsRevealedByTheRecordedCatch()
    {
        var (_, client) = this.Active(DeathRule.ConfirmFaint);
        var battle = new BattleStatus("fixture:2", null,
            [new(0, 280, 1, 20, 1), new(1, 25, 7, 16, 123)], CatchEncounter: true);
        Assert.True(this.Send(client, 2, false, Snapshot(2, Mon(1)) with { InBattle = true, Battle = battle }).Accepted);
        var trace = new BattleTrace(0, 25, 33, null, FinishingBlow: true);
        Assert.True(this.Send(client, 3, false, Snapshot(3, Mon(1) with { Hp = 0 }) with
        {
            InBattle = true,
            Battle = battle with { CatchEncounter = false },
            Faints = [trace],
        }).Accepted);

        var player = Assert.Single(this.engine.View(client.Registration).Players);
        var ownBattle = Assert.IsType<BattleStatus>(player.LastSnapshot?.Battle);
        Assert.Equal(25, ownBattle.Combatants[1].Species);
        Assert.Equal(new BattleCombatant(1, 0, 7, 16, 0), ownBattle.ForSpectator().Combatants[1]);
        var incident = Assert.Single(player.Pokemon).PendingDeath!;
        Assert.Equal(25, incident.Battle?.OpponentSpecies);
        this.engine.ResolveFaint(client.Registration, incident.PokemonId, incident.IncidentId, true, null);
        var death = Assert.Single(this.engine.View(client.Registration).History, item => item.Kind == "death");
        Assert.Null(death.Details?.Battle?.OpponentSpecies);
        Assert.DoesNotContain("Pikachu", death.Message);

        Assert.True(this.Send(client, 4, false, Snapshot(4, Mon(1) with { Hp = 0 }, Mon(2) with { Species = 25 }) with
        {
            WildBattles = [new(16, 2, 4, 7)],
        }).Accepted);
        var view = this.engine.View(client.Registration);
        Assert.Null(Assert.Single(view.Players).LastSnapshot?.Battle);
        Assert.Equal(25, Assert.Single(view.History, item => item.Kind == "caught").Details?.Pokemon?.Species);
    }

    [Fact]
    public void AutomaticCatchCheckUsesTheWildOpponentFamilyAndTheEncounterSlot()
    {
        var (_, client) = this.Active();
        var battle = new BattleStatus("fixture:2", null,
            [new(0, 280, 20, 20, 1), new(1, 25, 20, 20, 2)], CatchEncounter: true);
        Assert.True(this.Send(client, 2, false, Snapshot(2, Mon(1)) with { InBattle = true, Battle = battle }).Accepted);

        var view = this.engine.View(client.Registration);
        var player = Assert.Single(view.Players);
        Assert.True(Assert.Single(EncounterCatchChecks.ForPlayer(view, player)).Allowed);

        player.LastSnapshot = player.LastSnapshot! with
        {
            Battle = battle with { Combatants = [battle.Combatants[0], battle.Combatants[1] with { Species = 281 }] },
        };
        Assert.Contains("Evolutionsfamilie", Assert.Single(EncounterCatchChecks.ForPlayer(view, player)).Reason);

        view.Encounters.Add(new Encounter { Area = 16, Failed = true, Dead = true });
        Assert.Contains("Fangort gesperrt", Assert.Single(EncounterCatchChecks.ForPlayer(view, player)).Reason);

        player.LastSnapshot = player.LastSnapshot with { Battle = battle with { Trainer = "Felizia", CatchEncounter = false } };
        Assert.Empty(EncounterCatchChecks.ForPlayer(view, player));
    }

    [Fact]
    public void RejectedEvolutionFamilyDoesNotConsumeTheFirstEncounter()
    {
        var (_, client) = this.Active();
        var duplicate = new BattleStatus("fixture:2", null,
            [new(0, 280, 20, 20, 1), new(1, 281, 15, 15, 2)], CatchEncounter: true);
        Assert.True(this.Send(client, 2, false, Snapshot(2, Mon(1)) with { InBattle = true, Battle = duplicate }).Accepted);
        Assert.True(this.Send(client, 3, false, Snapshot(3, Mon(1)) with { WildBattles = [new(16, 2, 3, 4)] }).Accepted);
        var eligible = duplicate with { Key = "fixture:4", Combatants = [duplicate.Combatants[0], new(1, 25, 8, 16, 3)] };
        Assert.True(this.Send(client, 4, false, Snapshot(4, Mon(1)) with { InBattle = true, Battle = eligible }).Accepted);

        var view = this.engine.View(client.Registration);
        var player = Assert.Single(view.Players);
        Assert.Empty(view.Encounters);
        Assert.True(Assert.Single(EncounterCatchChecks.ForPlayer(view, player)).Allowed);
        Assert.Equal(new BattleCombatant(1, 0, 8, 16, 0), player.LastSnapshot!.Battle!.ForSpectator().Combatants[1]);
    }

    [Fact]
    public void CaughtDuplicateWildPokemonStaysOutsideTheRouteLink()
    {
        var (_, client) = this.Active();
        var caught = new WildBattleResult(16, 2, 3, 7, Species: 281);
        Assert.True(this.Send(client, 2, false, Snapshot(3, Mon(1), Mon(2) with { Species = 281 }) with
        {
            WildBattles = [caught],
        }).Accepted);
        var view = this.engine.View(client.Registration);
        Assert.Empty(view.Encounters);
        Assert.Equal("extra-catch", Assert.Single(view.History).Kind);
        Assert.Empty(Assert.Single(view.Players).FirstWildAreas);

        Assert.True(this.Send(client, 3, false, Snapshot(5, Mon(1), Mon(2) with { Species = 281 },
            Mon(3, slot: 2) with { Species = 25 }) with { WildBattles = [caught, new(16, 4, 5, 7, Species: 25)] }).Accepted);
        Assert.NotNull(Assert.Single(this.engine.View(client.Registration).Encounters).GroupId);
    }

    [Fact]
    public void OnlyThePlayerWhoLostTheLastCompleteLinkCanFinishTheRun()
    {
        var run = this.engine.Initialize("guild", 0, "Test", DeathRule.ConfirmFaint);
        var first = this.Join(run, "First");
        var second = this.Join(run, "Second");
        foreach (var client in new[] { first, second })
            this.Send(client, 1, true, Snapshot(1, Mon(1)));
        this.engine.ChangeRun("guild", run.Id, "start");
        var caught = Mon(2);
        var reserve = Mon(3, box: true) with { MetArea = 17 };
        foreach (var client in new[] { first, second })
            this.Send(client, 2, false, Snapshot(2, Mon(1), caught, reserve));
        var trace = new BattleTrace(1, 25, 33, null);
        this.Send(first, 3, false, Snapshot(3, Mon(1), caught with { Hp = 0 }, reserve) with { InBattle = true, Faints = [trace] });
        var pending = this.engine.View(first.Registration).Players.Single(player => player.Id == first.Registration.PlayerId)
            .Pokemon.Select(pokemon => pokemon.PendingDeath).OfType<PendingDeath>().Single();
        this.engine.ResolveFaint(first.Registration, pending.PokemonId, pending.IncidentId, true, null);
        Assert.Null(this.engine.View(first.Registration).PendingEnd);

        // A living boxed link still counts. Bring it out and lose it on the other player's turn.
        var fighting = reserve with { Container = Placement.Party, Box = null, Slot = 2, Level = 5, Hp = 15, MaxHp = 20 };
        this.Send(second, 3, false, Snapshot(3, Mon(1), caught, fighting));
        this.Send(second, 4, false, Snapshot(4, Mon(1), caught, fighting with { Hp = 0 }) with
        {
            InBattle = true,
            Faints = [trace with { PartySlot = 2 }],
        });
        pending = this.engine.View(second.Registration).Players.Single(player => player.Id == second.Registration.PlayerId)
            .Pokemon.Select(pokemon => pokemon.PendingDeath).OfType<PendingDeath>().Single();
        this.engine.ResolveFaint(second.Registration, pending.PokemonId, pending.IncidentId, true, null);

        var view = this.engine.View(second.Registration);
        var end = Assert.IsType<PendingRunEnd>(view.PendingEnd);
        Assert.Equal(second.Registration.PlayerId, end.PlayerId);
        Assert.Equal(RunPhase.Active, view.Phase);
        Assert.Throws<InvalidOperationException>(() => this.engine.CompleteWipedRun(first.Registration, end.Id, "Wrong player"));
        var restarted = new AutomationEngine(this.store);
        restarted.CompleteWipedRun(second.Registration, end.Id, "Team besiegt");
        view = restarted.View(first.Registration);
        Assert.Equal(RunPhase.Finished, view.Phase);
        Assert.Equal("Team besiegt", view.EndReason);
        Assert.False(view.Selected);
        Assert.Empty(view.Invitation);
        Assert.Null(view.PendingEnd);
        Assert.Single(view.History, item => item.Kind == "run-ended");
    }

    [Fact]
    public void RunEndRecoversForThePlayerWhoLostTheLastLinkEvenWhenTheirPartnerHasNoReserve()
    {
        var run = this.engine.Initialize("guild", 0, "Test", DeathRule.ConfirmFaint);
        var first = this.Join(run, "Testspieler");
        var second = this.Join(run, "Marco");
        foreach (var client in new[] { first, second })
            this.Send(client, 1, true, Snapshot(1));
        this.engine.ChangeRun("guild", run.Id, "start");
        var onRoute = Mon(1);
        var spare = Mon(2) with { MetArea = 17 };
        this.Send(first, 2, false, Snapshot(2, onRoute, spare));
        this.Send(second, 2, false, Snapshot(2, onRoute));
        this.Send(first, 3, false, Snapshot(3, onRoute with { Hp = 0 }, spare) with { InBattle = true });
        var pending = this.engine.View(first.Registration).Players[0].Pokemon.Select(p => p.PendingDeath).OfType<PendingDeath>().Single();
        this.engine.ResolveFaint(first.Registration, pending.PokemonId, pending.IncidentId, true, "Volltreffer");

        // Simulate persisted deaths from before the server recorded run-end prompts.
        this.store.Execute(tx =>
        {
            var legacy = tx.Run(run.Id);
            legacy.PendingEnd = null;
            legacy.History.RemoveAll(item => item.Kind == "run-end-pending");
            tx.Save(legacy);
            return true;
        });

        var restarted = new AutomationEngine(this.store);
        var restored = restarted.View(second.Registration);
        var end = Assert.IsType<PendingRunEnd>(restored.PendingEnd);
        Assert.Equal(first.Registration.PlayerId, end.PlayerId);
        Assert.Single(restored.History, item => item.Kind == "run-end-pending");
        Assert.Equal(end, restarted.View(first.Registration).PendingEnd);
        restarted.CompleteWipedRun(first.Registration, end.Id, "Keine Links übrig");
        Assert.Equal(RunPhase.Finished, restarted.View(second.Registration).Phase);
    }

    [Fact]
    public void APlayerLosingTheirLastPokemonBeforeCompletingALinkCanFinishTheRun()
    {
        var (_, client) = this.Active(DeathRule.ConfirmFaint);
        this.Send(client, 2, false, Snapshot(2, Mon(1) with { Hp = 0 }) with { InBattle = true });
        var pending = this.engine.View(client.Registration).Players[0].Pokemon.Single().PendingDeath!;
        this.engine.ResolveFaint(client.Registration, pending.PokemonId, pending.IncidentId, true, null);
        Assert.Equal(client.Registration.PlayerId, this.engine.View(client.Registration).PendingEnd?.PlayerId);
    }

    [Fact]
    public void SequenceGapResynchronizesFromTheServerOnReconnect()
    {
        var (_, client) = this.Active();
        var result = this.Send(client, 4, false, Snapshot(2, Mon(1), Mon(2)));
        Assert.Equal("Reconnecting", result.State);
        var session = this.engine.Connect(client.Registration, new Hello(client.Registration.ClientId));
        var retry = this.engine.Observe(client.Registration, new Observation(Guid.NewGuid(), session.SessionId, 2, true, Snapshot(2, Mon(1))));
        Assert.True(retry.Accepted);
        Assert.Equal("Ready", retry.State);
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
    public void RollbacksAndMissingPokemonSynchronizeWithoutFalseEvents(string kind)
    {
        var (_, client) = this.Active();
        var snapshot = kind switch
        {
            "missing" => Snapshot(2),
            "clock" => Snapshot(2, Mon(1)) with { PlaySeconds = 0, Frame = 0 },
            "experience" => Snapshot(2, Mon(1) with { Experience = 1 }),
            _ => Snapshot(2, Mon(1)) with { Epoch = "different" },
        };
        Assert.Equal("Ready", this.Send(client, 2, false, snapshot).State);
        var view = this.engine.View(client.Registration);
        Assert.Empty(view.History);
        Assert.Equal(kind != "missing", Assert.Single(Assert.Single(view.Players).Pokemon).Present);
    }

    [Fact]
    public void ServerRestartReconcilesACatchFromAContinuousGameOnBaseline()
    {
        var (_, client) = this.Active();
        var restarted = new AutomationEngine(this.store);
        Assert.Throws<UnauthorizedAccessException>(() => restarted.Observe(client.Registration,
            new Observation(Guid.NewGuid(), client.Session.SessionId, 2, false, Snapshot(2, Mon(1)))));
        var session = restarted.Connect(client.Registration, new Hello(client.Registration.ClientId));
        Assert.False(restarted.Observe(client.Registration, new Observation(Guid.NewGuid(), session.SessionId, 2, false, Snapshot(2, Mon(1)))).Accepted);
        Assert.Equal("Ready", restarted.Observe(client.Registration,
            new Observation(Guid.NewGuid(), session.SessionId, 2, true, Snapshot(2, Mon(1), Mon(2)))).State);
        Assert.Single(restarted.View(client.Registration).History, e => e.Kind == "caught");
    }

    [Fact]
    public void LoadingTheCorrectTrainerAutomaticallyRecoversAndKeepsTheLink()
    {
        var (_, client) = this.Active();
        this.Send(client, 2, false, Snapshot(2, Mon(1), Mon(2)) with { TrainerId = 123 });
        var original = Assert.Single(this.engine.View(client.Registration).Encounters);
        Assert.Equal("OutOfSync", this.Send(client, 3, false, Snapshot(1, Mon(3)) with { TrainerId = 456, Epoch = "other" }).State);
        Assert.Equal("Ready", this.Send(client, 3, false, Snapshot(1, Mon(1)) with { TrainerId = 123, Epoch = "old-save" }).State);
        Assert.Equal("Ready", this.Send(client, 4, false, Snapshot(2, Mon(1), Mon(2)) with { TrainerId = 123, Epoch = "old-save" }).State);

        var view = this.engine.View(client.Registration);
        Assert.Equal(original.GroupId, Assert.Single(view.Encounters).GroupId);
        Assert.Single(view.History, e => e.Kind == "caught");
        Assert.All(Assert.Single(view.Players).Pokemon, pokemon => Assert.True(pokemon.Present));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(4)]
    public void FailedFirstWildEncounterKillsTheRouteAndAnAlreadyCaughtPartner(int outcome)
    {
        var run = this.engine.Initialize("guild", 0, "Test");
        var first = this.Join(run, "First");
        var second = this.Join(run, "Second");
        this.Send(first, 1, true, Snapshot(1, Mon(1)));
        this.Send(second, 1, true, Snapshot(1, Mon(1)));
        this.engine.ChangeRun("guild", run.Id, "start");
        this.Send(first, 2, false, Snapshot(2, Mon(1), Mon(2)));

        var battle = new WildBattleResult(16, 2, 3, outcome);
        Assert.True(this.Send(second, 2, false, Snapshot(3, Mon(1)) with { WildBattles = [battle] }).Accepted);
        Assert.True(this.Send(second, 3, false, Snapshot(4, Mon(1), Mon(2)) with { WildBattles = [battle] }).Accepted);

        var view = this.engine.View(second.Registration);
        var route = Assert.Single(view.Encounters);
        Assert.True(route.Failed && route.Dead);
        var member = Assert.Single(route.Members);
        Assert.Equal(first.Registration.PlayerId, member.Key);
        Assert.True(view.Players.Single(p => p.Id == member.Key).Pokemon.Single(p => p.Id == member.Value).Dead);
        Assert.Single(view.History, e => e.Kind == "encounter-failed");
        Assert.Single(view.History, e => e.Kind == "extra-catch");
    }

    [Fact]
    public void WildResultsIgnoreBattleAlreadyInProgressAtStartAndSubsequentFailuresAfterACatch()
    {
        var (_, client) = this.Active();
        var preRun = new WildBattleResult(17, 0, 2, 1);
        var caught = new WildBattleResult(16, 2, 3, 7);
        var laterFailure = new WildBattleResult(16, 4, 5, 1);
        Assert.True(this.Send(client, 2, false, Snapshot(5, Mon(1), Mon(2)) with { WildBattles = [preRun, caught, laterFailure] }).Accepted);

        var view = this.engine.View(client.Registration);
        Assert.DoesNotContain(view.History, e => e.Kind == "encounter-failed");
        var route = Assert.Single(view.Encounters);
        Assert.Equal(16, route.Area);
        Assert.False(route.Dead);
    }

    [Fact]
    public void ResultsAccumulatedWhilePausedAreSkippedAfterResuming()
    {
        var (run, client) = this.Active();
        this.engine.ChangeRun("guild", run.Id, "pause");
        this.engine.ChangeRun("guild", run.Id, "load");
        var session = this.engine.Connect(client.Registration, new Hello(client.Registration.ClientId));
        var duringPause = new WildBattleResult(16, 2, 3, 1);
        var afterResume = new WildBattleResult(17, 5, 6, 1);
        Assert.True(this.Send(client with { Session = session }, 2, true, Snapshot(4, Mon(1)) with { WildBattles = [duringPause] }).Accepted);
        Assert.True(this.Send(client with { Session = session }, 3, false, Snapshot(6, Mon(1)) with { WildBattles = [duringPause, afterResume] }).Accepted);
        Assert.Equal(17, Assert.Single(this.engine.View(client.Registration).Encounters).Area);
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
    public void ArenaProgressIsSharedAndPersistsAcrossDifferentBadgeReadings()
    {
        var run = this.engine.Initialize("guild", 0, "Gemeinsam");
        var first = this.Join(run, "First");
        var second = this.Join(run, "Second");
        this.Send(first, 1, true, Snapshot(1, Mon(1)) with { BadgeMask = 1 });
        this.Send(second, 1, true, Snapshot(1, Mon(1)) with { BadgeMask = 0 });
        this.engine.ChangeRun("guild", run.Id, "start");
        var isolated = this.engine.Initialize("other", 0, "Andere Gruppe");
        var outsider = this.Join(isolated, "Outsider");

        Assert.Equal(0, this.engine.View(first.Registration).ArenaProgress);
        this.engine.ChangeArena(first.Registration, 1, true);
        this.engine.ChangeArena(second.Registration, 2, true);
        this.engine.ChangeArena(first.Registration, 2, true);
        var reopened = new AutomationEngine(new AutomationStore(Path.Combine(this.directory, "runs.sqlite")));
        var state = reopened.View(second.Registration);
        Assert.Equal(3, state.ArenaProgress);
        Assert.All(state.Players, player => Assert.Equal(3, player.ArenaProgress));
        Assert.Equal(2, state.History.Count(item => item.Kind == "arena-completed"));
        this.engine.ChangeArena(second.Registration, 2, false);
        this.engine.ChangeArena(second.Registration, 1, false);
        Assert.All(this.engine.View(first.Registration).Players, player => Assert.Equal(0, player.ArenaProgress));
        Assert.Equal(0, this.engine.View(outsider.Registration).ArenaProgress);
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

    private sealed class OverworldClock : TimeProvider
    {
        public TimeSpan Elapsed { get; set; }

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;

        public override long GetTimestamp() => this.Elapsed.Ticks;

        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + this.Elapsed;
    }
}
