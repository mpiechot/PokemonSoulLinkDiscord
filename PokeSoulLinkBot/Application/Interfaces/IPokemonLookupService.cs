using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Interfaces;

/// <summary>
/// Provides Pokémon image lookup functionality.
/// </summary>
public interface IPokemonLookupService
{
    /// <summary>
    /// Gets the image URL for the specified Pokémon.
    /// </summary>
    /// <param name="pokemonName">The Pokémon name.</param>
    /// <returns>The image URL if found; otherwise, <see langword="null"/>.</returns>
    Task<PokemonInfo?> GetPokemonInfoAsync(string pokemonName);
}