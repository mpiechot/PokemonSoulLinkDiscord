using System.Text.Json.Serialization;

namespace PokeSoulLinkBot.Core.Dtos;

/// <summary>
/// Represents the official artwork information of a Pokémon.
/// </summary>
public sealed class PokemonOfficialArtworkDto
{
    /// <summary>
    /// Gets or sets the front default image URL.
    /// </summary>
    [JsonPropertyName("front_default")]
    public string? FrontDefault { get; set; }
}