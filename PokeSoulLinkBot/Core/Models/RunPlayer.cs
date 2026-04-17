namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Represents a player participating in a Soul Link run.
/// </summary>
public sealed class RunPlayer
{
    /// <summary>
    /// Gets or sets the Discord user identifier.
    /// </summary>
    public ulong UserId { get; set; }

    /// <summary>
    /// Gets or sets the Discord user name.
    /// </summary>
    public string UserName { get; set; } = string.Empty;
}
