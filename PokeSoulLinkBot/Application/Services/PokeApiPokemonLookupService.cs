using System.Collections.Concurrent;
using System.Text.Json;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Core.Dtos;
using PokeSoulLinkBot.Core.Models;
using Serilog;

namespace PokeSoulLinkBot.Application.Services;

/// <summary>
/// Provides Pokémon lookup functionality using the PokéAPI.
/// </summary>
public sealed class PokeApiPokemonLookupService : IPokemonLookupService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions =
        new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly IPokemonNameResolver pokemonNameResolver;
    private readonly ConcurrentDictionary<string, PokemonInfo> pokemonInfoCache =
        new ConcurrentDictionary<string, PokemonInfo>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="PokeApiPokemonLookupService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="pokemonNameResolver">The Pokémon name resolver.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when one of the parameters is <see langword="null"/>.
    /// </exception>
    public PokeApiPokemonLookupService(
        HttpClient httpClient,
        IPokemonNameResolver pokemonNameResolver)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.pokemonNameResolver = pokemonNameResolver ?? throw new ArgumentNullException(nameof(pokemonNameResolver));
    }

    /// <inheritdoc />
    public async Task<PokemonInfo?> GetPokemonInfoAsync(string pokemonName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemonName);

        string resolvedName;

        try
        {
            resolvedName = await this.pokemonNameResolver.ResolvePokemonNameAsync(pokemonName);
        }
        catch (InvalidOperationException exception)
        {
            Log.Warning(exception, "Pokemon name lookup failed for '{PokemonName}'.", pokemonName);
            return null;
        }

        if (this.pokemonInfoCache.TryGetValue(resolvedName, out var cachedInfo))
        {
            Log.Debug("Using cached Pokemon info for '{PokemonName}' resolved as '{ResolvedName}'.", pokemonName, resolvedName);
            return cachedInfo;
        }

        var requestUri = $"pokemon/{Uri.EscapeDataString(resolvedName)}";
        PokemonDto? dto;

        try
        {
            using var response = await HttpRequestHelper.GetAsync(this.httpClient, requestUri);

            if (!response.IsSuccessStatusCode)
            {
                Log.Warning(
                    "Pokemon lookup failed for '{PokemonName}' resolved as '{ResolvedName}'. StatusCode={StatusCode}.",
                    pokemonName,
                    resolvedName,
                    response.StatusCode);
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            dto = await JsonSerializer.DeserializeAsync<PokemonDto>(stream, JsonSerializerOptions);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            Log.Warning(
                exception,
                "Pokemon lookup failed for '{PokemonName}' resolved as '{ResolvedName}'.",
                pokemonName,
                resolvedName);
            return null;
        }

        if (dto == null)
        {
            Log.Warning(
                "Pokemon lookup returned no data for '{PokemonName}' resolved as '{ResolvedName}'.",
                pokemonName,
                resolvedName);
            return null;
        }

        var types = dto.Types?
            .OrderBy(t => t.Slot)
            .Select(t => t.Type?.Name ?? "unknown")
            .ToList()
            ?? new List<string>();

        var imageUrl = dto.Sprites?.Other?.OfficialArtwork?.FrontDefault;

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            Log.Debug(
                "Pokemon lookup found no image for '{PokemonName}' resolved as '{ResolvedName}'.",
                pokemonName,
                resolvedName);
        }

        var pokemonInfo = new PokemonInfo
        {
            ImageUrl = imageUrl,
            Types = types,
        };

        this.pokemonInfoCache.TryAdd(resolvedName, pokemonInfo);
        return pokemonInfo;
    }
}
