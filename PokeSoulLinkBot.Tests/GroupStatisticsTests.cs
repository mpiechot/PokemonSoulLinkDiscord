using System.Net;
using System.Net.Http.Headers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using PokeSoulLinkBot.Automation;
using SoulLink;
using SoulLink.Client;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class GroupStatisticsTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "soullink-statistics-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PlayerStatisticsIncludeTheWholeGroupAndProtectOtherGroups()
    {
        const string adminKey = "test-key-for-the-local-statistics-endpoint";
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration["SoulLink:AdminKey"] = adminKey;
        builder.Configuration["SoulLink:DatabasePath"] = Path.Combine(this.directory, "group.sqlite");
        builder.Services.AddSingleton<Discord.WebSocket.DiscordSocketClient>();
        builder.Services.AddAutomation(builder.Configuration);
        await using var app = builder.Build();
        app.MapAutomation();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await app.StartAsync(timeout.Token);

        string server = app.Urls.Single();
        using var host = new HostAdminClient(server, adminKey);
        var engine = app.Services.GetRequiredService<AutomationEngine>();
        var group = engine.CreateGroup("Rubin");
        var otherGroup = engine.CreateGroup("Andere Runde");
        var archiveGroup = engine.CreateGroup("Nur Archiv");
        var previousRun = engine.CreateGroupRun(group.Id, new GroupRunRequest("Erster Versuch"));
        var retiredPlayer = Register(previousRun, "Alte Anmeldung");
        engine.ChangeGroupRun(group.Id, previousRun.Id, "end");
        var currentRun = engine.CreateGroupRun(group.Id, new GroupRunRequest("Neuer Versuch"));
        var firstPlayer = Register(currentRun, "Anne");
        var secondPlayer = Register(currentRun, "Bo");
        var otherRun = engine.CreateGroupRun(otherGroup.Id, new GroupRunRequest("Fremder Run"));
        var outsider = Register(otherRun, "Chris");
        var guildRun = engine.Initialize("123456789", 0, "Discord-Lobby");
        var guildPlayer = RegisterInvite(guildRun.Invitation, "Discord-Spieler");

        var continued = new DateTimeOffset(2025, 3, 1, 12, 0, 0, TimeSpan.Zero);
        var store = app.Services.GetRequiredService<AutomationStore>();
        store.Execute(tx =>
        {
            var run = tx.Run(currentRun.Id);
            run.CreatedAt = continued;
            run.Phase = RunPhase.Active;
            run.Players[0].ArenaConfirmed = 0b0111;
            run.Players[1].ArenaConfirmed = 0b0101;
            run.Players[0].Pokemon.Add(new TrackedPokemon { Dead = true, Imported = true });
            run.Players[1].Pokemon.Add(new TrackedPokemon { Dead = true });
            run.Players[1].Pokemon.Add(new TrackedPokemon { Dead = true, State = new PokemonState { IsEgg = true } });
            tx.Save(run);
            tx.RecordReplayEvent(run.Id, new AutomationEvent(Guid.NewGuid(), continued, "caught", "Erster Fang"));
            tx.RecordReplayEvent(run.Id, new AutomationEvent(Guid.NewGuid(), continued, "caught", "Zweiter Fang"));
            tx.RecordReplayEvent(run.Id, new AutomationEvent(Guid.NewGuid(), continued, "extra-catch", "Zusatzfang"));
            return true;
        });

        string archive = Path.Combine(this.directory, "runs.json");
        Guid legacyId = Guid.NewGuid();
        Guid incompleteId = Guid.NewGuid();
        var started = new DateTimeOffset(2025, 2, 2, 16, 0, 0, TimeSpan.Zero);
        var ended = started.AddDays(6);
        string complete = $$$"""
            {"Id":"{{{legacyId}}}","GuildId":"123","Name":"Discord-Versuch","StartedAtUtc":"{{{started:O}}}","EndedAtUtc":"{{{ended:O}}}",
             "EndReason":"Wipe","LinkGroups":[
                {"Route":"Starter","Entries":[{"IsAlive":false}]},
                {"Route":"Route 101","Entries":[{"IsAlive":true},{"IsAlive":false}]}
             ],"activeLinks":[{"Route":"Route 101","Entries":[{"IsAlive":false}]}],
             "CompletedArenas":[{"ArenaNumber":1},{"ArenaNumber":1},{"ArenaNumber":2}]}
            """;
        string incomplete = $$$"""
            {"id":"{{{incompleteId}}}","guildId":"123","name":"Alter Export","startedAtUtc":"2024-08-01T08:00:00Z"}
            """;
        await File.WriteAllTextAsync(archive, $"[{complete},{incomplete}]", timeout.Token);
        Assert.Equal(2, (await host.ImportDiscordRunsAsync(group.Id, archive)).Imported);
        await File.WriteAllTextAsync(archive, "[" + incomplete.Replace(incompleteId.ToString(), Guid.NewGuid().ToString(), StringComparison.Ordinal) + "]", timeout.Token);
        Assert.Equal(1, (await host.ImportDiscordRunsAsync(archiveGroup.Id, archive)).Imported);

        using var first = this.Client(server, firstPlayer);
        using var partner = this.Client(server, secondPlayer);
        using var retired = this.Client(server, retiredPlayer);
        using var visitor = this.Client(server, outsider);
        using var guild = this.Client(server, guildPlayer);
        var summary = await first.GroupStatisticsAsync(timeout.Token);
        Assert.Equal(group.Name, summary.Group);
        Assert.Equal(
            new[] { previousRun.Id, currentRun.Id, legacyId, incompleteId },
            summary.Runs.Select(run => run.Id));
        Assert.Equal(summary.Runs, (await partner.GroupStatisticsAsync(timeout.Token)).Runs);
        Assert.Equal(summary.Runs, (await retired.GroupStatisticsAsync(timeout.Token)).Runs);
        Assert.Equal(summary.Runs, (await host.GroupStatisticsAsync(group.Id, timeout.Token)).Runs);
        Assert.Equal(new RunStatistics(currentRun.Id, currentRun.Name, false, RunPhase.Active, continued, null, null, 2, 2, 2),
            summary.Runs.Single(run => run.Id == currentRun.Id));
        Assert.Equal(new RunStatistics(legacyId, "Discord-Versuch", true, RunPhase.Finished, started, ended, "Wipe", 2, 2, 2),
            summary.Runs.Single(run => run.Id == legacyId));
        var missing = summary.Runs.Single(run => run.Id == incompleteId);
        Assert.Null(missing.Phase);
        Assert.Null(missing.Catches);
        Assert.Null(missing.Deaths);
        Assert.Null(missing.Arenas);

        Assert.Equal(otherRun.Id, Assert.Single((await visitor.GroupStatisticsAsync(timeout.Token)).Runs).Id);
        Assert.Equal(guildRun.Id, Assert.Single((await guild.GroupStatisticsAsync(timeout.Token)).Runs).Id);
        Assert.True(Assert.Single((await host.GroupStatisticsAsync(archiveGroup.Id, timeout.Token)).Runs).DiscordArchive);

        using var http = new HttpClient { BaseAddress = new Uri(server) };
        using var unauthenticated = await http.GetAsync("/api/soullink/statistics", timeout.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, unauthenticated.StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", outsider.Token);
        using var spoofedGroup = await http.GetAsync($"/api/soullink/statistics?group={group.Id:D}", timeout.Token);
        Assert.True(spoofedGroup.Headers.CacheControl?.NoStore);
        var response = Protocol.Deserialize<GroupStatistics>(await spoofedGroup.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal(otherRun.Id, Assert.Single(response.Runs).Id);
        using var hostOnly = await http.GetAsync($"/api/soullink/admin/groups/{group.Id:D}/statistics", timeout.Token);
        Assert.Equal(HttpStatusCode.Unauthorized, hostOnly.StatusCode);

        (Registration Registration, string Token) Register(HostRun run, string name) => RegisterInvite(run.Invitation, name);

        (Registration Registration, string Token) RegisterInvite(string invitation, string name)
        {
            string token = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            return (engine.Join(new JoinRequest(invitation, name, Guid.NewGuid(), token, "AXVD")), token);
        }
    }

    public void Dispose()
    {
        if (Directory.Exists(this.directory)) Directory.Delete(this.directory, recursive: true);
    }

    private ClientRuntime Client(string server, (Registration Registration, string Token) account)
    {
        string path = Path.Combine(this.directory, $"{account.Registration.ClientId:N}.json");
        File.WriteAllText(path, Protocol.Serialize(new ClientSettings
        {
            Server = server,
            Registration = account.Registration,
            Token = account.Token,
            ClientId = account.Registration.ClientId,
        }));
        return new ClientRuntime(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SettingsPath"] = path,
        }).Build());
    }
}
