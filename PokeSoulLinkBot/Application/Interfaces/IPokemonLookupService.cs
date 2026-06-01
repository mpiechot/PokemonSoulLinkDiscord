using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Interfaces;

/// <summary>
/// Provides Pokémon metadata lookup functionality.
/// </summary>
public interface IPokemonLookupService
{
    /// <summary>
    /// Gets metadata for the specified Pokémon.
    /// </summary>
    /// <param name="pokemonName">The Pokémon name.</param>
    /// <returns>The Pokémon metadata if found; otherwise, <see langword="null"/>.</returns>
    Task<PokemonInfo?> GetPokemonInfoAsync(string pokemonName);
}
