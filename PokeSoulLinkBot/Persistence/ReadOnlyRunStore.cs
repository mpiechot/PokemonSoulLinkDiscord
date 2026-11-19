using System.Text.Json;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Infrastructure.Persistence;

/// <summary>
/// Exposes detached legacy run snapshots without any JSON persistence capability.
/// </summary>
public sealed class ReadOnlyRunStore : IRunStore
{
    private const string WriteBlockedMessage =
        "Die bestehende runs.json ist im Automations-Test schreibgeschützt. " +
        "Starte einen neuen Test-Run mit /run-init; dieser wird separat in SQLite gespeichert.";

    private readonly IRunStore archive;

    /// <summary>Initializes a read-only view of the legacy archive.</summary>
    /// <param name="archive">The archive to read without exposing mutable state.</param>
    public ReadOnlyRunStore(IRunStore archive)
    {
        this.archive = archive ?? throw new ArgumentNullException(nameof(archive));
    }

    /// <inheritdoc />
    public SoulLinkRun? GetActiveRun(string guildId)
    {
        var run = this.archive.GetActiveRun(guildId);
        return run is null ? null : Copy(run);
    }

    /// <inheritdoc />
    public IReadOnlyList<SoulLinkRun> GetRunsForGuild(string guildId)
    {
        return this.archive.GetRunsForGuild(guildId).Select(Copy).ToList();
    }

    /// <inheritdoc />
    public void AddRun(SoulLinkRun run)
    {
        throw new InvalidOperationException(WriteBlockedMessage);
    }

    /// <inheritdoc />
    public void Save()
    {
        throw new InvalidOperationException(WriteBlockedMessage);
    }

    private static SoulLinkRun Copy(SoulLinkRun run)
    {
        return JsonSerializer.Deserialize<SoulLinkRun>(JsonSerializer.Serialize(run))
            ?? throw new InvalidOperationException("The archived run snapshot could not be read.");
    }
}
