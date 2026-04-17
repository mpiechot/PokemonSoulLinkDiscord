namespace PokeSoulLinkBot.Core.Dtos;

/// <summary>
/// Represents encounter data for a specific game version.
/// </summary>
public sealed class LocationAreaVersionEncounterDto
{
    /// <summary>
    /// Gets or sets the game version.
    /// </summary>
    public NamedApiResourceDto Version { get; set; } = new ();
}
