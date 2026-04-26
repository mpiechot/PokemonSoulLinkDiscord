namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Represents an existing catch that blocks a new Pokémon catch.
/// </summary>
public sealed class CatchCheckMatch
{
    /// <summary>
    /// Gets or sets the route where the matching Pokémon was caught.
    /// </summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the player who caught the matching Pokémon.
    /// </summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the matching Pokémon name.
    /// </summary>
    public string PokemonName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current status of the matching Pokémon.
    /// </summary>
    public string Status { get; set; } = string.Empty;
}
