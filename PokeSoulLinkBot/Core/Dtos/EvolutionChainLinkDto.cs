using System.Text.Json.Serialization;

namespace PokeSoulLinkBot.Core.Dtos;

/// <summary>
/// Represents a single node in an evolution chain.
/// </summary>
public sealed class EvolutionChainLinkDto
{
    /// <summary>
    /// Gets or sets the species resource.
    /// </summary>
    [JsonPropertyName("species")]
    public NamedApiResourceDto? Species { get; set; }

    /// <summary>
    /// Gets or sets the evolution details for this node.
    /// </summary>
    [JsonPropertyName("evolution_details")]
    public List<EvolutionDetailDto>? EvolutionDetails { get; set; }

    /// <summary>
    /// Gets or sets the next evolution nodes.
    /// </summary>
    [JsonPropertyName("evolves_to")]
    public List<EvolutionChainLinkDto>? EvolvesTo { get; set; }
}
