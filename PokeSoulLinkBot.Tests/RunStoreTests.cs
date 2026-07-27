using System.Text.Json;
using PokeSoulLinkBot.Core.Models;
using PokeSoulLinkBot.Infrastructure.Persistence;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class RunStoreTests
{
    [Fact]
    public void AddRun_ShouldPersistVersionedDocument()
    {
        string filePath = CreateTemporaryRunStorePath();
        try
        {
            var store = new RunStore(filePath);

            store.AddRun(CreateRun("guild-1", "Ruby"));

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(filePath));
            JsonElement root = document.RootElement;
            Assert.Equal(JsonValueKind.Object, root.ValueKind);
            Assert.Equal(1, root.GetProperty("SchemaVersion").GetInt32());
            Assert.Single(root.GetProperty("Runs").EnumerateArray());
        }
        finally
        {
            DeleteTemporaryRunStore(filePath);
        }
    }

    [Fact]
    public void Constructor_ShouldLoadLegacyArrayWithoutSchemaVersion()
    {
        string filePath = CreateTemporaryRunStorePath();
        try
        {
            var legacyRuns = new List<SoulLinkRun>
            {
                CreateRun("guild-1", "Ruby"),
            };
            File.WriteAllText(filePath, JsonSerializer.Serialize(legacyRuns));

            var store = new RunStore(filePath);

            SoulLinkRun? run = store.GetActiveRun("guild-1");
            Assert.NotNull(run);
            Assert.Equal("Ruby", run.Name);

            store.Save();
            using JsonDocument migratedDocument = JsonDocument.Parse(File.ReadAllText(filePath));
            Assert.Equal(1, migratedDocument.RootElement.GetProperty("SchemaVersion").GetInt32());
        }
        finally
        {
            DeleteTemporaryRunStore(filePath);
        }
    }

    [Fact]
    public void Constructor_ShouldLoadCurrentVersionedDocument()
    {
        string filePath = CreateTemporaryRunStorePath();
        try
        {
            var initialStore = new RunStore(filePath);
            initialStore.AddRun(CreateRun("guild-1", "Ruby"));

            var reloadedStore = new RunStore(filePath);

            SoulLinkRun? run = reloadedStore.GetActiveRun("guild-1");
            Assert.NotNull(run);
            Assert.Equal("Ruby", run.Name);
        }
        finally
        {
            DeleteTemporaryRunStore(filePath);
        }
    }

    [Fact]
    public void Constructor_ShouldRejectNewerSchemaWithoutChangingFile()
    {
        string filePath = CreateTemporaryRunStorePath();
        try
        {
            const string newerDocument = """
                {
                  "SchemaVersion": 99,
                  "Runs": []
                }
                """;
            File.WriteAllText(filePath, newerDocument);

            var exception = Assert.Throws<NotSupportedException>(() => new RunStore(filePath));

            Assert.Contains("99", exception.Message, StringComparison.Ordinal);
            Assert.Equal(newerDocument, File.ReadAllText(filePath));
        }
        finally
        {
            DeleteTemporaryRunStore(filePath);
        }
    }

    [Fact]
    public void Constructor_ShouldLoadBackupWhenPrimaryJsonIsCorrupt()
    {
        string filePath = CreateTemporaryRunStorePath();
        try
        {
            var store = new RunStore(filePath);
            store.AddRun(CreateRun("guild-1", "Ruby"));
            store.Save();
            File.WriteAllText(filePath, "{ invalid json");

            var reloadedStore = new RunStore(filePath);
            SoulLinkRun? reloadedRun = reloadedStore.GetActiveRun("guild-1");

            Assert.NotNull(reloadedRun);
            Assert.Equal("Ruby", reloadedRun.Name);

            reloadedStore.Save();
            File.WriteAllText(filePath, "{ invalid json again");

            var recoveredStore = new RunStore(filePath);
            SoulLinkRun? recoveredRun = recoveredStore.GetActiveRun("guild-1");

            Assert.NotNull(recoveredRun);
            Assert.Equal("Ruby", recoveredRun.Name);
        }
        finally
        {
            DeleteTemporaryRunStore(filePath);
        }
    }

    [Fact]
    public void Save_ShouldCreateBackupBeforeReplacingExistingPrimary()
    {
        string filePath = CreateTemporaryRunStorePath();
        try
        {
            var store = new RunStore(filePath);
            store.AddRun(CreateRun("guild-1", "Ruby"));

            string firstJson = File.ReadAllText(filePath);
            store.AddRun(CreateRun("guild-2", "Sapphire"));

            string backupFilePath = filePath + ".bak";
            Assert.True(File.Exists(backupFilePath));
            Assert.Equal(firstJson, File.ReadAllText(backupFilePath));

            var reloadedStore = new RunStore(filePath);
            Assert.NotNull(reloadedStore.GetActiveRun("guild-2"));
        }
        finally
        {
            DeleteTemporaryRunStore(filePath);
        }
    }

    [Fact]
    public void AddRun_ShouldPersistConcurrentAddsWithoutCorruptingJson()
    {
        string filePath = CreateTemporaryRunStorePath();
        try
        {
            var store = new RunStore(filePath);

            Parallel.For(
                0,
                24,
                index => store.AddRun(CreateRun($"guild-{index}", $"Run {index}")));

            var reloadedStore = new RunStore(filePath);
            for (var index = 0; index < 24; index++)
            {
                SoulLinkRun? reloadedRun = reloadedStore.GetActiveRun($"guild-{index}");
                Assert.NotNull(reloadedRun);
                Assert.Equal($"Run {index}", reloadedRun.Name);
            }

            string directoryPath = Path.GetDirectoryName(filePath)!;
            Assert.Empty(Directory.GetFiles(directoryPath, "*.tmp"));
        }
        finally
        {
            DeleteTemporaryRunStore(filePath);
        }
    }

    private static SoulLinkRun CreateRun(string guildId, string name)
    {
        return new SoulLinkRun
        {
            Id = Guid.NewGuid(),
            GuildId = guildId,
            Name = name,
            Game = "ruby",
            StartedAtUtc = DateTime.UtcNow,
            Players = new List<RunPlayer>
            {
                new() { UserId = 1, UserName = "marpie1" },
                new() { UserId = 2, UserName = "bene" },
            },
        };
    }

    private static string CreateTemporaryRunStorePath()
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            "PokeSoulLinkBotTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        return Path.Combine(directoryPath, "runs.json");
    }

    private static void DeleteTemporaryRunStore(string filePath)
    {
        string? directoryPath = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
    }
}
