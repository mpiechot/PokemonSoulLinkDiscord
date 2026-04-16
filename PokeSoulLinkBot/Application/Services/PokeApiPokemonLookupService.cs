using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Core.Dtos;
using PokeSoulLinkBot.Core.Models;
using System.Text.Json;

namespace PokeSoulLinkBot.Application.Services;

/// <summary>
/// Provides Pokémon lookup functionality using the PokéAPI.
/// </summary>
public sealed class PokeApiPokemonLookupService : IPokemonLookupService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="PokeApiPokemonLookupService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="httpClient"/> is <see langword="null"/>.
    /// </exception>
    public PokeApiPokemonLookupService(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc />
    public async Task<PokemonInfo?> GetPokemonInfoAsync(string pokemonName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemonName);

        var normalizedName = pokemonName.Trim().ToLowerInvariant();
        var requestUri = $"pokemon/{Uri.EscapeDataString(normalizedName)}";

        using var response = await this.httpClient.GetAsync(requestUri);

        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        var dto = await JsonSerializer.DeserializeAsync<PokemonDto>(stream, JsonSerializerOptions);

        if (dto == null)
        {
            return null;
        }

        var types = dto.Types?
            .OrderBy(t => t.Slot)
            .Select(t => t.Type?.Name ?? "unknown")
            .ToList()
            ?? new List<string>();

        return new PokemonInfo
        {
            ImageUrl = dto.Sprites?.Other?.OfficialArtwork?.FrontDefault,
            Types = types
        };
    }
}