using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Bot.Hosting;
using PokeSoulLinkBot.Core.Models;
using PokeSoulLinkBot.Infrastructure.Persistence;
using SoulLink;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class ReadOnlyRunStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "readonly-run-tests-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public void LegacyWritesAndMutationsLeavePrimaryBackupAndSubsequentReadsUnchanged()
    {
        string path = this.CreateArchive();
        string backup = path + ".bak";
        byte[] primaryHash = Hash(path);
        byte[] backupHash = Hash(backup);
        DateTime primaryTime = File.GetLastWriteTimeUtc(path);
        DateTime backupTime = File.GetLastWriteTimeUtc(backup);
        var archive = new ReadOnlyRunStore(new RunStore(path));

        var copy = archive.GetActiveRun("guild")!;
        copy.Name = "Changed only in a detached copy";
        copy.Players[0].UserName = "Changed player";
        archive.GetRunsForGuild("guild")[0].Players.Clear();
        Assert.Throws<InvalidOperationException>(() => archive.Save());
        Assert.Throws<InvalidOperationException>(() => archive.AddRun(new SoulLinkRun()));
        Assert.Throws<InvalidOperationException>(() => new RunService(archive).EndRun("guild", "Accidental command"));
        Assert.Throws<InvalidOperationException>(() => new RunService(archive).StartRun("another-guild", "Manual", "ruby",
            new[] { new RunPlayer { UserId = 2, UserName = "Player" } }));

        var unchanged = archive.GetActiveRun("guild")!;
        Assert.Equal("Historical run", unchanged.Name);
        Assert.Equal("Original player", Assert.Single(unchanged.Players).UserName);
        Assert.Null(unchanged.EndedAtUtc);
        Assert.Equal(primaryHash, Hash(path));
        Assert.Equal(backupHash, Hash(backup));
        Assert.Equal(primaryTime, File.GetLastWriteTimeUtc(path));
        Assert.Equal(backupTime, File.GetLastWriteTimeUtc(backup));
        Assert.Equal(2, Directory.GetFiles(this.directory).Length);
    }

    [Fact]
    public void BackupReadDoesNotRestoreOrCreateThePrimaryJson()
    {
        string path = this.CreateArchive();
        File.Delete(path);
        byte[] backupHash = Hash(path + ".bak");
        var archive = new ReadOnlyRunStore(new RunStore(path));
        Assert.NotNull(archive.GetActiveRun("guild"));
        Assert.Throws<InvalidOperationException>(() => archive.Save());
        Assert.False(File.Exists(path));
        Assert.Equal(backupHash, Hash(path + ".bak"));
    }

    [Fact]
    public void EmptyArchiveDoesNotCreateJsonOrItsDirectory()
    {
        var archive = new ReadOnlyRunStore(new RunStore(Path.Combine(this.directory, "runs.json")));
        Assert.Null(archive.GetActiveRun("guild"));
        Assert.Throws<InvalidOperationException>(() => archive.AddRun(new SoulLinkRun()));
        Assert.Throws<InvalidOperationException>(() => archive.Save());
        Assert.False(Directory.Exists(this.directory));
    }

    [Fact]
    public void ActualHostUsesReadOnlyArchiveWhileAutomationPersistsSeparately()
    {
        string path = this.CreateArchive();
        byte[] originalHash = Hash(path);
        var builder = BotHost.CreateBuilder(Array.Empty<string>());
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SoulLink:PersistencePath"] = path,
            ["SoulLink:DatabasePath"] = Path.Combine(this.directory, "test.sqlite"),
        });
        using var host = builder.Build();
        var archive = host.Services.GetRequiredService<IRunStore>();
        Assert.IsType<ReadOnlyRunStore>(archive);
        Assert.Throws<InvalidOperationException>(() => archive.Save());
        Assert.Throws<InvalidOperationException>(() => host.Services.GetRequiredService<IRunService>().EndRun("guild", "Blocked"));
        var engine = host.Services.GetRequiredService<AutomationEngine>();
        var lobby = engine.Initialize("guild", 0, "Independent test");
        Assert.Equal(lobby.Id, Assert.Single(engine.Runs("guild")).Id);
        Assert.True(File.Exists(Path.Combine(this.directory, "test.sqlite")));
        Assert.Equal(originalHash, Hash(path));
    }

    [Fact]
    public void MisconfiguredDatabaseCannotOpenOrCreateJsonArchives()
    {
        string path = this.CreateArchive();
        byte[] original = Hash(path);
        Assert.Throws<InvalidDataException>(() => new AutomationStore(path));
        Assert.Throws<InvalidDataException>(() => new AutomationStore(path + ".bak"));
        string missing = Path.Combine(this.directory, "new-runs.json");
        Assert.Throws<InvalidDataException>(() => new AutomationStore(missing));
        Assert.False(File.Exists(missing));
        string disguisedJson = Path.Combine(this.directory, "wrong.sqlite");
        File.Copy(path, disguisedJson);
        Assert.Throws<InvalidDataException>(() => new AutomationStore(disguisedJson));
        Assert.Equal(original, Hash(disguisedJson));
        Assert.Equal(original, Hash(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(this.directory)) Directory.Delete(this.directory, true);
    }

    private static byte[] Hash(string path) => SHA256.HashData(File.ReadAllBytes(path));

    private string CreateArchive()
    {
        Directory.CreateDirectory(this.directory);
        string path = Path.Combine(this.directory, "runs.json");
        var run = new SoulLinkRun
        {
            Id = Guid.NewGuid(),
            GuildId = "guild",
            Name = "Historical run",
            Game = "ruby",
            Players = new List<RunPlayer> { new() { UserId = 1, UserName = "Original player" } },
        };
        string json = JsonSerializer.Serialize(new[] { run });
        File.WriteAllText(path, json);
        File.WriteAllText(path + ".bak", json);
        File.SetLastWriteTimeUtc(path, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(path + ".bak", new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc));
        return path;
    }
}
