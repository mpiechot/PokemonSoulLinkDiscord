using System.Text.Json.Serialization;

namespace PokeSoulLinkBot.Core.Dtos;

/// <summary>
/// Represents the sprite information of a Pokémon.
/// </summary>
public sealed class PokemonSpritesDto
{
    /// <summary>
    /// Gets or sets the other sprite collection.
    /// </summary>
    [JsonPropertyName("other")]
    public PokemonOtherSpritesDto? Other { get; set; }
}
