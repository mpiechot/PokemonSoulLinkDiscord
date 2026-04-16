namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Represents a Pokémon entry linked to a player within a Soul Link group.
/// </summary>
public sealed class LinkedPokemon
{
    /// <summary>
    /// Gets or sets the Discord user identifier of the owning player.
    /// </summary>
    public ulong PlayerUserId { get; set; }

    /// <summary>
    /// Gets or sets the Discord user name of the owning player.
    /// </summary>
    public string PlayerName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Pokémon name.
    /// </summary>
    public string PokemonName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the Pokémon is alive.
    /// </summary>
    public bool IsAlive { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the Pokémon was registered as caught.
    /// </summary>
    public DateTime CaughtAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the Pokémon died.
    /// </summary>
    public DateTime? DiedAtUtc { get; set; }
}