using System.Text.Json;
using PokeSoulLinkBot.Core.Models;
using Serilog;

namespace PokeSoulLinkBot.Application.Services;

/// <summary>
/// Loads and saves locally cached Pokémon data used by PokéAPI-backed services.
/// </summary>
public sealed class PokemonDataCacheStore
{
    private const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string cacheFilePath;
    private readonly SemaphoreSlim cacheLock = new SemaphoreSlim(1, 1);
    private PokemonDataCache? cache;

    /// <summary>
    /// Initializes a new instance of the <see cref="PokemonDataCacheStore"/> class.
    /// </summary>
    /// <param name="cacheFilePath">The local cache file path.</param>
    public PokemonDataCacheStore(string cacheFilePath)
    {
        this.cacheFilePath = cacheFilePath ?? throw new ArgumentNullException(nameof(cacheFilePath));
    }

    /// <summary>
    /// Loads the cache file into memory if it exists.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task InitializeAsync()
    {
        _ = await this.GetCacheAsync();
    }

    /// <summary>
    /// Gets a shareable status summary for the cache.
    /// </summary>
    /// <returns>The cache status.</returns>
    public async Task<PokemonDataCacheStatus> GetStatusAsync()
    {
        _ = await this.GetCacheAsync();
        await this.cacheLock.WaitAsync();
        try
        {
            return new PokemonDataCacheStatus
            {
                IsLoaded = this.cache is not null,
                Version = this.cache!.Version,
                RefreshedAtUtc = this.cache.RefreshedAtUtc,
                NameIndexCount = this.cache.NameIndex.Count,
                PokemonInfoCount = this.cache.PokemonInfos.Count,
                PokedexEntryCount = this.cache.PokedexEntries.Count,
            };
        }
        finally
        {
            this.cacheLock.Release();
        }
    }

    /// <summary>
    /// Gets the cached localized name index.
    /// </summary>
    /// <returns>The cached index, or <see langword="null"/> when no index exists.</returns>
    public async Task<IReadOnlyDictionary<string, string>?> GetNameIndexAsync()
    {
        _ = await this.GetCacheAsync();
        await this.cacheLock.WaitAsync();
        try
        {
            if (this.cache!.NameIndex.Count == 0)
            {
                return null;
            }

            return new Dictionary<string, string>(
                this.cache.NameIndex,
                StringComparer.OrdinalIgnoreCase);
        }
        finally
        {
            this.cacheLock.Release();
        }
    }

    /// <summary>
    /// Saves the localized name index to the persistent cache.
    /// </summary>
    /// <param name="nameIndex">The name index to save.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task SaveNameIndexAsync(IReadOnlyDictionary<string, string> nameIndex)
    {
        ArgumentNullException.ThrowIfNull(nameIndex);

        await this.UpdateCacheAsync(cache =>
        {
            cache.NameIndex = new Dictionary<string, string>(nameIndex, StringComparer.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// Gets cached Pokémon metadata.
    /// </summary>
    /// <param name="pokemonName">The normalized Pokémon name.</param>
    /// <returns>The cached Pokémon metadata, or <see langword="null"/>.</returns>
    public async Task<PokemonInfo?> GetPokemonInfoAsync(string pokemonName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemonName);

        _ = await this.GetCacheAsync();
        await this.cacheLock.WaitAsync();
        try
        {
            return this.cache!.PokemonInfos.TryGetValue(NormalizeKey(pokemonName), out PokemonInfo? cachedInfo)
                ? cachedInfo
                : null;
        }
        finally
        {
            this.cacheLock.Release();
        }
    }

    /// <summary>
    /// Saves Pokémon metadata to the persistent cache.
    /// </summary>
    /// <param name="pokemonName">The normalized Pokémon name.</param>
    /// <param name="pokemonInfo">The Pokémon metadata.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task SavePokemonInfoAsync(string pokemonName, PokemonInfo pokemonInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemonName);
        ArgumentNullException.ThrowIfNull(pokemonInfo);

        await this.UpdateCacheAsync(cache =>
        {
            cache.PokemonInfos[NormalizeKey(pokemonName)] = pokemonInfo;
        });
    }

    /// <summary>
    /// Gets a cached Pokédex entry.
    /// </summary>
    /// <param name="pokemonName">The normalized Pokémon name.</param>
    /// <returns>The cached Pokédex entry, or <see langword="null"/>.</returns>
    public async Task<PokedexEntry?> GetPokedexEntryAsync(string pokemonName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemonName);

        _ = await this.GetCacheAsync();
        await this.cacheLock.WaitAsync();
        try
        {
            return this.cache!.PokedexEntries.TryGetValue(NormalizeKey(pokemonName), out PokedexEntry? cachedEntry)
                ? cachedEntry
                : null;
        }
        finally
        {
            this.cacheLock.Release();
        }
    }

    /// <summary>
    /// Saves a Pokédex entry to the persistent cache.
    /// </summary>
    /// <param name="pokemonName">The normalized Pokémon name.</param>
    /// <param name="pokedexEntry">The Pokédex entry.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task SavePokedexEntryAsync(string pokemonName, PokedexEntry pokedexEntry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemonName);
        ArgumentNullException.ThrowIfNull(pokedexEntry);

        await this.UpdateCacheAsync(cache =>
        {
            cache.PokedexEntries[NormalizeKey(pokemonName)] = pokedexEntry;
        });
    }

