namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Represents the current state of the persistent Pokémon data cache.
/// </summary>
public sealed class PokemonDataCacheStatus
{
    /// <summary>
    /// Gets or sets a value indicating whether the cache has been loaded into memory.
    /// </summary>
    public bool IsLoaded { get; set; }

    /// <summary>
    /// Gets or sets the cache schema version.
    /// </summary>
    public int Version { get; set; }

    /// <summary>
    /// Gets or sets the UTC date and time when the cache was last refreshed.
    /// </summary>
    public DateTime RefreshedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets the number of cached localized Pokémon names.
    /// </summary>
    public int NameIndexCount { get; set; }

    /// <summary>
    /// Gets or sets the number of cached Pokémon metadata entries.
    /// </summary>
    public int PokemonInfoCount { get; set; }

    /// <summary>
    /// Gets or sets the number of cached Pokédex entries.
    /// </summary>
    public int PokedexEntryCount { get; set; }
}
