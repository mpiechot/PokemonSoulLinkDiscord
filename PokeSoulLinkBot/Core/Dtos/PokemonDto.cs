using System.Text.Json.Serialization;

namespace PokeSoulLinkBot.Core.Dtos;

/// <summary>
/// Represents the Pokémon response from the PokéAPI.
/// </summary>
public sealed class PokemonDto
{
    /// <summary>
    /// Gets or sets the Pokémon name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the sprites information.
    /// </summary>
    [JsonPropertyName("sprites")]
    public PokemonSpritesDto? Sprites { get; set; }

    [JsonPropertyName("types")]
    public List<PokemonTypeEntryDto>? Types { get; set; }
}