    private static string NormalizeKey(string value)
    {
        return value.Trim().ToLowerInvariant();
    }

    private static bool IsCacheIoException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or NotSupportedException;
    }

    private static PokemonDataCache NormalizeCache(PokemonDataCache cache)
    {
        cache.Version = CurrentVersion;
        cache.NameIndex = new Dictionary<string, string>(
            cache.NameIndex ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
        cache.PokemonInfos = new Dictionary<string, PokemonInfo>(
            cache.PokemonInfos ?? new Dictionary<string, PokemonInfo>(),
            StringComparer.OrdinalIgnoreCase);
        cache.PokedexEntries = new Dictionary<string, PokedexEntry>(
            cache.PokedexEntries ?? new Dictionary<string, PokedexEntry>(),
            StringComparer.OrdinalIgnoreCase);

        return cache;
    }

    private async Task<PokemonDataCache> GetCacheAsync()
    {
        if (this.cache is not null)
        {
            return this.cache;
        }

        await this.cacheLock.WaitAsync();
        try
        {
            if (this.cache is not null)
            {
                return this.cache;
            }

            this.cache = await this.LoadCacheAsync() ?? new PokemonDataCache
            {
                Version = CurrentVersion,
                RefreshedAtUtc = DateTime.UtcNow,
            };
            return this.cache;
        }
        finally
        {
            this.cacheLock.Release();
        }
    }

    private async Task<PokemonDataCache?> LoadCacheAsync()
    {
        if (!File.Exists(this.cacheFilePath))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(this.cacheFilePath);
            PokemonDataCache? loadedCache = await JsonSerializer.DeserializeAsync<PokemonDataCache>(stream, JsonOptions);
            return loadedCache is null ? null : NormalizeCache(loadedCache);
        }
        catch (JsonException exception)
        {
            Log.Warning(exception, "Pokemon data cache could not be read from {CacheFilePath}.", this.cacheFilePath);
            return null;
        }
        catch (Exception exception) when (IsCacheIoException(exception))
        {
            Log.Warning(exception, "Pokemon data cache could not be opened from {CacheFilePath}.", this.cacheFilePath);
            return null;
        }
    }

    private async Task UpdateCacheAsync(Action<PokemonDataCache> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        PokemonDataCache currentCache = await this.GetCacheAsync();
        await this.cacheLock.WaitAsync();
        try
        {
            update(currentCache);
            currentCache.Version = CurrentVersion;
            currentCache.RefreshedAtUtc = DateTime.UtcNow;
            await this.SaveCacheAsync(currentCache);
        }
        finally
        {
            this.cacheLock.Release();
        }
    }

    private async Task SaveCacheAsync(PokemonDataCache currentCache)
    {
        string? directoryPath = Path.GetDirectoryName(this.cacheFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        string tempFilePath = this.CreateTempFilePath(directoryPath);

        try
        {
            await using (var stream = File.Create(tempFilePath))
            {
                await JsonSerializer.SerializeAsync(stream, currentCache, JsonOptions);
            }

            File.Move(tempFilePath, this.cacheFilePath, overwrite: true);
        }
        catch (Exception exception) when (IsCacheIoException(exception))
        {
            Log.Warning(exception, "Pokemon data cache could not be saved to {CacheFilePath}.", this.cacheFilePath);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }

    private string CreateTempFilePath(string? directoryPath)
    {
        string fileName = $"{Path.GetFileName(this.cacheFilePath)}.{Guid.NewGuid():N}.tmp";
        return string.IsNullOrWhiteSpace(directoryPath)
            ? fileName
            : Path.Combine(directoryPath, fileName);
    }
}
