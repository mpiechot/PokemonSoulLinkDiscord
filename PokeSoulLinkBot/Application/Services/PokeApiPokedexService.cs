using System.Text.Json;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Core.Dtos;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Services;

/// <summary>
/// Provides Pokédex lookup functionality using the PokéAPI.
/// </summary>
public sealed class PokeApiPokedexService : IPokedexService
{
    private static readonly JsonSerializerOptions JsonSerializerOptions =
        new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;
    private readonly IPokemonNameResolver pokemonNameResolver;

    /// <summary>
    /// Initializes a new instance of the <see cref="PokeApiPokedexService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="pokemonNameResolver">The Pokémon name resolver.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when one of the parameters is <see langword="null"/>.
    /// </exception>
    public PokeApiPokedexService(
        HttpClient httpClient,
        IPokemonNameResolver pokemonNameResolver)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.pokemonNameResolver = pokemonNameResolver ?? throw new ArgumentNullException(nameof(pokemonNameResolver));
    }

    /// <inheritdoc />
    public async Task<PokedexEntry> GetPokedexEntryAsync(string pokemonName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemonName);

        var normalizedPokemonName = await this.pokemonNameResolver.ResolvePokemonNameAsync(pokemonName);

        var requestedPokemon = await this.GetPokemonAsync(normalizedPokemonName)
            ?? throw CreatePokemonNotFoundException(pokemonName, normalizedPokemonName);

        var species = await this.GetPokemonSpeciesAsync(normalizedPokemonName)
            ?? throw new InvalidOperationException($"Species data for '{pokemonName}' was not found.");

        var evolutionChainUrl = species.EvolutionChain?.Url
            ?? throw new InvalidOperationException($"Evolution chain for '{pokemonName}' was not found.");

        var evolutionChain = await this.GetEvolutionChainAsync(evolutionChainUrl)
            ?? throw new InvalidOperationException($"Evolution chain for '{pokemonName}' was not found.");

        var rows = new List<PokedexTableRow>();

        if (evolutionChain.Chain is null)
        {
            throw new InvalidOperationException($"Evolution chain for '{pokemonName}' is invalid.");
        }

        await this.AddEvolutionRowsAsync(evolutionChain.Chain, "Basis", rows);

        var imageUrl = requestedPokemon.Sprites?.Other?.OfficialArtwork?.FrontDefault;

        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            Console.WriteLine($"Pokédex image lookup found no image for '{pokemonName}' using '{normalizedPokemonName}'.");
        }

        return new PokedexEntry
        {
            PokemonName = FormatResourceName(requestedPokemon.Name ?? normalizedPokemonName),
            ImageUrl = imageUrl,
            Rows = rows,
        };
    }

    private static IReadOnlyList<string> GetFormattedTypes(PokemonDto pokemon)
    {
        ArgumentNullException.ThrowIfNull(pokemon);

        return pokemon.Types?
            .OrderBy(typeEntry => typeEntry.Slot)
            .Select(typeEntry => typeEntry.Type?.Name)
            .Where(typeName => !string.IsNullOrWhiteSpace(typeName))
            .Select(typeName => FormatResourceName(typeName!))
            .ToList()
            ?? new List<string>();
    }

    private static string FormatEvolutionRequirements(IReadOnlyList<EvolutionDetailDto>? details)
    {
        if (details is null || details.Count == 0)
        {
            return "Basis";
        }

        var formattedDetails = details
            .Select(FormatEvolutionRequirement)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return string.Join(" OR ", formattedDetails);
    }

    private static string FormatEvolutionRequirement(EvolutionDetailDto? detail)
    {
        if (detail is null)
        {
            return "Basis";
        }

        var parts = new List<string>();
        var triggerName = detail.Trigger?.Name;

        if (triggerName == "trade")
        {
            parts.Add("Trade");
        }
        else if (triggerName == "use-item")
        {
            if (!string.IsNullOrWhiteSpace(detail.Item?.Name))
            {
                parts.Add($"Use {FormatResourceName(detail.Item.Name)}");
            }
            else
            {
                parts.Add("Use item");
            }
        }
        else if (triggerName == "shed")
        {
            parts.Add("Shed");
        }

        if (detail.MinLevel.HasValue)
        {
            parts.Add($"Level {detail.MinLevel.Value}+");
        }

        if (triggerName != "use-item" && !string.IsNullOrWhiteSpace(detail.Item?.Name))
        {
            parts.Add($"with {FormatResourceName(detail.Item.Name)}");
        }

        if (!string.IsNullOrWhiteSpace(detail.HeldItem?.Name))
        {
            parts.Add($"while holding {FormatResourceName(detail.HeldItem.Name)}");
        }

        if (!string.IsNullOrWhiteSpace(detail.KnownMove?.Name))
        {
            parts.Add($"knowing {FormatResourceName(detail.KnownMove.Name)}");
        }

        if (!string.IsNullOrWhiteSpace(detail.KnownMoveType?.Name))
        {
            parts.Add($"knowing a {FormatResourceName(detail.KnownMoveType.Name)} move");
        }

        if (!string.IsNullOrWhiteSpace(detail.Location?.Name))
        {
            parts.Add($"at {FormatResourceName(detail.Location.Name)}");
        }

        if (detail.MinHappiness.HasValue)
        {
            parts.Add($"with friendship {detail.MinHappiness.Value}+");
        }

        if (detail.MinBeauty.HasValue)
        {
            parts.Add($"with beauty {detail.MinBeauty.Value}+");
        }

        if (detail.MinAffection.HasValue)
        {
            parts.Add($"with affection {detail.MinAffection.Value}+");
        }

        if (!string.IsNullOrWhiteSpace(detail.TimeOfDay))
        {
            parts.Add($"during {FormatResourceName(detail.TimeOfDay)}");
        }

        if (detail.NeedsOverworldRain)
        {
            parts.Add("while raining");
        }

        if (!string.IsNullOrWhiteSpace(detail.PartySpecies?.Name))
        {
            parts.Add($"with {FormatResourceName(detail.PartySpecies.Name)} in party");
        }

        if (!string.IsNullOrWhiteSpace(detail.PartyType?.Name))
        {
            parts.Add($"with a {FormatResourceName(detail.PartyType.Name)} type in party");
        }

        if (detail.Gender.HasValue)
        {
            parts.Add(detail.Gender.Value switch
            {
                1 => "female only",
                2 => "male only",
                _ => "specific gender",
            });
        }

        if (detail.RelativePhysicalStats.HasValue)
        {
            parts.Add(detail.RelativePhysicalStats.Value switch
            {
                -1 => "with Attack < Defense",
                0 => "with Attack = Defense",
                1 => "with Attack > Defense",
                _ => "with specific stats",
            });
        }

        if (!string.IsNullOrWhiteSpace(detail.TradeSpecies?.Name))
        {
            parts.Add($"for {FormatResourceName(detail.TradeSpecies.Name)}");
        }

        if (detail.TurnUpsideDown)
        {
            parts.Add("while device is upside down");
        }

        if (parts.Count == 0)
        {
            if (string.Equals(triggerName, "level-up", StringComparison.OrdinalIgnoreCase))
            {
                return "Level up";
            }

            if (!string.IsNullOrWhiteSpace(triggerName))
            {
                return FormatResourceName(triggerName);
            }

            return "Special condition";
        }

        return string.Join(", ", parts);
    }

    private static string NormalizePokemonName(string pokemonName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemonName);

        return pokemonName.Trim().ToLowerInvariant().Replace(' ', '-');
    }

    private static InvalidOperationException CreatePokemonNotFoundException(
        string requestedPokemonName,
        string normalizedPokemonName)
    {
        if (string.Equals(requestedPokemonName, normalizedPokemonName, StringComparison.OrdinalIgnoreCase))
        {
            return new InvalidOperationException(
                $"Pokémon '{requestedPokemonName}' wurde nicht gefunden. Prüfe Schreibweise oder Namenszuordnung.");
        }

        return new InvalidOperationException(
            $"Pokémon '{requestedPokemonName}' wurde als '{normalizedPokemonName}' gesucht, aber nicht gefunden.");
    }

    private static string FormatResourceName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return string.Join(
            ' ',
            value.Split('-', StringSplitOptions.RemoveEmptyEntries)
                .Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    private async Task AddEvolutionRowsAsync(
        EvolutionChainLinkDto chainLink,
        string requirementText,
        List<PokedexTableRow> rows)
    {
        ArgumentNullException.ThrowIfNull(chainLink);
        ArgumentNullException.ThrowIfNull(rows);

        var pokemonName = chainLink.Species?.Name
            ?? throw new InvalidOperationException("Evolution chain contains an invalid species entry.");

        var pokemon = await this.GetPokemonAsync(pokemonName)
            ?? throw new InvalidOperationException($"Pokémon '{pokemonName}' could not be loaded.");

        rows.Add(new PokedexTableRow
        {
            PokemonName = FormatResourceName(pokemonName),
            RequirementText = requirementText,
            Types = GetFormattedTypes(pokemon),
        });

        if (chainLink.EvolvesTo is null || chainLink.EvolvesTo.Count == 0)
        {
            return;
        }

        foreach (var nextEvolution in chainLink.EvolvesTo)
        {
            var nextRequirement = FormatEvolutionRequirements(nextEvolution.EvolutionDetails);
            await this.AddEvolutionRowsAsync(nextEvolution, nextRequirement, rows);
        }
    }

    private async Task<PokemonDto?> GetPokemonAsync(string pokemonName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemonName);

        var requestUri = $"pokemon/{Uri.EscapeDataString(NormalizePokemonName(pokemonName))}";
        return await this.GetFromApiAsync<PokemonDto>(requestUri);
    }

    private async Task<PokemonSpeciesDto?> GetPokemonSpeciesAsync(string pokemonName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemonName);

        var requestUri = $"pokemon-species/{Uri.EscapeDataString(NormalizePokemonName(pokemonName))}";
        return await this.GetFromApiAsync<PokemonSpeciesDto>(requestUri);
    }

    private async Task<EvolutionChainDto?> GetEvolutionChainAsync(string requestUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);

        return await this.GetFromApiAsync<EvolutionChainDto>(requestUri);
    }

    private async Task<T?> GetFromApiAsync<T>(string requestUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);

        try
        {
            using var response = await this.httpClient.GetAsync(requestUri);

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
