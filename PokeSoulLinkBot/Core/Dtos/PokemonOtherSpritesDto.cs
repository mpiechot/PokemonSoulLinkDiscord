using System.Text.Json.Serialization;

namespace PokeSoulLinkBot.Core.Dtos;

/// <summary>
/// Represents additional Pokémon sprite groups.
/// </summary>
public sealed class PokemonOtherSpritesDto
{
    /// <summary>
    /// Gets or sets the official artwork group.
    /// </summary>
    [JsonPropertyName("official-artwork")]
    public PokemonOfficialArtworkDto? OfficialArtwork { get; set; }
}
