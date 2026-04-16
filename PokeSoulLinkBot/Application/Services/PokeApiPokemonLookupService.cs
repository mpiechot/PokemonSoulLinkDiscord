using System.Text.Json;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Core.Dtos;
using PokeSoulLinkBot.Core.Models;

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
            Console.WriteLine($"Pokémon name lookup failed for '{pokemonName}': {exception.Message}");
            return null;
        }

        var requestUri = $"pokemon/{Uri.EscapeDataString(resolvedName)}";
        PokemonDto? dto;

        try
        {
            using var response = await this.httpClient.GetAsync(requestUri);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Pokémon image lookup failed for '{pokemonName}' using '{resolvedName}': {response.StatusCode}");
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync();
            dto = await JsonSerializer.DeserializeAsync<PokemonDto>(stream, JsonSerializerOptions);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            Console.WriteLine($"Pokémon image lookup failed for '{pokemonName}' using '{resolvedName}': {exception.Message}");
            return null;
        }

        if (dto == null)
        {
            Console.WriteLine($"Pokémon image lookup returned no data for '{pokemonName}' using '{resolvedName}'.");
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
            Console.WriteLine($"Pokémon image lookup found no image for '{pokemonName}' using '{resolvedName}'.");
        }

        return new PokemonInfo
        {
            ImageUrl = imageUrl,
            Types = types,
        };
    }
}
