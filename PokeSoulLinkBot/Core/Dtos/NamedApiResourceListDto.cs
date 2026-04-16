using System.Text.Json.Serialization;

namespace PokeSoulLinkBot.Core.Dtos;

/// <summary>
/// Represents a paged list of named API resources.
/// </summary>
public sealed class NamedApiResourceListDto
{
    /// <summary>
    /// Gets or sets the listed resources.
    /// </summary>
    [JsonPropertyName("results")]
    public List<NamedApiResourceDto>? Results { get; set; }
}
