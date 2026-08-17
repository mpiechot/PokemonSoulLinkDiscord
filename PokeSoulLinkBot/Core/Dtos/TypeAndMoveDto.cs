using System.Text.Json.Serialization;

namespace PokeSoulLinkBot.Core.Dtos;

public sealed class TypeDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("damage_relations")]
    public TypeDamageRelationsDto? DamageRelations { get; set; }
}

public sealed class TypeDamageRelationsDto
{
    [JsonPropertyName("double_damage_to")]
    public List<NamedApiResourceDto>? DoubleDamageTo { get; set; }

    [JsonPropertyName("half_damage_to")]
    public List<NamedApiResourceDto>? HalfDamageTo { get; set; }

    [JsonPropertyName("no_damage_to")]
    public List<NamedApiResourceDto>? NoDamageTo { get; set; }
}

public sealed class MoveDetailDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("names")]
    public List<LocalizedNameDto>? Names { get; set; }

    [JsonPropertyName("type")]
    public NamedApiResourceDto? Type { get; set; }

    [JsonPropertyName("damage_class")]
    public NamedApiResourceDto? DamageClass { get; set; }

    [JsonPropertyName("power")]
    public int? Power { get; set; }

    [JsonPropertyName("accuracy")]
    public int? Accuracy { get; set; }

    [JsonPropertyName("pp")]
    public int? Pp { get; set; }

    [JsonPropertyName("effect_entries")]
    public List<MoveEffectEntryDto>? EffectEntries { get; set; }
}

public sealed class MoveEffectEntryDto
{
    [JsonPropertyName("effect")]
    public string? Effect { get; set; }

    [JsonPropertyName("language")]
    public NamedApiResourceDto? Language { get; set; }
}
