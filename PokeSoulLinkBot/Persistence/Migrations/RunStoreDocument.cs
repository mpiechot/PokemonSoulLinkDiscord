using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Infrastructure.Persistence.Migrations;

/// <summary>
/// Represents the versioned root document persisted by the run store.
/// </summary>
internal sealed class RunStoreDocument
{
    /// <summary>
    /// Gets or sets the persisted schema version.
    /// </summary>
    public int SchemaVersion { get; set; }

    /// <summary>
    /// Gets or sets the persisted runs.
    /// </summary>
    public List<SoulLinkRun>? Runs { get; set; } = new ();
}
