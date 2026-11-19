using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
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
    }

    [Fact]
    public void BridgeIsEmbeddedInClientAndExportedWithoutAnExternalDownload()
    {
        string path = BridgeInstaller.Install(Path.Combine(this.directory, "Bridge"));
        string text = File.ReadAllText(path);
        Assert.Contains("local function snapshot", text);
        Assert.DoesNotContain("emu:write8(", text);
        Assert.Equal(path, BridgeInstaller.Install(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public async Task PreparedPlayerRetriesTemporaryJoinFailureAndKeepsTheSuccessfulRegistration()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        await using var app = builder.Build();
        int attempts = 0;
        app.MapPost("/api/soullink/join", (JoinRequest request) =>
        {
            if (Interlocked.Increment(ref attempts) == 1) return Results.StatusCode(503);
            Assert.Equal("Prepared player", request.Name);
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
        using var client = new ClientRuntime(configuration, clock, new JoinForm(endpoint, "INVITE", "Prepared player"));
        await client.StartAsync(timeout.Token);
        try
        {
            while (Volatile.Read(ref attempts) < 1) await Task.Delay(25, timeout.Token);
            clock.Now += TimeSpan.FromSeconds(11);
            while (!client.Status().Registered) await Task.Delay(25, timeout.Token);
            Assert.Equal(2, Volatile.Read(ref attempts));
            Assert.Equal("Prepared player", client.Status().Player);
            using var restarted = new ClientRuntime(configuration);
            Assert.True(restarted.Status().Registered);
            Assert.Null(restarted.Status().Snapshot);
        }
        finally { await client.StopAsync(timeout.Token); }
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
        }).Build();
        return new ClientRuntime(configuration, clock);
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => this.Now;
    }
}
