using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Core.Dtos;
using Serilog;

namespace PokeSoulLinkBot.Application.Services;

/// <summary>
/// Resolves localized Pokémon names through the PokéAPI species data.
/// </summary>
public sealed class PokeApiPokemonNameResolver : IPokemonNameResolver
{
    private const int SpeciesRequestBatchSize = 20;

    private static readonly JsonSerializerOptions JsonSerializerOptions =
        new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private readonly SemaphoreSlim indexLock = new SemaphoreSlim(1, 1);
    private readonly HttpClient httpClient;
    private readonly PokemonDataCacheStore? pokemonDataCacheStore;
    private readonly ConcurrentDictionary<string, string> resolvedNameCache =
        new ConcurrentDictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, bool> directLookupMissCache =
        new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyDictionary<string, string>? localizedNameIndex;

    /// <summary>
    /// Initializes a new instance of the <see cref="PokeApiPokemonNameResolver"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="pokemonDataCacheStore">The optional persistent Pokémon data cache.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="httpClient"/> is <see langword="null"/>.
    /// </exception>
    public PokeApiPokemonNameResolver(
        HttpClient httpClient,
        PokemonDataCacheStore? pokemonDataCacheStore = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.pokemonDataCacheStore = pokemonDataCacheStore;
    }

    /// <inheritdoc />
    public async Task<string> ResolvePokemonNameAsync(string pokemonName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemonName);

        var normalizedName = this.NormalizePokemonName(pokemonName);
        var lookupKey = this.CreateLookupKey(pokemonName);
        if (this.resolvedNameCache.TryGetValue(lookupKey, out var cachedName))
        {
            return cachedName;
        }

        var indexedName = await this.TryResolveFromAvailableIndexAsync(lookupKey);
        if (!string.IsNullOrWhiteSpace(indexedName))
        {
            this.resolvedNameCache.TryAdd(lookupKey, indexedName);
            return indexedName;
        }

        var directName = await this.TryResolveDirectPokemonNameAsync(normalizedName);

        if (!string.IsNullOrWhiteSpace(directName))
        {
            this.resolvedNameCache.TryAdd(lookupKey, directName);
            return directName;
        }

        var nameIndex = await this.GetLocalizedNameIndexAsync();
        var resolvedName = nameIndex.TryGetValue(lookupKey, out var nameFromIndex)
            ? nameFromIndex
            : normalizedName;

