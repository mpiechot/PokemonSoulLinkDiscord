using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SoulLink;
using SoulLink.Client;
using SoulLink.Demo;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class PlayerClientTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "soullink-player-tests-" + Guid.NewGuid().ToString("N"));

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
    public void RosterProjectsActualLinksIntoPlayerColumnsAndKeepsUnlinkedPokemonSeparate()
    {
        Guid first = Guid.NewGuid(), second = Guid.NewGuid();
        var linkLeft = new DashboardPokemon(new PokemonState { Identity = "A", Species = 25, MetArea = 16 }, false, true);
        var linkRight = new DashboardPokemon(new PokemonState { Identity = "B", Species = 1, MetArea = 16 }, false, true);
        var unlinked = new DashboardPokemon(new PokemonState { Identity = "C", Species = 4, MetArea = 16 }, false, true);
        var inBox = new DashboardPokemon(new PokemonState { Identity = "D", Species = 7, Container = Placement.Box }, false, true);
        var run = new ClientRunView("Test", RunPhase.Active, Guid.NewGuid(),
            [new("Anne", first, "", "Ready", Pokemon: [linkLeft, unlinked]), new("Max", second, "", "Ready", Pokemon: [linkRight, inBox])],
            [], [], [new("Route 101", "ACTIVE", [new("Anne", linkLeft, first), new("Max", linkRight, second)])]);
        var view = new ClientView("", "", true, "", 0, "", null, null, run, [], false);

        var team = RosterGridData.From(view, RosterView.Team);
        Assert.Equal(new[] { first, second }, team.Columns.Select(c => c.Id));
        Assert.Equal(2, team.Rows.Length);
        Assert.True(team.Rows[0].Linked);
        Assert.Equal(new[] { linkLeft, linkRight }, team.Rows[0].Slots);
        Assert.False(team.Rows[1].Linked);
        Assert.Equal(unlinked, team.Rows[1].Slots[0]);
        Assert.Null(team.Rows[1].Slots[1]);
        Assert.Equal(inBox, Assert.Single(RosterGridData.From(view, RosterView.Box).Rows).Slots[1]);
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
