namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Describes whether a Pokémon may still be caught in an active run.
/// </summary>
public sealed class CatchCheckResult
{
    /// <summary>
    /// Gets or sets the requested Pokémon name.
    /// </summary>
    public string RequestedPokemonName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the Pokémon may be caught.
    /// </summary>
    public bool IsAllowed { get; set; }

    /// <summary>
    /// Gets or sets the existing catch that blocks this Pokémon, if any.
    /// </summary>
    public CatchCheckMatch? Match { get; set; }
}
