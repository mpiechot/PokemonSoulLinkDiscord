namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Represents a linked encounter group for a specific route or area.
/// </summary>
public sealed class LinkGroup
{
    /// <summary>
    /// Gets or sets the unique identifier of the link group.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the route or area name.
    /// </summary>
    public string Route { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the linked Pokémon entries for this group.
    /// </summary>
    public List<LinkedPokemon> Entries { get; set; } = new ();

    /// <summary>
    /// Gets or sets a value indicating whether this route was lost before a catch was registered.
    /// </summary>
    public bool IsLostWithoutEncounter { get; set; }

    /// <summary>
    /// Gets or sets the reason why this route was lost.
    /// </summary>
    public string? LossReason { get; set; }

    /// <summary>
    /// Gets or sets the Discord user identifier of the player who missed the encounter.
    /// </summary>
    public ulong? FailedEncounterPlayerUserId { get; set; }

    /// <summary>
    /// Gets or sets the Discord user name of the player who missed the encounter.
    /// </summary>
    public string? FailedEncounterPlayerName { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when this route was marked as lost.
    /// </summary>
    public DateTime? LostAtUtc { get; set; }

    /// <summary>
    ///     Gets a value indicating whether this link group is still alive (i.e., at least one linked Pokémon is alive).
    /// </summary>
    public bool IsAlive => this.Entries.Any(entry => entry.IsAlive);
}
