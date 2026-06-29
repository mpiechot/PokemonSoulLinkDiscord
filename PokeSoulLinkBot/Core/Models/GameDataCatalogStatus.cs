namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Represents the current state of the game-data catalog used for edition and route suggestions.
/// </summary>
public sealed class GameDataCatalogStatus
{
    /// <summary>
    /// Gets or sets a value indicating whether a catalog is currently loaded.
    /// </summary>
    public bool IsReady { get; set; }

    /// <summary>
    /// Gets or sets the source of the currently loaded catalog.
    /// </summary>
    public string Source { get; set; } = "none";

    /// <summary>
    /// Gets or sets a value indicating whether a background refresh is currently running.
    /// </summary>
    public bool IsRefreshRunning { get; set; }

    /// <summary>
    /// Gets or sets the catalog schema version.
    /// </summary>
    public int? SchemaVersion { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the catalog was last refreshed.
    /// </summary>
    public DateTime? RefreshedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the number of known game editions.
    /// </summary>
    public int EditionCount { get; set; }

    /// <summary>
    /// Gets or sets the number of known routes across all editions.
    /// </summary>
    public int RouteCount { get; set; }
}
