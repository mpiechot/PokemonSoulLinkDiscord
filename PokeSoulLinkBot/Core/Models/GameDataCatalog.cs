namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Represents the cached game data used for Discord autocomplete.
/// </summary>
public sealed class GameDataCatalog
{
    /// <summary>
    /// Gets or sets the UTC date and time when the catalog was refreshed.
    /// </summary>
    public DateTime RefreshedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets known Pokemon editions.
    /// </summary>
    public List<GameEditionInfo> Editions { get; set; } = new ();
}
