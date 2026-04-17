using System.Text.Json.Serialization;

namespace PokeSoulLinkBot.Core.Dtos;

/// <summary>
/// Represents one Pokemon encounter entry in a location area.
/// </summary>
public sealed class LocationAreaPokemonEncounterDto
{
    /// <summary>
    /// Gets or sets version-specific encounter details.
    /// </summary>
    [JsonPropertyName("version_details")]
    public List<LocationAreaVersionEncounterDto> VersionDetails { get; set; } = new ();
}
