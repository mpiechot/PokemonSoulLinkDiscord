// <copyright file="ArenaInfo.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Contains details for a gym battle in a Pokemon game edition.
/// </summary>
public sealed class ArenaInfo
{
    /// <summary>
    /// Gets or sets the requested edition.
    /// </summary>
    public string Edition { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the arena number.
    /// </summary>
    public int ArenaNumber { get; set; }

    /// <summary>
    /// Gets or sets the arena location.
    /// </summary>
    public string Location { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the arena leader name.
    /// </summary>
    public string LeaderName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Pokemon levels used by the arena leader.
    /// </summary>
    public IReadOnlyList<int> Levels { get; set; } = Array.Empty<int>();
}
