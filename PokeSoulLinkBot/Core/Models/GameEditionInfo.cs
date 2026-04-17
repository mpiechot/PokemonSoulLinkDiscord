namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Represents cached game data for one Pokemon edition.
/// </summary>
public sealed class GameEditionInfo
{
    /// <summary>
    /// Gets or sets the PokéAPI resource name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name shown to users.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets routes and locations where encounters can be registered.
    /// </summary>
    public List<string> Routes { get; set; } = new ();
}
