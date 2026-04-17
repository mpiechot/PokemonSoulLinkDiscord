// <copyright file="IArenaInfoService.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Interfaces;

/// <summary>
/// Provides arena information for Pokemon game editions.
/// </summary>
public interface IArenaInfoService
{
    /// <summary>
    /// Gets arena information for the provided edition and arena number.
    /// </summary>
    /// <param name="edition">The requested edition.</param>
    /// <param name="arenaNumber">The requested arena number.</param>
    /// <returns>The matching arena information.</returns>
    Task<ArenaInfo> GetArenaInfoAsync(string edition, int arenaNumber);
}