        this.resolvedNameCache.TryAdd(lookupKey, resolvedName);
        return resolvedName;
    }

    /// <summary>
    /// Warms the localized Pokémon name index.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task WarmUpAsync()
    {
        _ = await this.GetLocalizedNameIndexAsync();
    }

    private void AddSpeciesNames(Dictionary<string, string> index, PokemonSpeciesDto species)
    {
        if (string.IsNullOrWhiteSpace(species.Name))
        {
            return;
        }

        this.AddName(index, species.Name, species.Name);

        foreach (var nameEntry in species.Names ?? new List<LocalizedNameDto>())
        {
            var languageName = nameEntry.Language?.Name;

            if (!string.Equals(languageName, "de", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(languageName, "en", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            this.AddName(index, nameEntry.Name, species.Name);
        }
    }

    private void AddName(Dictionary<string, string> index, string? name, string speciesName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        var lookupKey = this.CreateLookupKey(name);
        index.TryAdd(lookupKey, speciesName);

        if (name.Contains('♀', StringComparison.Ordinal))
        {
            index.TryAdd(lookupKey.Replace("♀", "w", StringComparison.Ordinal), speciesName);
            index.TryAdd(lookupKey.Replace("♀", "f", StringComparison.Ordinal), speciesName);
        }

        if (name.Contains('♂', StringComparison.Ordinal))
        {
            index.TryAdd(lookupKey.Replace("♂", "m", StringComparison.Ordinal), speciesName);
        }
    }

    private string NormalizePokemonName(string pokemonName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemonName);

        return pokemonName.Trim().ToLowerInvariant();
    }

    private string CreateLookupKey(string pokemonName)
    {
        var normalizedName = this.NormalizePokemonName(pokemonName)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal)
            .Replace("'", string.Empty, StringComparison.Ordinal)
            .Replace("’", string.Empty, StringComparison.Ordinal);

        return this.RemoveDiacritics(normalizedName);
    }

    private string RemoveDiacritics(string value)
    {
        var normalizedValue = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalizedValue.Length);

        foreach (var character in normalizedValue)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(character);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private async Task<string?> TryResolveDirectPokemonNameAsync(string pokemonName)
    {
        if (this.directLookupMissCache.ContainsKey(pokemonName))
        {
            return null;
        }

        var requestUri = $"pokemon/{Uri.EscapeDataString(pokemonName)}";

        try
        {
            var pokemon = await this.GetFromApiAsync<PokemonDto>(requestUri);
            if (!string.IsNullOrWhiteSpace(pokemon?.Name))
            {
                return pokemon.Name;
            }

            this.directLookupMissCache.TryAdd(pokemonName, true);
            return null;
        }
        catch (InvalidOperationException exception)
        {
            Log.Debug(exception, "Direct Pokemon name lookup failed for '{PokemonName}'.", pokemonName);
            this.directLookupMissCache.TryAdd(pokemonName, true);
            return null;
        }
    }

    private async Task<string?> TryResolveFromAvailableIndexAsync(string lookupKey)
    {
        if (this.localizedNameIndex is not null)
        {
            return this.localizedNameIndex.TryGetValue(lookupKey, out var indexedName)
                ? indexedName
                : null;
        }

        IReadOnlyDictionary<string, string>? persistedIndex = this.pokemonDataCacheStore is null
            ? null
            : await this.pokemonDataCacheStore.GetNameIndexAsync();
        if (persistedIndex is null || persistedIndex.Count == 0)
        {
            return null;
        }

        Log.Information(
            "Using Pokemon localized name index from persistent cache with {NameCount} names.",
            persistedIndex.Count);
        this.localizedNameIndex = persistedIndex;

        return persistedIndex.TryGetValue(lookupKey, out var persistedName)
            ? persistedName
            : null;
    }

    private async Task<IReadOnlyDictionary<string, string>> GetLocalizedNameIndexAsync()
    {
        if (this.localizedNameIndex is not null)
        {
            return this.localizedNameIndex;
        }

        await this.indexLock.WaitAsync();

        try
        {
            if (this.localizedNameIndex is not null)
            {
                return this.localizedNameIndex;
            }

            IReadOnlyDictionary<string, string>? persistedIndex = this.pokemonDataCacheStore is null
                ? null
                : await this.pokemonDataCacheStore.GetNameIndexAsync();
            if (persistedIndex is not null && persistedIndex.Count > 0)
            {
                Log.Information(
                    "Using Pokemon localized name index from persistent cache with {NameCount} names.",
                    persistedIndex.Count);
                this.localizedNameIndex = persistedIndex;
                return this.localizedNameIndex;
            }

            this.localizedNameIndex = await this.BuildLocalizedNameIndexAsync();
            if (this.pokemonDataCacheStore is not null)
            {
                await this.pokemonDataCacheStore.SaveNameIndexAsync(this.localizedNameIndex);
            }

            return this.localizedNameIndex;
        }
        finally
        {
            this.indexLock.Release();
        }
    }

    private async Task<IReadOnlyDictionary<string, string>> BuildLocalizedNameIndexAsync()
    {
        var speciesList = await this.GetFromApiAsync<NamedApiResourceListDto>("pokemon-species?limit=100000&offset=0")
            ?? throw new InvalidOperationException("Pokémon species list could not be loaded.");

        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var speciesResources = speciesList.Results?
            .Where(resource => !string.IsNullOrWhiteSpace(resource.Name) && !string.IsNullOrWhiteSpace(resource.Url))
            .ToList()
            ?? new List<NamedApiResourceDto>();

        foreach (var speciesBatch in speciesResources.Chunk(SpeciesRequestBatchSize))
        {
            var speciesTasks = speciesBatch.Select(resource => this.GetFromApiAsync<PokemonSpeciesDto>(resource.Url!));
            var speciesItems = await Task.WhenAll(speciesTasks);

            foreach (var species in speciesItems.Where(species => species is not null))
            {
                this.AddSpeciesNames(index, species!);
            }
        }

        return index;
    }

    private async Task<T?> GetFromApiAsync<T>(string requestUri)
    {
        try
        {
            using var response = await HttpRequestHelper.GetAsync(this.httpClient, requestUri);

            if (!response.IsSuccessStatusCode)
            {
                return default;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync();
            return await JsonSerializer.DeserializeAsync<T>(responseStream, JsonSerializerOptions);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            throw new InvalidOperationException($"PokéAPI request '{requestUri}' failed: {exception.Message}", exception);
        }
    }
}
