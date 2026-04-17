using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Interfaces;

/// <summary>
/// Provides game edition and route data for command suggestions.
/// </summary>
public interface IGameDataCatalogService
{
    /// <summary>
    /// Loads the catalog from cache or refreshes it from the remote source.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task InitializeAsync();

    /// <summary>
    /// Gets all known game editions.
    /// </summary>
    /// <returns>A read-only collection of game editions.</returns>
    Task<IReadOnlyCollection<GameEditionInfo>> GetEditionsAsync();

    /// <summary>
    /// Gets known encounter routes for a game edition.
    /// </summary>
    /// <param name="edition">The edition name or display name.</param>
    /// <returns>A read-only collection of route names.</returns>
    Task<IReadOnlyCollection<string>> GetRoutesAsync(string edition);
}
