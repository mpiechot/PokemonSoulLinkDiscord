namespace PokeSoulLinkBot.Application.Interfaces;

/// <summary>
/// Resolves user-provided Pokémon names to PokéAPI resource names.
/// </summary>
public interface IPokemonNameResolver
{
    /// <summary>
    /// Resolves a Pokémon name to the resource name used by the PokéAPI.
    /// </summary>
    /// <param name="pokemonName">The user-provided Pokémon name.</param>
    /// <returns>The resolved PokéAPI resource name.</returns>
    Task<string> ResolvePokemonNameAsync(string pokemonName);
}
