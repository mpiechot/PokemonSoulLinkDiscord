using System.Text.Json.Serialization;

namespace PokeSoulLinkBot.Core.Dtos;

/// <summary>
/// Represents the Pokémon species response from the PokéAPI.
/// </summary>
public sealed class PokemonSpeciesDto
{
    /// <summary>
    /// Gets or sets the species resource name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the evolution chain resource.
    /// </summary>
    [JsonPropertyName("evolution_chain")]
    public NamedApiResourceDto? EvolutionChain { get; set; }

    /// <summary>
    /// Gets or sets the localized species names.
    /// </summary>
    [JsonPropertyName("names")]
    public List<LocalizedNameDto>? Names { get; set; }
}
