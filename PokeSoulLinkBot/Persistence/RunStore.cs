using System.Text.Json;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Infrastructure.Persistence;

/// <summary>
/// Provides a JSON-based implementation of <see cref="IRunStore"/>.
/// </summary>
public sealed class RunStore : IRunStore
{
    private readonly string filePath;
    private readonly JsonSerializerOptions jsonSerializerOptions;
    private readonly List<SoulLinkRun> runs;

    /// <summary>
    /// Initializes a new instance of the <see cref="RunStore"/> class.
    /// </summary>
    /// <param name="filePath">The file path used to persist run data.</param>
    public RunStore(string filePath)
    {
        this.filePath = filePath;
        this.jsonSerializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        this.runs = this.LoadRuns();
    }

    /// <inheritdoc />
    public SoulLinkRun? GetActiveRun(string guildId)
    {
        return this.runs.LastOrDefault(run => run.GuildId == guildId && run.EndedAtUtc is null);
    }

    /// <inheritdoc />
    public IReadOnlyList<SoulLinkRun> GetRunsForGuild(string guildId)
    {
        return this.runs
            .Where(run => run.GuildId == guildId)
            .OrderByDescending(run => run.StartedAtUtc)
            .ToList();
    }

    /// <inheritdoc />
    public void AddRun(SoulLinkRun run)
    {
        this.runs.Add(run);
        this.Save();
    }

    /// <inheritdoc />
    public void Save()
    {
        string directoryPath = Path.GetDirectoryName(this.filePath) ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(directoryPath) && !Directory.Exists(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        string json = JsonSerializer.Serialize(this.runs, this.jsonSerializerOptions);
        File.WriteAllText(this.filePath, json);
    }

    private List<SoulLinkRun> LoadRuns()
    {
        if (!File.Exists(this.filePath))
        {
            return new List<SoulLinkRun>();
        }

        string json = File.ReadAllText(this.filePath);

        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<SoulLinkRun>();
        }

        return JsonSerializer.Deserialize<List<SoulLinkRun>>(json) ?? new List<SoulLinkRun>();
    }
}
