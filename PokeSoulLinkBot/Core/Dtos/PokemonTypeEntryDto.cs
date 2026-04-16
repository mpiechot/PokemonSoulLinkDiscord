using System.Text.Json.Serialization;

namespace PokeSoulLinkBot.Core.Dtos;

public sealed class PokemonTypeEntryDto
{
    [JsonPropertyName("slot")]
    public int Slot { get; set; }

    [JsonPropertyName("type")]
    public NamedApiResourceDto? Type { get; set; }
}
