using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Services;

/// <summary>
/// Provides Pokédex lookup functionality.
/// </summary>
public interface IPokedexService
{
    /// <summary>
    /// Gets Pokédex information for the specified Pokémon.
    /// </summary>
    /// <param name="pokemonName">The Pokémon name.</param>
    /// <returns>The Pokédex entry.</returns>
    Task<PokedexEntry> GetPokedexEntryAsync(string pokemonName);

    /// <summary>
    /// Gets the level-up and TM/HM moves for the specified Pokémon.
    /// </summary>
    /// <param name="pokemonName">The Pokémon name.</param>
    /// <returns>The Pokémon move learnset.</returns>
    Task<PokemonMoveLearnset> GetMoveLearnsetAsync(string pokemonName);
}
