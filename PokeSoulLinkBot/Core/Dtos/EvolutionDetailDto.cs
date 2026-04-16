using System.Text.Json.Serialization;

namespace PokeSoulLinkBot.Core.Dtos;

/// <summary>
/// Represents a single evolution requirement entry.
/// </summary>
public sealed class EvolutionDetailDto
{
    /// <summary>
    /// Gets or sets the minimum level.
    /// </summary>
    [JsonPropertyName("min_level")]
    public int? MinLevel { get; set; }

    /// <summary>
    /// Gets or sets the required item.
    /// </summary>
    [JsonPropertyName("item")]
    public NamedApiResourceDto? Item { get; set; }

    /// <summary>
    /// Gets or sets the evolution trigger.
    /// </summary>
    [JsonPropertyName("trigger")]
    public NamedApiResourceDto? Trigger { get; set; }

    /// <summary>
    /// Gets or sets the required held item.
    /// </summary>
    [JsonPropertyName("held_item")]
    public NamedApiResourceDto? HeldItem { get; set; }

    /// <summary>
    /// Gets or sets the known move requirement.
    /// </summary>
    [JsonPropertyName("known_move")]
    public NamedApiResourceDto? KnownMove { get; set; }

    /// <summary>
    /// Gets or sets the known move type requirement.
    /// </summary>
    [JsonPropertyName("known_move_type")]
    public NamedApiResourceDto? KnownMoveType { get; set; }

    /// <summary>
    /// Gets or sets the required location.
    /// </summary>
    [JsonPropertyName("location")]
    public NamedApiResourceDto? Location { get; set; }

    /// <summary>
    /// Gets or sets the minimum happiness.
    /// </summary>
    [JsonPropertyName("min_happiness")]
    public int? MinHappiness { get; set; }

    /// <summary>
    /// Gets or sets the minimum beauty.
    /// </summary>
    [JsonPropertyName("min_beauty")]
    public int? MinBeauty { get; set; }

    /// <summary>
    /// Gets or sets the minimum affection.
    /// </summary>
    [JsonPropertyName("min_affection")]
    public int? MinAffection { get; set; }

    /// <summary>
    /// Gets or sets the required gender.
    /// </summary>
    [JsonPropertyName("gender")]
    public int? Gender { get; set; }

    /// <summary>
    /// Gets or sets the required time of day.
    /// </summary>
    [JsonPropertyName("time_of_day")]
    public string? TimeOfDay { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether overworld rain is required.
    /// </summary>
    [JsonPropertyName("needs_overworld_rain")]
    public bool NeedsOverworldRain { get; set; }

    /// <summary>
    /// Gets or sets the required party species.
    /// </summary>
    [JsonPropertyName("party_species")]
    public NamedApiResourceDto? PartySpecies { get; set; }

    /// <summary>
    /// Gets or sets the required party type.
    /// </summary>
    [JsonPropertyName("party_type")]
    public NamedApiResourceDto? PartyType { get; set; }

    /// <summary>
    /// Gets or sets the relative physical stats requirement.
    /// </summary>
    [JsonPropertyName("relative_physical_stats")]
    public int? RelativePhysicalStats { get; set; }

    /// <summary>
    /// Gets or sets the trade species requirement.
    /// </summary>
    [JsonPropertyName("trade_species")]
    public NamedApiResourceDto? TradeSpecies { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the device must be turned upside down.
    /// </summary>
    [JsonPropertyName("turn_upside_down")]
    public bool TurnUpsideDown { get; set; }
}
