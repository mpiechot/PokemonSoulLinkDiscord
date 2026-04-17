namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Represents Pokédex information for a Pokémon.
/// </summary>
public sealed class PokedexEntry
{
    /// <summary>
    /// Gets or sets the requested Pokémon name.
    /// </summary>
    public string PokemonName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the image URL of the requested Pokémon.
    /// </summary>
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Gets or sets the rows shown in the evolution table.
    /// </summary>
    public List<PokedexTableRow> Rows { get; set; } = new();
}
