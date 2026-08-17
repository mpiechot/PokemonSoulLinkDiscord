using System.Collections.Concurrent;
using System.Text.Json;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Core.Dtos;
using PokeSoulLinkBot.Core.Models;
using Serilog;

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
    private readonly PokemonDataCacheStore? pokemonDataCacheStore;
    private readonly ConcurrentDictionary<string, PokedexEntry> pokedexEntryCache =
        new ConcurrentDictionary<string, PokedexEntry>(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, PokemonDto> pokemonDtoCache =
        new ConcurrentDictionary<string, PokemonDto>(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, PokemonSpeciesDto> pokemonSpeciesCache =
        new ConcurrentDictionary<string, PokemonSpeciesDto>(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, EvolutionChainDto> evolutionChainCache =
        new ConcurrentDictionary<string, EvolutionChainDto>(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Lazy<Task<PokedexEntry>>> pendingPokedexEntryRequests =
        new ConcurrentDictionary<string, Lazy<Task<PokedexEntry>>>(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Lazy<Task<PokemonDto?>>> pendingPokemonRequests =
        new ConcurrentDictionary<string, Lazy<Task<PokemonDto?>>>(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Lazy<Task<PokemonSpeciesDto?>>> pendingSpeciesRequests =
        new ConcurrentDictionary<string, Lazy<Task<PokemonSpeciesDto?>>>(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, Lazy<Task<EvolutionChainDto?>>> pendingEvolutionChainRequests =
        new ConcurrentDictionary<string, Lazy<Task<EvolutionChainDto?>>>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="PokeApiPokedexService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client.</param>
    /// <param name="pokemonNameResolver">The Pokémon name resolver.</param>
    /// <param name="pokemonDataCacheStore">The optional persistent Pokémon data cache.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when one of the parameters is <see langword="null"/>.
    /// </exception>
    public PokeApiPokedexService(
        HttpClient httpClient,
        IPokemonNameResolver pokemonNameResolver,
        PokemonDataCacheStore? pokemonDataCacheStore = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.pokemonNameResolver = pokemonNameResolver ?? throw new ArgumentNullException(nameof(pokemonNameResolver));
        this.pokemonDataCacheStore = pokemonDataCacheStore;
    }

    /// <inheritdoc />
    public async Task<PokedexEntry> GetPokedexEntryAsync(string pokemonName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemonName);

        var normalizedPokemonName = await this.pokemonNameResolver.ResolvePokemonNameAsync(pokemonName);
        var cacheKey = NormalizePokemonName(normalizedPokemonName);

        if (this.pokedexEntryCache.TryGetValue(cacheKey, out PokedexEntry? cachedEntry))
        {
            Log.Debug(
                "Using cached Pokedex entry for '{PokemonName}' resolved as '{ResolvedPokemonName}'.",
                pokemonName,
                normalizedPokemonName);
            return cachedEntry;
        }

        PokedexEntry? persistedEntry = this.pokemonDataCacheStore is null
            ? null
            : await this.pokemonDataCacheStore.GetPokedexEntryAsync(cacheKey);
        if (persistedEntry is not null)
        {
            Log.Debug(
                "Using persisted Pokedex entry for '{PokemonName}' resolved as '{ResolvedPokemonName}'.",
                pokemonName,
                normalizedPokemonName);
            this.pokedexEntryCache.TryAdd(cacheKey, persistedEntry);
            return persistedEntry;
        }

        var pendingRequest = this.pendingPokedexEntryRequests.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<PokedexEntry>>(
                () => this.CreatePokedexEntryAsync(pokemonName, normalizedPokemonName, cacheKey),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return await pendingRequest.Value;
        }
        finally
        {
            this.pendingPokedexEntryRequests.TryRemove(cacheKey, out _);
        }
    }

#pragma warning disable SA1204

    /// <summary>
    /// Gets the level-up and TM/HM moves for a Pokémon.
    /// </summary>
    /// <param name="pokemonName">The Pokémon name.</param>
    /// <returns>The move learnset.</returns>
    public async Task<PokemonMoveLearnset> GetMoveLearnsetAsync(string pokemonName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemonName);

        var normalizedPokemonName = await this.pokemonNameResolver.ResolvePokemonNameAsync(pokemonName);
        var pokemon = await this.GetPokemonAsync(normalizedPokemonName)
            ?? throw CreatePokemonNotFoundException(pokemonName, normalizedPokemonName);
        var moveEntries = pokemon.Moves ?? new List<PokemonMoveDto>();

        var levelUpCandidates = await Task.WhenAll(moveEntries.Select(async move => new
        {
            Move = move,
            Details = await this.GetFromApiAsync<MoveDto>(move.Move?.Url ?? string.Empty),
        }));
        var levelUpMoves = levelUpCandidates
            .SelectMany(candidate => (candidate.Move.VersionGroupDetails ?? new List<PokemonMoveVersionGroupDetailDto>())
                .Where(detail => string.Equals(detail.MoveLearnMethod?.Name, "level-up", StringComparison.OrdinalIgnoreCase))
                .Select(detail => new
                {
                    MoveName = GetGermanName(candidate.Details?.Names, candidate.Move.Move?.Name),
                    detail.LevelLearnedAt,
                }))
            .Where(move => !string.IsNullOrWhiteSpace(move.MoveName))
            .GroupBy(move => move.MoveName!, StringComparer.OrdinalIgnoreCase)
            .Select(group => new LevelUpMove
            {
                Level = group.Min(move => move.LevelLearnedAt),
                MoveName = FormatResourceName(group.Key),
            })
            .OrderBy(move => move.Level)
            .ThenBy(move => move.MoveName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var machineMoveEntries = moveEntries
            .Where(move => (move.VersionGroupDetails ?? new List<PokemonMoveVersionGroupDetailDto>())
                .Any(detail => string.Equals(detail.MoveLearnMethod?.Name, "machine", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        var machineMoveResults = await Task.WhenAll(machineMoveEntries.Select(this.GetMachineMovesAsync));

        return new PokemonMoveLearnset
        {
            PokemonName = FormatResourceName(pokemon.Name ?? normalizedPokemonName),
            LevelUpMoves = levelUpMoves,
            MachineMoves = machineMoveResults
                .SelectMany(moves => moves)
                .GroupBy(move => $"{move.MachineName}|{move.MoveName}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(move => move.MachineName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(move => move.MoveName, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        };
    }

    private static string GetGermanName(IReadOnlyList<LocalizedNameDto>? names, string? fallback)
    {
        return names?
            .FirstOrDefault(localizedName => string.Equals(
                localizedName.Language?.Name,
                "de",
                StringComparison.OrdinalIgnoreCase))?.Name
            ?? fallback
            ?? string.Empty;
    }

    private static string FormatMachineName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalizedValue = value.Trim();
        return normalizedValue.Length >= 2
            ? normalizedValue[..2].ToUpperInvariant() + normalizedValue[2..]
            : normalizedValue.ToUpperInvariant();
    }

    private async Task<IReadOnlyCollection<MachineMove>> GetMachineMovesAsync(PokemonMoveDto pokemonMove)
    {
        var moveUrl = pokemonMove.Move?.Url;
        if (string.IsNullOrWhiteSpace(moveUrl))
        {
            return Array.Empty<MachineMove>();
        }

        var move = await this.GetFromApiAsync<MoveDto>(moveUrl);
        if (move?.Machines is null)
        {
            return Array.Empty<MachineMove>();
        }

        var supportedVersionGroups = (pokemonMove.VersionGroupDetails ?? new List<PokemonMoveVersionGroupDetailDto>())
            .Where(detail => string.Equals(detail.MoveLearnMethod?.Name, "machine", StringComparison.OrdinalIgnoreCase))
            .Select(detail => detail.VersionGroup?.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var machineReferences = move.Machines
            .Where(machine => machine.Machine?.Url is not null)
            .Where(machine => supportedVersionGroups.Count == 0 ||
                supportedVersionGroups.Contains(machine.VersionGroup?.Name ?? string.Empty))
            .ToList();
        var machineData = await Task.WhenAll(machineReferences.Select(async machineReference =>
            new
            {
                Machine = await this.GetFromApiAsync<MachineDto>(machineReference.Machine!.Url!),
                Move = move,
            }));
        var localizedMachineData = await Task.WhenAll(machineData.Select(async result => new
        {
            result.Machine,
            Item = result.Machine?.Item?.Url is null
                ? null
                : await this.GetFromApiAsync<ItemDto>(result.Machine.Item.Url),
            MoveName = GetGermanName(result.Move.Names, pokemonMove.Move?.Name),
        }));

        return localizedMachineData
            .Where(result => !string.IsNullOrWhiteSpace(result.Machine?.Item?.Name) && !string.IsNullOrWhiteSpace(result.MoveName))
            .Select(result => new MachineMove
            {
                MachineName = string.IsNullOrWhiteSpace(result.Item?.Names?.FirstOrDefault(name =>
                    string.Equals(name.Language?.Name, "de", StringComparison.OrdinalIgnoreCase))?.Name)
                    ? FormatMachineName(result.Machine!.Item!.Name!)
                    : result.Item!.Names!.First(name => string.Equals(
                        name.Language?.Name,
                        "de",
                        StringComparison.OrdinalIgnoreCase)).Name!,
                MoveName = FormatResourceName(result.MoveName!),
            })
            .Where(move => move.MachineName.StartsWith("TM", StringComparison.OrdinalIgnoreCase) ||
                move.MachineName.StartsWith("HM", StringComparison.OrdinalIgnoreCase) ||
                move.MachineName.StartsWith("VM", StringComparison.OrdinalIgnoreCase))
            .ToList();
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

#pragma warning restore SA1204
    private async Task<PokedexEntry> CreatePokedexEntryAsync(
        string pokemonName,
        string normalizedPokemonName,
        string cacheKey)
    {
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
            Log.Debug(
                "Pokedex image lookup found no image for '{PokemonName}' resolved as '{ResolvedPokemonName}'.",
                pokemonName,
                normalizedPokemonName);
        }

        var pokedexEntry = new PokedexEntry
        {
            PokemonName = FormatResourceName(requestedPokemon.Name ?? normalizedPokemonName),
            ImageUrl = imageUrl,
            Rows = rows,
        };

        this.pokedexEntryCache.TryAdd(cacheKey, pokedexEntry);
        if (this.pokemonDataCacheStore is not null)
        {
            await this.pokemonDataCacheStore.SavePokedexEntryAsync(cacheKey, pokedexEntry);
        }

        return pokedexEntry;
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

        var cacheKey = NormalizePokemonName(pokemonName);
        if (this.pokemonDtoCache.TryGetValue(cacheKey, out var cachedPokemon))
        {
            return cachedPokemon;
        }

        var requestUri = $"pokemon/{Uri.EscapeDataString(cacheKey)}";
        var pendingRequest = this.pendingPokemonRequests.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<PokemonDto?>>(
                () => this.GetFromApiAsync<PokemonDto>(requestUri),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var pokemon = await pendingRequest.Value;
            if (pokemon is not null)
            {
                this.pokemonDtoCache.TryAdd(cacheKey, pokemon);
            }

            return pokemon;
        }
        finally
        {
            this.pendingPokemonRequests.TryRemove(cacheKey, out _);
        }
    }

    private async Task<PokemonSpeciesDto?> GetPokemonSpeciesAsync(string pokemonName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemonName);

        var cacheKey = NormalizePokemonName(pokemonName);
        if (this.pokemonSpeciesCache.TryGetValue(cacheKey, out var cachedSpecies))
        {
            return cachedSpecies;
        }

        var requestUri = $"pokemon-species/{Uri.EscapeDataString(cacheKey)}";
        var pendingRequest = this.pendingSpeciesRequests.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<PokemonSpeciesDto?>>(
                () => this.GetFromApiAsync<PokemonSpeciesDto>(requestUri),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var species = await pendingRequest.Value;
            if (species is not null)
            {
                this.pokemonSpeciesCache.TryAdd(cacheKey, species);
            }

            return species;
        }
        finally
        {
            this.pendingSpeciesRequests.TryRemove(cacheKey, out _);
        }
    }

    private async Task<EvolutionChainDto?> GetEvolutionChainAsync(string requestUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);

        var cacheKey = requestUri.Trim();
        if (this.evolutionChainCache.TryGetValue(cacheKey, out var cachedEvolutionChain))
        {
            return cachedEvolutionChain;
        }

        var pendingRequest = this.pendingEvolutionChainRequests.GetOrAdd(
            cacheKey,
            _ => new Lazy<Task<EvolutionChainDto?>>(
                () => this.GetFromApiAsync<EvolutionChainDto>(cacheKey),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var evolutionChain = await pendingRequest.Value;
            if (evolutionChain is not null)
            {
                this.evolutionChainCache.TryAdd(cacheKey, evolutionChain);
            }

            return evolutionChain;
        }
        finally
        {
            this.pendingEvolutionChainRequests.TryRemove(cacheKey, out _);
        }
    }

    private async Task<T?> GetFromApiAsync<T>(string requestUri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requestUri);

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
