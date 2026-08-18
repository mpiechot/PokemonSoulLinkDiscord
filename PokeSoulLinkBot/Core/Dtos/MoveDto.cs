using System.Text.Json.Serialization;

namespace PokeSoulLinkBot.Core.Dtos;

/// <summary>
/// Represents move data from the PokéAPI.
/// </summary>
public sealed class MoveDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("names")]
    public List<LocalizedNameDto>? Names { get; set; }

    [JsonPropertyName("machines")]
    public List<MoveMachineReferenceDto>? Machines { get; set; }
}

/// <summary>
/// Represents a machine reference for a move.
/// </summary>
public sealed class MoveMachineReferenceDto
{
    [JsonPropertyName("machine")]
    public NamedApiResourceDto? Machine { get; set; }

    [JsonPropertyName("version_group")]
    public NamedApiResourceDto? VersionGroup { get; set; }
}

/// <summary>
/// Represents machine data from the PokéAPI.
/// </summary>
public sealed class MachineDto
{
    [JsonPropertyName("item")]
    public NamedApiResourceDto? Item { get; set; }
}

/// <summary>
/// Represents item data from the PokéAPI.
/// </summary>
public sealed class ItemDto
{
    [JsonPropertyName("names")]
    public List<LocalizedNameDto>? Names { get; set; }
}
