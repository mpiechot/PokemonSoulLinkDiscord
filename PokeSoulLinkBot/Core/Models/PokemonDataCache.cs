namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Represents locally cached Pokémon data that can be reused across bot restarts.
/// </summary>
public sealed class PokemonDataCache
{
    /// <summary>
    /// Gets or sets the cache schema version.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Gets or sets the UTC date and time when the cache was last refreshed.
    /// </summary>
    public DateTime RefreshedAtUtc { get; set; }

    /// <summary>
    /// Gets or sets normalized localized names mapped to PokéAPI species names.
    /// </summary>
    public Dictionary<string, string> NameIndex { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets cached Pokémon metadata by normalized PokéAPI name.
    /// </summary>
    public Dictionary<string, PokemonInfo> PokemonInfos { get; set; } =
        new Dictionary<string, PokemonInfo>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets or sets cached Pokédex entries by normalized PokéAPI name.
    /// </summary>
    public Dictionary<string, PokedexEntry> PokedexEntries { get; set; } =
        new Dictionary<string, PokedexEntry>(StringComparer.OrdinalIgnoreCase);
}
