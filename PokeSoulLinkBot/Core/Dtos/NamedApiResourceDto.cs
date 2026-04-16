using System.Text.Json.Serialization;

namespace PokeSoulLinkBot.Core.Dtos;

/// <summary>
/// Represents a named API resource.
/// </summary>
public sealed class NamedApiResourceDto
{
    /// <summary>
    /// Gets or sets the resource name.
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets the resource URL.
    /// </summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}