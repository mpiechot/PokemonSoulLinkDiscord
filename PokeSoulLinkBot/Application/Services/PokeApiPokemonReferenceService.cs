using System.Collections.Concurrent;
using System.Text.Json;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Core.Dtos;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Services;

public sealed class PokeApiPokemonReferenceService : IPokemonReferenceService
{
    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    private readonly HttpClient httpClient;
    private readonly ConcurrentDictionary<string, TypeInfo> typeCache = new ConcurrentDictionary<string, TypeInfo>(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, AttackInfo> attackCache = new ConcurrentDictionary<string, AttackInfo>(StringComparer.OrdinalIgnoreCase);
    private readonly Lazy<Task<IReadOnlyList<string>>> moveCatalog;

    public PokeApiPokemonReferenceService(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.moveCatalog = new Lazy<Task<IReadOnlyList<string>>>(this.LoadMoveCatalogAsync);
    }

    public async Task<TypeInfo?> GetTypeInfoAsync(string typeName)
    {
        var key = this.NormalizeName(typeName);
        if (this.typeCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var dto = await this.GetAsync<TypeDto>($"type/{Uri.EscapeDataString(key)}");
        if (dto?.Name is null)
        {
            return null;
        }

        var info = new TypeInfo
        {
            Name = dto.Name,
            DoubleDamageTo = this.GetNames(dto.DamageRelations?.DoubleDamageTo),
            HalfDamageTo = this.GetNames(dto.DamageRelations?.HalfDamageTo),
            NoDamageTo = this.GetNames(dto.DamageRelations?.NoDamageTo),
        };
        this.typeCache[key] = info;
        return info;
    }

    public async Task<AttackInfo?> GetAttackInfoAsync(string moveName)
    {
        var key = this.NormalizeName(moveName);
        if (this.attackCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var dto = await this.GetAsync<MoveDetailDto>($"move/{Uri.EscapeDataString(key)}");
        if (dto?.Name is null)
        {
            return null;
        }

        var info = new AttackInfo
        {
            Name = dto.Name,
            GermanName = dto.Names?.FirstOrDefault(name => string.Equals(name.Language?.Name, "de", StringComparison.OrdinalIgnoreCase))?.Name,
            Type = dto.Type?.Name,
            DamageClass = dto.DamageClass?.Name,
            Power = dto.Power,
            Accuracy = dto.Accuracy,
            Pp = dto.Pp,
            Effect = dto.EffectEntries?.FirstOrDefault(entry => string.Equals(entry.Language?.Name, "en", StringComparison.OrdinalIgnoreCase))?.Effect,
        };
        this.attackCache[key] = info;
        return info;
    }

    public async Task<IReadOnlyList<AttackSuggestion>> GetAttackSuggestionsAsync(string query)
    {
        var normalizedQuery = string.IsNullOrWhiteSpace(query)
            ? string.Empty
            : this.NormalizeName(query).Replace('-', ' ');
        var catalog = await this.moveCatalog.Value;
        var cachedGermanMatches = this.attackCache.Values
            .Where(info => !string.IsNullOrWhiteSpace(info.GermanName) &&
                info.GermanName!.Contains(query ?? string.Empty, StringComparison.CurrentCultureIgnoreCase))
            .Select(info => info.Name);
        var apiMatches = catalog.Where(name => string.IsNullOrWhiteSpace(normalizedQuery) ||
            name.Replace('-', ' ').Contains(normalizedQuery, StringComparison.OrdinalIgnoreCase));
        var candidates = cachedGermanMatches.Concat(apiMatches)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(25)
            .ToList();

        var suggestions = await Task.WhenAll(candidates.Select(async apiName =>
        {
            var info = await this.GetAttackInfoAsync(apiName);
            return new AttackSuggestion
            {
                ApiName = apiName,
                DisplayName = info?.GermanName ?? apiName.Replace('-', ' '),
            };
        }));

        return suggestions
            .OrderBy(suggestion => suggestion.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    private async Task<IReadOnlyList<string>> LoadMoveCatalogAsync()
    {
        var dto = await this.GetAsync<MoveResourceListDto>("move?limit=10000");
        return dto?.Results?
            .Where(resource => !string.IsNullOrWhiteSpace(resource.Name))
            .Select(resource => resource.Name!)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList() ?? new List<string>();
    }

    private async Task<T?> GetAsync<T>(string relativePath)
    {
        using var response = await HttpRequestHelper.GetAsync(this.httpClient, relativePath);
        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions);
    }

    private string NormalizeName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return name.Trim().ToLowerInvariant().Replace(' ', '-');
    }

    private IReadOnlyList<string> GetNames(IEnumerable<NamedApiResourceDto>? resources)
    {
        return resources?.Where(resource => !string.IsNullOrWhiteSpace(resource.Name))
            .Select(resource => resource.Name!)
            .ToList() ?? new List<string>();
    }
}
