using System.Text.Json;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Core.Models;
using PokeSoulLinkBot.Infrastructure.Persistence.Migrations;

namespace PokeSoulLinkBot.Infrastructure.Persistence;

/// <summary>
/// Provides a JSON-based implementation of <see cref="IRunStore"/>.
/// </summary>
public sealed class RunStore : IRunStore
{
    private const string BackupFileSuffix = ".bak";

    private readonly string filePath;
    private readonly string backupFilePath;
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly RunStoreMigrationPipeline migrationPipeline;
    private readonly object stateLock = new object();
    private readonly object fileLock = new object();
    private readonly List<SoulLinkRun> runs;
    private long nextSaveVersion;
    private long persistedSaveVersion;
    private bool canBackupPrimaryFile;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunStore"/> class.
    /// </summary>
    /// <param name="filePath">The file path used to persist run data.</param>
    public RunStore(string filePath)
    {
        this.filePath = filePath;
        this.backupFilePath = filePath + BackupFileSuffix;
        this.jsonSerializerOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true,
        };
        this.migrationPipeline = new RunStoreMigrationPipeline();

        this.runs = this.LoadRuns();
    }

    /// <inheritdoc />
    public SoulLinkRun? GetActiveRun(string guildId)
    {
        lock (this.stateLock)
        {
            return this.runs.LastOrDefault(run => run.GuildId == guildId && run.EndedAtUtc is null);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<SoulLinkRun> GetRunsForGuild(string guildId)
    {
        lock (this.stateLock)
        {
            return this.runs
                .Where(run => run.GuildId == guildId)
                .OrderByDescending(run => run.StartedAtUtc)
                .ToList();
        }
    }

    /// <inheritdoc />
    public void AddRun(SoulLinkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        RunStoreSnapshot snapshot;
        lock (this.stateLock)
        {
            this.runs.Add(run);
            snapshot = this.CreateSnapshotCore();
        }

        this.SaveSnapshot(snapshot);
    }

    /// <inheritdoc />
    public void Save()
    {
        RunStoreSnapshot snapshot = this.CreateSnapshot();
        this.SaveSnapshot(snapshot);
    }

    private RunStoreSnapshot CreateSnapshot()
    {
        lock (this.stateLock)
        {
            return this.CreateSnapshotCore();
        }
    }

    private RunStoreSnapshot CreateSnapshotCore()
    {
        var document = new RunStoreDocument
        {
            SchemaVersion = RunStoreMigrationPipeline.CurrentSchemaVersion,
            Runs = this.runs,
        };
        string json = JsonSerializer.Serialize(document, this.jsonSerializerOptions);
        return new RunStoreSnapshot(++this.nextSaveVersion, json);
    }

    private void SaveSnapshot(RunStoreSnapshot snapshot)
    {
        lock (this.fileLock)
        {
            if (snapshot.Version < this.persistedSaveVersion)
            {
                return;
            }

            string directoryPath = this.EnsureDirectory();
            string tempFilePath = this.CreateTempFilePath(directoryPath);

            try
            {
                this.WriteAllText(tempFilePath, snapshot.Json);
                this.ReplacePersistedFile(tempFilePath);
                this.persistedSaveVersion = snapshot.Version;
            }
            finally
            {
                this.DeleteFileIfExists(tempFilePath);
            }
        }
    }

    private string EnsureDirectory()
    {
        string directoryPath = Path.GetDirectoryName(this.filePath) ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(directoryPath) && !Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        return directoryPath;
    }

    private string CreateTempFilePath(string directoryPath)
    {
        string tempFileName = $"{Path.GetFileName(this.filePath)}.{Guid.NewGuid():N}.tmp";

        return string.IsNullOrWhiteSpace(directoryPath)
            ? tempFileName
            : Path.Combine(directoryPath, tempFileName);
    }

    private void ReplacePersistedFile(string tempFilePath)
    {
        if (File.Exists(this.filePath))
        {
            if (this.canBackupPrimaryFile)
            {
                File.Copy(this.filePath, this.backupFilePath, overwrite: true);
            }

            File.Move(tempFilePath, this.filePath, overwrite: true);
            this.canBackupPrimaryFile = true;
            return;
        }

        File.Move(tempFilePath, this.filePath);
        this.canBackupPrimaryFile = true;
    }

    private List<SoulLinkRun> LoadRuns()
    {
        if (this.TryLoadRuns(this.filePath, out List<SoulLinkRun> persistedRuns))
        {
            this.canBackupPrimaryFile = true;
            return persistedRuns;
        }

        if (this.TryLoadRuns(this.backupFilePath, out List<SoulLinkRun> backupRuns))
        {
            this.canBackupPrimaryFile = false;
            return backupRuns;
        }

        this.canBackupPrimaryFile = false;
        return new List<SoulLinkRun>();
    }

    private bool TryLoadRuns(string path, out List<SoulLinkRun> persistedRuns)
    {
        persistedRuns = new List<SoulLinkRun>();

        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json))
            {
                return false;
            }

            RunStoreDocument document = this.DeserializeDocument(json);
            RunStoreDocument migratedDocument = this.migrationPipeline.Migrate(document);
            persistedRuns = migratedDocument.Runs!;
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
    }

    private RunStoreDocument DeserializeDocument(string json)
    {
        using JsonDocument parsedDocument = JsonDocument.Parse(json);

        return parsedDocument.RootElement.ValueKind switch
        {
            JsonValueKind.Array => new RunStoreDocument
            {
                SchemaVersion = 0,
                Runs = JsonSerializer.Deserialize<List<SoulLinkRun>>(json, this.jsonSerializerOptions),
            },
            JsonValueKind.Object => JsonSerializer.Deserialize<RunStoreDocument>(
                json,
                this.jsonSerializerOptions) ?? throw new JsonException("The run-store document is empty."),
            _ => throw new JsonException("The run-store root must be an object or an array."),
        };
    }

    private void WriteAllText(string path, string content)
    {
        using var stream = new FileStream(
            path,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.WriteThrough);
        using var writer = new StreamWriter(stream);
        writer.Write(content);
        writer.Flush();
        stream.Flush(flushToDisk: true);
    }

    private void DeleteFileIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private readonly struct RunStoreSnapshot
    {
        public RunStoreSnapshot(long version, string json)
        {
            this.Version = version;
            this.Json = json;
        }

        public long Version { get; }

        public string Json { get; }
    }
}
