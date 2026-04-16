using System.Text.Json.Serialization;

namespace PokeSoulLinkBot.Core.Dtos;

/// <summary>
/// Represents an evolution chain response from the PokéAPI.
/// </summary>
public sealed class EvolutionChainDto
{
    /// <summary>
    /// Gets or sets the root chain node.
    /// </summary>
    [JsonPropertyName("chain")]
    public EvolutionChainLinkDto? Chain { get; set; }
}