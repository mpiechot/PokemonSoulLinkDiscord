namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Represents an arena that has been completed in a Soul Link run.
/// </summary>
public sealed class CompletedArena
{
    /// <summary>
    /// Gets or sets the arena number.
    /// </summary>
    public int ArenaNumber { get; set; }

    /// <summary>
    /// Gets or sets the game edition used for the arena data.
    /// </summary>
    public string Edition { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the arena leader name.
    /// </summary>
    public string LeaderName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the arena location.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC date and time when the arena was completed.
    /// </summary>
    public DateTime CompletedAtUtc { get; set; }
}
