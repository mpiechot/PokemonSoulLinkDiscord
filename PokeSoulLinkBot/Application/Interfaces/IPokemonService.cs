using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Interfaces;

/// <summary>
/// Provides Pokémon lookup functionality.
/// </summary>
public interface IPokemonService
{
    /// <summary>
    /// Gets Pokédex information for the specified Pokémon name.
    /// </summary>
    /// <param name="pokemonName">The Pokémon name.</param>
    /// <returns>The Pokédex information.</returns>
    Task<PokedexEntry> GetPokedexEntryAsync(string pokemonName);
}
