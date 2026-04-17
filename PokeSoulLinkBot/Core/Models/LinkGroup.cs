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
    public List<LinkedPokemon> Entries { get; set; } = new();

    /// <summary>
    ///     Gets a value indicating whether this link group is still alive (i.e., at least one linked Pokémon is alive).
    /// </summary>
    public bool IsAlive => this.Entries.Any(entry => entry.IsAlive);
}
