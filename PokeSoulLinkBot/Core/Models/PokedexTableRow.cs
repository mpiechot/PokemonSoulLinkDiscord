namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Represents a single row in the Pokédex evolution table.
/// </summary>
public sealed class PokedexTableRow
{
    /// <summary>
    /// Gets or sets the Pokémon name.
    /// </summary>
    public string PokemonName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the evolution requirement text.
    /// </summary>
    public string RequirementText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Pokémon types.
    /// </summary>
    public IReadOnlyList<string> Types { get; set; } = new List<string>();
}
