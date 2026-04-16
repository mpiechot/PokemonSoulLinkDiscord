using System.Text.Json.Serialization;

namespace PokeSoulLinkBot.Core.Dtos;

/// <summary>
/// Represents a localized Pokémon name.
/// </summary>
public sealed class LocalizedNameDto
{
    /// <summary>
    /// Gets or sets the localized name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the language of the localized name.
    /// </summary>
    [JsonPropertyName("language")]
    public NamedApiResourceDto? Language { get; set; }
}
