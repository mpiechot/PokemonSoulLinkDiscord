namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Represents a compact diagnostic summary of the active run.
/// </summary>
public sealed class ActiveRunHealthStatus
{
    /// <summary>
    /// Gets or sets the run name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the game edition.
    /// </summary>
    public string Game { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of players in the run.
    /// </summary>
    public int PlayerCount { get; set; }

    /// <summary>
    /// Gets or sets the number of recorded link groups.
    /// </summary>
    public int LinkGroupCount { get; set; }

    /// <summary>
    /// Gets or sets the number of active team slots.
    /// </summary>
    public int ActiveTeamCount { get; set; }

    /// <summary>
    /// Gets or sets the number of dead link groups.
    /// </summary>
    public int DeadGroupCount { get; set; }

    /// <summary>
    /// Gets or sets the number of routes lost without a catch.
    /// </summary>
    public int LostRouteCount { get; set; }
}
