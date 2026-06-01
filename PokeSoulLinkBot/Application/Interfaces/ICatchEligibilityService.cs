using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Interfaces;

/// <summary>
/// Checks whether a Pokémon may still be caught in an active run.
/// </summary>
public interface ICatchEligibilityService
{
    /// <summary>
    /// Checks if a Pokémon or any member of its evolution line already appears in the active run.
    /// </summary>
    /// <param name="guildId">The Discord guild identifier.</param>
    /// <param name="pokemonName">The Pokémon name to check.</param>
    /// <returns>The catch check result.</returns>
    Task<CatchCheckResult> CheckCatchAsync(string guildId, string pokemonName);
}
