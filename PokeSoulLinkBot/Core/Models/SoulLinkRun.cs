namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Represents a Soul Link run for a specific Discord guild.
/// </summary>
public sealed class SoulLinkRun
{
    /// <summary>
    /// Gets or sets the unique identifier of the run.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the Discord guild identifier.
    /// </summary>
    public string GuildId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the display name of the run.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the name of the game for this run.
    /// </summary>
    public string Game { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC date and time when the run started.
    /// </summary>
    public DateTime StartedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the run ended.
    /// </summary>
    public DateTime? EndedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the reason why the run ended.
    /// </summary>
    public string? EndReason { get; set; }

    /// <summary>
    /// Gets or sets the participating players.
    /// </summary>
    public List<RunPlayer> Players { get; set; } = new();

    /// <summary>
    /// Gets or sets the linked Pokémon groups for this run.
    /// </summary>
    public List<LinkGroup> LinkGroups { get; set; } = new();

    /// <summary>
    /// Gets or sets the collection of active link groups.
    /// </summary>
    public LinkGroup[] activeLinks { get; } = new LinkGroup[6];

    public void TryAddToActive(LinkGroup linkGroup)
    {
        for (var i = 0; i < activeLinks.Length; i++)
        {
            if (activeLinks[i] == null)
            {
                activeLinks[i] = linkGroup;
                return;
            }
        }
    }
}