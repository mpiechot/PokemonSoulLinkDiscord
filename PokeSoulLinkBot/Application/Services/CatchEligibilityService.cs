using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Services;

/// <summary>
/// Provides catch eligibility checks for active Soul Link runs.
/// </summary>
public sealed class CatchEligibilityService : ICatchEligibilityService
{
    private readonly IRunService runService;
    private readonly IPokedexService pokedexService;

    /// <summary>
    /// Initializes a new instance of the <see cref="CatchEligibilityService"/> class.
    /// </summary>
    /// <param name="runService">The run service.</param>
    /// <param name="pokedexService">The Pokédex service.</param>
    public CatchEligibilityService(
        IRunService runService,
        IPokedexService pokedexService)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.pokedexService = pokedexService ?? throw new ArgumentNullException(nameof(pokedexService));
    }

    /// <inheritdoc />
    public async Task<CatchCheckResult> CheckCatchAsync(string guildId, string pokemonName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemonName);

        var activeRun = this.runService.GetActiveRun(guildId);
        var familyCache = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var requestedFamily = await this.GetEvolutionFamilyAsync(pokemonName, familyCache);

        foreach (var linkGroup in activeRun.LinkGroups)
        {
            foreach (var entry in linkGroup.Entries)
            {
                var caughtFamily = await this.GetStoredCatchFamilyAsync(entry.PokemonName, familyCache);
                if (!requestedFamily.Overlaps(caughtFamily))
                {
                    continue;
                }

                return new CatchCheckResult
                {
                    RequestedPokemonName = pokemonName,
                    IsAllowed = false,
                    Match = new CatchCheckMatch
                    {
                        Route = linkGroup.Route,
                        PlayerName = entry.PlayerName,
                        PokemonName = entry.PokemonName,
                        Status = GetStatus(activeRun, linkGroup, entry),
                    },
                };
            }
        }

        return new CatchCheckResult
        {
            RequestedPokemonName = pokemonName,
            IsAllowed = true,
        };
    }

    private static string GetStatus(SoulLinkRun run, LinkGroup linkGroup, LinkedPokemon pokemon)
    {
        if (!pokemon.IsAlive)
        {
            return "Dead";
        }

        var isInTeam = run.ActiveLinks.Any(activeLink =>
            activeLink is not null &&
            (ReferenceEquals(activeLink, linkGroup) ||
                activeLink.Id == linkGroup.Id ||
                string.Equals(activeLink.Route, linkGroup.Route, StringComparison.OrdinalIgnoreCase)));

        return isInTeam ? "Team" : "Box";
    }

    private static string NormalizePokemonName(string pokemonName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemonName);

        return pokemonName.Trim()
            .ToLowerInvariant()
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace("_", string.Empty, StringComparison.Ordinal);
    }

    private async Task<HashSet<string>> GetStoredCatchFamilyAsync(
        string pokemonName,
        Dictionary<string, HashSet<string>> familyCache)
    {
        try
        {
            return await this.GetEvolutionFamilyAsync(pokemonName, familyCache);
        }
        catch (InvalidOperationException)
        {
            var family = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                NormalizePokemonName(pokemonName),
            };

            familyCache[pokemonName] = family;
            return family;
        }
    }

    private async Task<HashSet<string>> GetEvolutionFamilyAsync(
        string pokemonName,
        Dictionary<string, HashSet<string>> familyCache)
    {
        if (familyCache.TryGetValue(pokemonName, out var cachedFamily))
        {
            return cachedFamily;
        }

        try
        {
            var pokedexEntry = await this.pokedexService.GetPokedexEntryAsync(pokemonName);
            var family = pokedexEntry.Rows
                .Select(row => NormalizePokemonName(row.PokemonName))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            family.Add(NormalizePokemonName(pokedexEntry.PokemonName));
            familyCache[pokemonName] = family;

            return family;
        }
        catch (InvalidOperationException exception)
        {
            throw new InvalidOperationException(
                $"Pokémon '{pokemonName}' was not found or is ambiguous. Check the German or English name.",
                exception);
        }
    }
}
