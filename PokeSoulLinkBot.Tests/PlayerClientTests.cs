using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PokeSoulLinkBot.Automation;
using SoulLink;
using SoulLink.Client;
using SoulLink.Demo;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class PlayerClientTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "soullink-player-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task HostImportsDiscordRunsIntoOneGroupAndPreservesTheSourceDocument()
    {
        const string key = "test-key-for-the-local-host-import-endpoint";
        string database = Path.Combine(this.directory, "archive.sqlite");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration["SoulLink:AdminKey"] = key;
        builder.Configuration["SoulLink:DatabasePath"] = database;
        builder.Services.AddSingleton<Discord.WebSocket.DiscordSocketClient>();
        builder.Services.AddAutomation(builder.Configuration);
        await using var app = builder.Build();
        app.MapAutomation();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await app.StartAsync(timeout.Token);

        using var host = new HostAdminClient(app.Urls.Single(), key);
        using var guest = new HostAdminClient(app.Urls.Single(), new string('x', 32));
        var group = await host.CreateGroupAsync("Rubin-Gruppe");
        var other = await host.CreateGroupAsync("Andere Gruppe");
        var live = await host.CreateRunAsync(group.Id, "Aktuelle Lobby");
        Guid runId = Guid.NewGuid();
        string run = $$$"""
            {"Id":"{{{runId}}}","GuildId":"123","Name":"Alter Run","Game":"Rubin",
             "StartedAtUtc":"2025-02-02T16:00:00Z","EndReason":"Wipe",
             "Players":[{"UserId":123,"UserName":"Anne"}],"CompletedArenas":[],"LinkGroups":[],
             "activeLinks":[null],"HistoricalField":{"keep":true}}
            """;
        string path = Path.Combine(this.directory, "runs.json");
        string archive = "[" + run + "]";
        await File.WriteAllTextAsync(path, archive, new System.Text.UTF8Encoding(true), timeout.Token);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => guest.ImportDiscordRunsAsync(group.Id, path));
        Assert.Equal(new DiscordRunImportResult(1, 0), await host.ImportDiscordRunsAsync(group.Id, path));
        Assert.Equal(new DiscordRunImportResult(0, 1), await host.ImportDiscordRunsAsync(group.Id, path));
        Assert.Equal(archive, await File.ReadAllTextAsync(path, timeout.Token));

        string anotherRun = run.Replace(runId.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal);
        string mixedArchive = Path.Combine(this.directory, "mixed.json");
        await File.WriteAllTextAsync(mixedArchive, $"[{anotherRun},{run}]", timeout.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.ImportDiscordRunsAsync(other.Id, mixedArchive));
        await File.WriteAllTextAsync(mixedArchive, "[" + anotherRun + ",{}]", timeout.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.ImportDiscordRunsAsync(other.Id, mixedArchive));

        var reopened = new AutomationStore(database);
        var saved = Assert.Single(reopened.Execute(tx => tx.ArchivedDiscordRuns(group.Id)));
        Assert.Equal(runId, saved.Id);
        Assert.Equal(run, saved.Document);
        Assert.Empty(reopened.Execute(tx => tx.ArchivedDiscordRuns(other.Id)));
        Assert.Equal(live.Id, Assert.Single(await host.RunsAsync(group.Id)).Id);
    }

    [Fact]
    public void AnInvitationCanSwitchTheSameExeToAnotherRunAndStaySelectedOnRestart()
    {
        var profiles = new PlayerProfileLibrary(this.directory);
        var old = new PlayerProfile(Guid.NewGuid(), "https://example.invalid", "old-code", Label: "Alter Run");
        Assert.Equal(old, profiles.OpenForStartup(old, selection: null));

        var next = new PlayerProfile(Guid.NewGuid(), "https://new.example.invalid", "new-code", "Host", "Neue Runde / Rubin", PlayerMarker.Moon);
        string code = PlayerInvitation.Encode(next);
        var invite = PlayerInvitation.Read("  " + code + "\r\n", old.Server);
        Assert.Equal(next.Server, invite.Server);
        Assert.Equal(next.Invitation, invite.Invitation);
        Assert.Equal(next.Label, invite.Label);
        Assert.Null(invite.Name);
        Assert.Null(invite.Marker);
        var added = profiles.AddInvitation(invite.Server, invite.Invitation, invite.Label!);
        Assert.Equal(added.Id, profiles.AddInvitation(invite.Server, invite.Invitation, invite.Label!).Id);
        Assert.Equal(added, profiles.OpenForStartup(old, added.Id));

        var reopened = new PlayerProfileLibrary(this.directory);
        Assert.Equal(added, reopened.OpenForStartup(old, selection: null));
        Assert.Equal(old, reopened.Read(old.Id));
        Assert.Null(reopened.OpenForStartup(old, Guid.Empty));
        Assert.Null(reopened.OpenForStartup(old, selection: null));
    }

    [Fact]
    public void ExistingCodesStillUseAnExplicitServerAndBrokenShareCodesAreRejected()
    {
        const string server = "https://example.invalid";
        Assert.Equal("legacy-code", PlayerInvitation.Read("legacy-code", server).Invitation);
        Assert.Throws<InvalidDataException>(() => PlayerInvitation.Read("SL1-%%broken", server));
        Assert.Throws<InvalidDataException>(() => PlayerInvitation.Read("legacy-code", "http://example.invalid"));
        Assert.Throws<InvalidDataException>(() => PlayerInvitation.Read("legacy\ncode", server));
    }

    [Fact]
    public void RunProfileLibraryKeepsAndReopensSeparateGroupInvitations()
    {
        var profiles = new PlayerProfileLibrary(this.directory);
        var groupOne = new RunGroup(Guid.NewGuid(), "Gruppe eins");
        var groupTwo = new RunGroup(Guid.NewGuid(), "Gruppe zwei");
        var runOne = new HostRun(Guid.NewGuid(), "Rubin", RunPhase.Lobby, true, "INVITE-ONE", 0, 0);
        var runTwo = new HostRun(Guid.NewGuid(), "Rubin", RunPhase.Lobby, true, "INVITE-TWO", 0, 0);
        const string server = "https://soul.example.test";
        var one = PlayerProfileLibrary.ForHostedRun(server, groupOne, runOne);
        var two = PlayerProfileLibrary.ForHostedRun(server, groupTwo, runTwo);
        profiles.Remember(one);
        profiles.Remember(two);
        profiles.Choose(one.Id);
        Assert.Equal(one, new PlayerProfileLibrary(this.directory).Current());
        profiles.Choose(two.Id);
        Assert.Equal(two, new PlayerProfileLibrary(this.directory).Current());
        Assert.Equal(new[] { one, two }, profiles.List());
        Assert.Equal(one.Id, profiles.AddInvitation(server, one.Invitation, one.Label!).Id);
        profiles.Choose(Guid.Empty);
        Assert.Null(profiles.Current());
        Assert.Equal(2, profiles.List().Length);
    }

    [Fact]
    public void FreshClientIsEmptyAndDoesNotCreateDemoRegistration()
    {
        using var client = this.Client();
        var view = client.Status();
        Assert.False(view.IsDemo);
        Assert.False(view.Registered);
        Assert.Equal("", view.Player);
        Assert.Null(view.Snapshot);
        Assert.Null(view.Run);
        Assert.NotEqual("Online", view.Bridge);
    }

    [Fact]
    public void PlayerRejectsSyntheticObservationsButExplicitTestHarnessCanAcceptThem()
    {
        using var player = this.Client();
        var frame = SyntheticPokemon.Frame(1, SyntheticPokemon.Raw(1, 392));
        Assert.Throws<InvalidDataException>(() => player.AcceptFrame(frame));
        Assert.Null(player.Status().Snapshot);
        using var harness = this.Client(demo: true);
        harness.AcceptFrame(frame);
        Assert.True(harness.Status().IsDemo);
        Assert.NotNull(harness.Status().Snapshot);
    }

    [Fact]
    public void AStaleBridgeDoesNotKeepPartyAndOnlineStatusVisible()
    {
        var clock = new TestClock();
        using var client = this.Client(clock: clock);
        client.AcceptFrame(SyntheticPokemon.Frame(1, SyntheticPokemon.Raw(1, 392)) with { LayoutId = "ruby-reference" });
        Assert.NotNull(client.Status().Snapshot);
        Assert.Equal("Online", client.Status().Bridge);
        clock.Now += TimeSpan.FromSeconds(11);
        Assert.Null(client.Status().Snapshot);
        Assert.NotEqual("Online", client.Status().Bridge);
    }

    [Fact]
    public void LocalPlayerFeedShowsACatchAfterTheInitialInventory()
    {
        using var client = this.Client(demo: true);
        var starter = SyntheticPokemon.Raw(1, 392);
        client.AcceptFrame(SyntheticPokemon.Frame(1, starter));
        Assert.Empty(client.Status().Run!.History);

        var caught = SyntheticPokemon.Raw(2, 25, slot: 1);
        client.AcceptFrame(SyntheticPokemon.Frame(2, starter, caught));
        client.AcceptFrame(SyntheticPokemon.Frame(3, starter, caught));

        var item = Assert.Single(client.Status().Run!.History, e => e.Kind == "caught");
        Assert.Equal("caught", item.Kind);
        Assert.Contains("Pikachu", item.Message);
        Assert.Equal(new[] { 16 }, client.Status().Run!.CaughtAreas);
    }

    [Fact]
    public void RosterNamesAppendCustomNicknamesAndKeepDefaultNamesAndEggsCompact()
    {
        var charmander = new PokemonState { Species = 4 };
        Assert.Equal("Glumanda (Flamme)", RubyNames.PokemonWithNickname(charmander with { Nickname = " Flamme " }));
        Assert.Equal("Glumanda", RubyNames.PokemonWithNickname(charmander with { Nickname = "GLUMANDA" }));
        Assert.Equal("Glumanda", RubyNames.PokemonWithNickname(charmander));
        Assert.Equal("Ei", RubyNames.PokemonWithNickname(charmander with { IsEgg = true, Nickname = "Ei" }));
    }

    [Fact]
    public void RosterProjectsActualLinksIntoPlayerColumnsAndKeepsUnlinkedPokemonSeparate()
    {
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();
        var linkLeft = new DashboardPokemon(new PokemonState { Identity = "A", Species = 25, MetArea = 16 }, false, true);
        var linkRight = new DashboardPokemon(new PokemonState { Identity = "B", Species = 1, MetArea = 16 }, false, true);
        var unlinked = new DashboardPokemon(new PokemonState { Identity = "C", Species = 4, MetArea = 16 }, false, true);
        var inBox = new DashboardPokemon(new PokemonState { Identity = "D", Species = 7, Container = Placement.Box }, false, true);
        var run = new ClientRunView("Test", RunPhase.Active, Guid.NewGuid(),
            [new("Anne", first, "", "Ready", Pokemon: [linkLeft, unlinked]), new("Max", second, "", "Ready", Pokemon: [linkRight, inBox])],
            [], [], [new("Starter", "ACTIVE", [new("Anne", linkLeft, first), new("Max", linkRight, second)])]);
        var view = new ClientView("", "", true, "", 0, "", null, null, run, [], false);

        var team = RosterGridData.From(view, RosterView.Team);
        Assert.Equal(new[] { first, second }, team.Columns.Select(c => c.Id));
        Assert.Equal(2, team.Rows.Length);
        Assert.True(team.Rows[0].Linked);
        Assert.Equal("Starter", team.Rows[0].LinkName);
        Assert.Equal(new[] { linkLeft, linkRight }, team.Rows[0].Slots);
        Assert.False(team.Rows[1].Linked);
        Assert.Null(team.Rows[1].LinkName);
        Assert.Equal(unlinked, team.Rows[1].Slots[0]);
        Assert.Null(team.Rows[1].Slots[1]);
        var box = RosterGridData.From(view, RosterView.Box);
        Assert.True(box.ShowRouteColumn);
        Assert.False(team.ShowRouteColumn);
        Assert.Equal(inBox, Assert.Single(box.Rows).Slots[1]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MissedEncounterIsVisibleWithAnyNumberOfPriorCatches(int caught)
    {
        var players = Enumerable.Range(0, 3).Select(i => new AutomationPlayer { Name = $"Player {i + 1}" }).ToArray();
        var failed = new Encounter { Area = 18, Failed = true, Dead = true };
        foreach (var player in players.Take(caught))
        {
            var mon = new TrackedPokemon
            {
                State = new PokemonState { Identity = player.Name, Species = 25, MetArea = 18 },
                Dead = true,
            };
            player.Pokemon.Add(mon);
            failed.Members.Add(player.Id, mon.Id);
        }

        var progress = DashboardProjection.FromRun(new AutomationRun { Players = [.. players], Encounters = [failed] });
        Assert.Equal(new[] { 18 }, progress.CaughtAreas);
        var view = new ClientView("", "", true, "", 0, "", null, null, progress, [], false);
        var dead = RosterGridData.From(view, RosterView.Dead);
        Assert.True(dead.ShowRouteColumn);
        Assert.Equal(players.Select(player => player.Id), dead.Columns.Select(column => column.Id));
        var row = Assert.Single(dead.Rows);
        Assert.True(row.Failed);
        Assert.Equal("Route 103", row.LinkName);
        Assert.Equal(3, row.Slots.Length);
        Assert.Equal(caught, row.Slots.Count(mon => mon is not null));
        Assert.Equal(3 - caught, row.MissingMembers!.Count(missing => missing));
        Assert.Empty(RosterGridData.From(view, RosterView.Box).Rows);
    }

    [Fact]
    public void SharedGridUsesTheSameServerInventoryForLocalAndRemotePlayers()
    {
        Guid player = Guid.NewGuid();
        var accepted = new PokemonState { Identity = "A", Species = 25, Hp = 19 };
        var ownGrid = new ClientPlayerView("Anne", player, "", "OutOfSync", Pokemon: [new(accepted, false, true)]);
        var run = new ClientRunView("Test", RunPhase.Active, Guid.NewGuid(), [ownGrid], [], []);
        var raw = new GameSnapshot { Pokemon = [accepted with { Hp = 5 }, new PokemonState { Identity = "B", Slot = 1, Species = 1 }] };
        var view = new ClientView("Anne", "", true, "", 0, "", null, raw, run, [], false, LocalPlayerId: player);

        var row = Assert.Single(RosterGridData.From(view, RosterView.Team).Rows);
        Assert.Equal(19, row.Slots[0]?.State.Hp);
        Assert.Equal(2, RosterGridData.From(view with { IsLocalRun = true }, RosterView.Team).Rows.Length);
    }

    [Fact]
    public async Task SavedRegistrationWithUnavailableRunServerStillShowsLocalCatches()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var unavailableServer = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await unavailableServer.StartAsync(timeout.Token);
        Directory.CreateDirectory(this.directory);
        string settingsPath = Path.Combine(this.directory, "player.json");
        var settings = new ClientSettings { Server = unavailableServer.Urls.Single(), Name = "Previously registered" };
        settings = settings with { Registration = new Registration(Guid.NewGuid(), Guid.NewGuid(), settings.ClientId) };
        File.WriteAllText(settingsPath, Protocol.Serialize(settings));
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SettingsPath"] = settingsPath,
            ["EnableLocalRun"] = "true",
            ["BridgePort"] = "0",
        }).Build();
        using var client = new ClientRuntime(configuration);
        await client.StartAsync(timeout.Token);
        try
        {
            using var bridge = new System.Net.Sockets.TcpClient();
            await bridge.ConnectAsync(System.Net.IPAddress.Loopback, client.Status().BridgePort, timeout.Token);
            var starter = SyntheticPokemon.Raw(1, 392);
            await SendFrame(SyntheticPokemon.Frame(1, starter));
            while (client.Status() is not { Snapshot.Frame: 1 } ||
                   !client.Status().State.StartsWith("Offline", StringComparison.Ordinal))
                await Task.Delay(25, timeout.Token);

            await SendFrame(SyntheticPokemon.Frame(2, starter, SyntheticPokemon.Raw(2, 25, slot: 1)));
            while (client.Status().Snapshot?.Frame != 2) await Task.Delay(25, timeout.Token);

            var view = client.Status();
            Assert.True(view.Registered);
            Assert.True(view.IsLocalRun);
            Assert.NotEqual(settings.Registration.RunId, view.Run!.Id);
            Assert.Contains("Pikachu", Assert.Single(view.Run.History, e => e.Kind == "caught").Message);
            Assert.Equal("Pikachu", (await client.CheckCatchAsync(view.Run.Id, "Raichu")).PreviouslyCaught);
            await Assert.ThrowsAsync<InvalidOperationException>(() => client.ChangeArenaAsync(settings.Registration.RunId, 1, true));
            await client.ChangeArenaAsync(view.Run.Id, 1, true);
            Assert.Single(client.Status().Run!.History, e => e.Kind == "arena-completed");
            Assert.Equal(settings, Protocol.Deserialize<ClientSettings>(File.ReadAllText(settingsPath)));

            async Task SendFrame(BridgeFrame frame)
            {
                string json = Protocol.Serialize(frame with { LayoutId = "ruby-axvd-reference-v2" });
                await bridge.GetStream().WriteAsync(System.Text.Encoding.UTF8.GetBytes(json + "\n"), timeout.Token);
            }
        }
        finally
        {
            await client.StopAsync(timeout.Token);
        }
    }

    [Fact]
    public async Task LoadingALaterSnapshotKeepsRecordedTimesAndConfirmedDeaths()
    {
        var clock = new TestClock();
        using var client = this.Client(demo: true, clock: clock);
        var starter = SyntheticPokemon.Raw(1, 392);
        var caught = SyntheticPokemon.Raw(2, 25, slot: 1);
        client.AcceptFrame(SyntheticPokemon.Frame(1, starter));
        clock.Now = clock.Now.AddSeconds(5);
        client.AcceptFrame(SyntheticPokemon.Frame(2, starter, caught));
        DateTimeOffset caughtAt = clock.Now;
        clock.Now = clock.Now.AddSeconds(2);
        client.AcceptFrame(SyntheticPokemon.Frame(3, starter, SyntheticPokemon.Raw(2, 25, slot: 1, hp: 0)) with { InBattle = true });
        var run = client.Status().Run!;
        var faint = Assert.Single(run.Players.Single().PendingDeaths!);
        await client.ResolveFaintAsync(run.Id, faint.PokemonId, faint.IncidentId, true, "Test");
        DateTimeOffset diedAt = clock.Now;

        clock.Now = clock.Now.AddSeconds(10);
        client.AcceptFrame(SyntheticPokemon.Frame(4, starter, caught, SyntheticPokemon.Raw(3, 1, slot: 2, area: 17)) with { PlaySeconds = 240 });

        var view = client.Status();
        Assert.Equal(caughtAt, Assert.Single(view.Run!.History, e => e.Kind == "caught").At);
        Assert.Equal(diedAt, Assert.Single(view.Run.History, e => e.Kind == "death").At);
        Assert.Contains(RosterGridData.From(view, RosterView.Dead).Rows, row => row.Slots.Any(p => p?.State.Species == 25));
    }

    [Fact]
    public async Task ReopeningTheSameLocalGameRestoresHistoryAndDeadList()
    {
        Directory.CreateDirectory(this.directory);
        string ruby = Path.Combine(this.directory, "ruby.gba");
        File.WriteAllBytes(ruby, [1, 2, 3]);
        string otherRuby = Path.Combine(this.directory, "other-ruby.gba");
        File.WriteAllBytes(otherRuby, [4, 5, 6]);
        var clock = new TestClock();
        var starter = SyntheticPokemon.Raw(1, 392);
        var caught = SyntheticPokemon.Raw(2, 25, slot: 1);
        const uint trainer = 1234;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SettingsPath"] = Path.Combine(this.directory, "player.json"),
            ["LocalRunDirectory"] = Path.Combine(this.directory, "test-runs"),
            ["EnableLocalRun"] = "true",
            ["AllowSyntheticFrames"] = "true",
        }).Build();

        Guid runId;
        Guid caughtId;
        DateTimeOffset caughtAt = clock.Now;
        DateTimeOffset diedAt = caughtAt.AddSeconds(3);
        using (var first = new ClientRuntime(configuration, clock))
        {
            await first.SelectLocalGameAsync(ruby);
            first.AcceptFrame(SyntheticPokemon.Frame(1, starter) with { TrainerId = trainer });
            first.AcceptFrame(SyntheticPokemon.Frame(2, starter, caught) with { TrainerId = trainer });
            var run = first.Status().Run!;
            runId = run.Id;
            caughtId = Assert.Single(run.History, e => e.Kind == "caught").Id;
            clock.Now = diedAt;
            first.AcceptFrame(SyntheticPokemon.Frame(3, starter, SyntheticPokemon.Raw(2, 25, slot: 1, hp: 0)) with
            {
                TrainerId = trainer,
                InBattle = true,
            });
            var faint = Assert.Single(first.Status().Run!.Players.Single().PendingDeaths!);
            await first.ResolveFaintAsync(runId, faint.PokemonId, faint.IncidentId, true, "Test");
        }

        clock.Now = diedAt.AddHours(2);
        using var reopened = new ClientRuntime(configuration, clock);
        await reopened.SelectLocalGameAsync(ruby);
        reopened.AcceptFrame(SyntheticPokemon.Frame(1, starter) with { TrainerId = trainer });
        var restored = reopened.Status();
        Assert.Equal(runId, restored.Run!.Id);
        var originalCatch = Assert.Single(restored.Run.History, e => e.Kind == "caught");
        Assert.Equal(caughtId, originalCatch.Id);
        Assert.Equal(caughtAt, originalCatch.At);
        Assert.Equal(diedAt, Assert.Single(restored.Run.History, e => e.Kind == "death").At);
        Assert.Contains(RosterGridData.From(restored, RosterView.Dead).Rows, row => row.Slots.Any(p => p?.State.Species == 25));

        reopened.AcceptFrame(SyntheticPokemon.Frame(1, starter, caught) with { TrainerId = trainer + 1 });
        Assert.NotEqual(runId, reopened.Status().Run!.Id);
        Assert.DoesNotContain(reopened.Status().Run!.History, e => e.Kind is "caught" or "death");

        await reopened.SelectLocalGameAsync(otherRuby);
        reopened.AcceptFrame(SyntheticPokemon.Frame(1, starter) with { TrainerId = trainer });
        Assert.NotEqual(runId, reopened.Status().Run!.Id);
        Assert.DoesNotContain(reopened.Status().Run!.History, e => e.Kind is "caught" or "death");
    }

    [Fact]
    public void EmptyTeamRequiresAValidOverworldPositionAndCompleteInventory()
    {
        var title = SyntheticPokemon.Frame(1);
        Assert.Throws<InvalidDataException>(() => title.Parse());
        var field = title with { Position = new MapPosition(5, 7, 28, 24, true) };
        Assert.Empty(field.Parse().Pokemon);
        Assert.Throws<InvalidDataException>(() => (field with { InBattle = true }).Parse());
        Assert.Throws<InvalidDataException>(() => (field with { CompleteInventory = false }).Parse());
        Assert.Throws<InvalidDataException>(() => (field with { Pokemon = [new RawPokemon(new string('0', 200), Placement.Party, 0)] }).Parse());
    }

    [Fact]
    public void BattleHealthOverridesOnlyAValidPartySlotWhileInBattle()
    {
        var party = SyntheticPokemon.Raw(1, 25) with { LiveHp = 7 };
        var box = SyntheticPokemon.Raw(2, 25, box: true) with { LiveHp = 7 };
        var battle = SyntheticPokemon.Frame(1, party, box) with { InBattle = true };
        var observed = battle.Parse().Pokemon;
        Assert.Equal(7, observed[0].Hp);
        Assert.Null(observed[1].Hp);
        Assert.Equal(20, (battle with { InBattle = false }).Parse().Pokemon[0].Hp);
        Assert.Equal(20, (battle with { Pokemon = [party with { LiveHp = 21 }] }).Parse().Pokemon[0].Hp);
    }

    [Fact]
    public async Task OwnedScriptingBridgeIsReadyBeforeTheFirstGameSnapshot()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SettingsPath"] = Path.Combine(this.directory, "startup.json"),
            ["BridgePort"] = "0",
        }).Build();
        using var client = new ClientRuntime(configuration);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await client.StartAsync(timeout.Token);
        try
        {
            string session = client.BeginLocalItemSession();
            using var peer = new System.Net.Sockets.TcpClient();
            await peer.ConnectAsync(System.Net.IPAddress.Loopback, client.Status().BridgePort, timeout.Token);
            using var sender = new StreamWriter(peer.GetStream()) { AutoFlush = true };
            string ready = Protocol.Serialize(new BridgeFrame { Type = "bridge-ready", ItemSession = session });
            await sender.WriteLineAsync(ready.AsMemory(), timeout.Token);
            while (!client.OwnedBridgeReady) await Task.Delay(25, timeout.Token);
            Assert.Null(client.Status().Snapshot);
            _ = client.BeginLocalItemSession();
            Assert.False(client.OwnedBridgeReady);
        }
        finally
        {
            await client.StopAsync(timeout.Token);
        }
    }

    [Fact]
    public async Task ClosingClientCancelsAnInFlightJoinAndReleasesItsBridge()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        var joinStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        app.MapPost("/api/soullink/join", async (HttpContext context) =>
        {
            joinStarted.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted);
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await app.StartAsync(timeout.Token);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SettingsPath"] = Path.Combine(this.directory, "shutdown.json"),
            ["BridgePort"] = "0",
        }).Build();
        using var client = new ClientRuntime(configuration);
        await client.QueueJoinAsync(new JoinForm(app.Urls.Single(), "INVITE", "Player"));
        await client.StartAsync(timeout.Token);
        int port = client.Status().BridgePort;
        try
        {
            await joinStarted.Task.WaitAsync(timeout.Token);
            await client.StopAsync(timeout.Token).WaitAsync(TimeSpan.FromSeconds(3));
            Assert.True(client.ExecuteTask!.IsCompleted);
            var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            try { listener.Start(); }
            finally { listener.Stop(); }
        }
        finally
        {
            await client.StopAsync(timeout.Token);
        }
    }

    [Fact]
    public void PlayerCopiesKeepExecutableBytesAndReplaceOnlyJoinDetails()
    {
        Directory.CreateDirectory(this.directory);
        string source = Path.Combine(this.directory, "original.exe");
        byte[] bytes = Enumerable.Range(0, 2048).Select(i => (byte)(i % 256)).ToArray();
        File.WriteAllBytes(source, bytes);
        var profile = new PlayerProfile(Guid.NewGuid(), "https://example.test", "INVITE", "Alex");
        string first = Path.Combine(this.directory, "Alex.exe");
        PlayerBundle.Create(source, first, profile);
        Assert.Equal(profile, PlayerBundle.Read(first));
        Assert.Equal(bytes, File.ReadAllBytes(first)[..bytes.Length]);
        Assert.Null(PlayerBundle.Read(source));
        var secondProfile = profile with { Id = Guid.NewGuid(), Name = "Sam" };
        string second = Path.Combine(this.directory, "Sam.exe");
        PlayerBundle.Create(first, second, secondProfile);
        Assert.Equal(secondProfile, PlayerBundle.Read(second));
        Assert.DoesNotContain("Alex", System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(second)[bytes.Length..]));
        Assert.Throws<InvalidDataException>(() => PlayerBundle.Create(first, first, profile));
    }

    [Fact]
    public void BundleRejectsInsecureRemoteDestinationsAndMentionNames()
    {
        var profile = new PlayerProfile(Guid.NewGuid(), "http://remote.test", "INVITE", "Player");
        Assert.Throws<InvalidDataException>(() => PlayerBundle.Validate(profile));
        Assert.Throws<InvalidDataException>(() => PlayerBundle.Validate(profile with { Server = "https://example.test", Name = "@everyone" }));
        PlayerBundle.Validate(profile with { Server = "http://127.0.0.1:5080" });
        PlayerBundle.Validate(profile with { Server = "https://example.test", Name = null });
    }

    [Fact]
    public void BridgeIsEmbeddedInClientAndExportedWithoutAnExternalDownload()
    {
        string path = BridgeInstaller.Install(Path.Combine(this.directory, "Bridge"));
        string text = File.ReadAllText(path);
        Assert.Contains("local function snapshot", text);
        Assert.DoesNotContain("SOULLINK_ITEM_SESSION = \"", text);
        string token = new string('A', 32);
        string itemBridge = BridgeInstaller.Install(Path.GetDirectoryName(path)!, itemSession: token);
        Assert.StartsWith($"SOULLINK_ITEM_SESSION = \"{token}\"", File.ReadAllText(itemBridge));
        Assert.Equal(path, BridgeInstaller.Install(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public async Task PreparedPlayerRetriesTemporaryJoinFailureAndKeepsTheSuccessfulRegistration()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        await using var app = builder.Build();
        int attempts = 0;
        app.MapPost("/api/soullink/join", (JoinRequest request) =>
        {
            if (Interlocked.Increment(ref attempts) == 1) return Results.StatusCode(503);
            Assert.Equal("Self-selected name", request.Name);
            Assert.Equal(PlayerMarker.Star, request.Marker);
            return Results.Json(new Registration(Guid.NewGuid(), Guid.NewGuid(), request.ClientId), Protocol.Json);
        });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await app.StartAsync(timeout.Token);
        string endpoint = app.Urls.Single();
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SettingsPath"] = Path.Combine(this.directory, "prepared.json"),
            ["BridgePort"] = port.ToString(),
        }).Build();
        var clock = new TestClock();
        using (var firstStart = new ClientRuntime(configuration, clock))
        {
            await firstStart.QueueJoinAsync(new JoinForm(endpoint, "INVITE", "Self-selected name", PlayerMarker.Star));
        }
        using var client = new ClientRuntime(configuration, clock);
        await client.StartAsync(timeout.Token);
        try
        {
            while (Volatile.Read(ref attempts) < 1) await Task.Delay(25, timeout.Token);
            clock.Now += TimeSpan.FromSeconds(11);
            while (!client.Status().Registered) await Task.Delay(25, timeout.Token);
            Assert.Equal(2, Volatile.Read(ref attempts));
            Assert.Equal("Self-selected name", client.Status().Player);
            Assert.Equal(PlayerMarker.Star, client.Status().Marker);
            using var restarted = new ClientRuntime(configuration);
            Assert.True(restarted.Status().Registered);
            Assert.Equal(PlayerMarker.Star, restarted.Status().Marker);
            Assert.Null(restarted.Status().Snapshot);
        }
        finally { await client.StopAsync(timeout.Token); }
    }

    [Fact]
    public void DescribesRubyTypesAndGymOrderUsingGenerationThreeData()
    {
        Assert.Equal(new RubySpeciesAppearance(280, "psychic", "psychic"), RubyDashboardCatalog.Appearance(392));
        Assert.Equal(386, Enumerable.Range(1, 411).Count(id => RubyDashboardCatalog.Appearance(id) is not null));
        Assert.Equal("Norman", RubyDashboardCatalog.NextGym(0b00001111)!.Name);
        Assert.Equal(new[] { 28, 30, 31 }, RubyDashboardCatalog.NextGym(0b00001111)!.Levels);
        Assert.Null(RubyDashboardCatalog.NextGym(byte.MaxValue));
        Assert.Contains(RubyDashboardCatalog.Areas, area => area.Area == MapCatalog.Section(19, 1));
    }

    public void Dispose()
    {
        if (Directory.Exists(this.directory)) Directory.Delete(this.directory, true);
    }

    private ClientRuntime Client(bool demo = false, TimeProvider? clock = null)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SettingsPath"] = Path.Combine(this.directory, demo ? "harness.json" : "player.json"),
            ["AllowSyntheticFrames"] = demo.ToString(),
            ["EnableLocalRun"] = demo.ToString(),
        }).Build();
        return new ClientRuntime(configuration, clock);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => this.Now;
    }
}
