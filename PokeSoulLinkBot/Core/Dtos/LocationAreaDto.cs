using System.Text.Json.Serialization;

namespace PokeSoulLinkBot.Core.Dtos;

/// <summary>
/// Represents a PokéAPI location area response.
/// </summary>
public sealed class LocationAreaDto
{
    /// <summary>
    /// Gets or sets the API resource name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets localized names.
    /// </summary>
    public List<LocalizedNameDto> Names { get; set; } = new ();

    /// <summary>
    /// Gets or sets Pokemon encounter data.
    /// </summary>
    [JsonPropertyName("pokemon_encounters")]
    public List<LocationAreaPokemonEncounterDto> PokemonEncounters { get; set; } = new ();
}
