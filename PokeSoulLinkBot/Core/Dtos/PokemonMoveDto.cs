using System.Text.Json.Serialization;

namespace PokeSoulLinkBot.Core.Dtos;

/// <summary>
/// Represents a move entry in a Pokémon response.
/// </summary>
public sealed class PokemonMoveDto
{
    [JsonPropertyName("move")]
    public NamedApiResourceDto? Move { get; set; }

    [JsonPropertyName("version_group_details")]
    public List<PokemonMoveVersionGroupDetailDto>? VersionGroupDetails { get; set; }
}

/// <summary>
/// Represents how a Pokémon learns a move in a version group.
/// </summary>
public sealed class PokemonMoveVersionGroupDetailDto
{
    [JsonPropertyName("level_learned_at")]
    public int LevelLearnedAt { get; set; }

    [JsonPropertyName("move_learn_method")]
    public NamedApiResourceDto? MoveLearnMethod { get; set; }

    [JsonPropertyName("version_group")]
    public NamedApiResourceDto? VersionGroup { get; set; }
}
