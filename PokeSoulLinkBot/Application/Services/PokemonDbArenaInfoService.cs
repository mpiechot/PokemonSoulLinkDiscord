// <copyright file="PokemonDbArenaInfoService.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Net;
using System.Text.RegularExpressions;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Services;

/// <summary>
/// Loads arena information from Pokemon Database gym leader pages.
/// </summary>
public sealed class PokemonDbArenaInfoService : IArenaInfoService
{
    private static readonly Uri BaseUri = new Uri("https://pokemondb.net/");

    private static readonly Regex ArenaSectionRegex = new Regex(
        "<h2 id=\"gym-(?<number>\\d+)\">Gym #\\d+,\\s*(?<location>.*?)</h2>(?<content>.*?)(?=<h2 id=\"(?:gym-\\d+|elite4-\\d+|champion-\\d+)\">|</main>)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex LeaderNameRegex = new Regex(
        "<span class=\"ent-name\">(?<name>.*?)</span>",
        RegexOptions.IgnoreCase | RegexOptions.Singleline,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex LevelRegex = new Regex(
        "Level\\s+(?<level>\\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Singleline,
        TimeSpan.FromMilliseconds(250));

    private static readonly IReadOnlyDictionary<string, string> SourceSlugsByEdition =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["red"] = "red-blue",
            ["blue"] = "red-blue",
            ["yellow"] = "yellow",
            ["gold"] = "gold-silver",
            ["silver"] = "gold-silver",
            ["crystal"] = "crystal",
            ["ruby"] = "ruby-sapphire",
            ["rubin"] = "ruby-sapphire",
            ["sapphire"] = "ruby-sapphire",
            ["saphir"] = "ruby-sapphire",
            ["emerald"] = "emerald",
            ["smaragd"] = "emerald",
            ["firered"] = "firered-leafgreen",
            ["fire-red"] = "firered-leafgreen",
            ["leafgreen"] = "firered-leafgreen",
            ["leaf-green"] = "firered-leafgreen",
            ["diamond"] = "diamond-pearl",
            ["pearl"] = "diamond-pearl",
            ["platinum"] = "platinum",
            ["heartgold"] = "heartgold-soulsilver",
            ["heart-gold"] = "heartgold-soulsilver",
            ["soulsilver"] = "heartgold-soulsilver",
            ["soul-silver"] = "heartgold-soulsilver",
            ["black"] = "black-white",
            ["white"] = "black-white",
            ["black-2"] = "black-2-white-2",
            ["white-2"] = "black-2-white-2",
            ["x"] = "x-y",
            ["y"] = "x-y",
            ["omega-ruby"] = "omega-ruby-alpha-sapphire",
            ["omega ruby"] = "omega-ruby-alpha-sapphire",
            ["alpha-sapphire"] = "omega-ruby-alpha-sapphire",
            ["alpha sapphire"] = "omega-ruby-alpha-sapphire",
            ["sun"] = "sun-moon",
            ["moon"] = "sun-moon",
            ["ultra-sun"] = "ultra-sun-ultra-moon",
            ["ultra-moon"] = "ultra-sun-ultra-moon",
            ["sword"] = "sword-shield",
            ["shield"] = "sword-shield",
        };

    private readonly Dictionary<string, IReadOnlyDictionary<int, ArenaInfo>> cachedArenaInfosBySlug =
        new Dictionary<string, IReadOnlyDictionary<int, ArenaInfo>>(StringComparer.OrdinalIgnoreCase);

    private readonly HttpClient httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="PokemonDbArenaInfoService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used to request Pokemon Database pages.</param>
    public PokemonDbArenaInfoService(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc />
    public async Task<ArenaInfo> GetArenaInfoAsync(string edition, int arenaNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(edition);

        if (arenaNumber is < 1 or > 8)
        {
            throw new InvalidOperationException($"Arena '{arenaNumber}' ist ungültig. Bitte wähle eine Arena zwischen 1 und 8.");
        }

        var sourceSlug = ResolveSourceSlug(edition);
        var arenasByNumber = await this.GetArenaInfosByNumberAsync(sourceSlug, edition.Trim());

        if (!arenasByNumber.TryGetValue(arenaNumber, out var arenaInfo))
        {
            throw new InvalidOperationException($"Für Arena '{arenaNumber}' wurden keine Daten für '{edition}' gefunden.");
        }

        return arenaInfo;
    }

    /// <summary>
    /// Warms the in-memory cache for known Pokemon Database gym leader pages.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task WarmUpKnownEditionsAsync()
    {
        var representativeEditions = SourceSlugsByEdition
            .GroupBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToList();

        foreach (var representativeEdition in representativeEditions)
        {
            try
            {
                await this.GetArenaInfosByNumberAsync(representativeEdition.Value, representativeEdition.Key);
            }
            catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                Console.WriteLine($"Arena data warm-up failed for '{representativeEdition.Key}': {exception.Message}");
            }
        }
    }

    private static string ResolveSourceSlug(string edition)
    {
        var normalizedEdition = edition.Trim().ToLowerInvariant();
        if (SourceSlugsByEdition.TryGetValue(normalizedEdition, out var sourceSlug))
        {
            return sourceSlug;
        }

        return normalizedEdition.Replace(' ', '-');
    }

    private static IReadOnlyDictionary<int, ArenaInfo> ParseArenaInfos(string html, string edition)
    {
        var arenasByNumber = new Dictionary<int, ArenaInfo>();
        foreach (Match sectionMatch in ArenaSectionRegex.Matches(html))
        {
            var arenaNumber = int.Parse(sectionMatch.Groups["number"].Value);
            var sectionContent = sectionMatch.Groups["content"].Value;
            var leaderMatch = LeaderNameRegex.Match(sectionContent);
            if (!leaderMatch.Success)
            {
                continue;
            }

            var levels = LevelRegex.Matches(sectionContent)
                .Select(match => int.Parse(match.Groups["level"].Value))
                .ToArray();

            arenasByNumber[arenaNumber] = new ArenaInfo
            {
                ArenaNumber = arenaNumber,
                Edition = edition,
                LeaderName = CleanHtmlText(leaderMatch.Groups["name"].Value),
                Levels = levels,
                Location = CleanHtmlText(sectionMatch.Groups["location"].Value),
            };
        }

        return arenasByNumber;
    }

    private static string CleanHtmlText(string value)
    {
        var withoutTags = Regex.Replace(value, "<.*?>", string.Empty, RegexOptions.Singleline, TimeSpan.FromMilliseconds(250));
        return WebUtility.HtmlDecode(withoutTags).Trim();
    }

    private async Task<IReadOnlyDictionary<int, ArenaInfo>> GetArenaInfosByNumberAsync(string sourceSlug, string edition)
    {
        if (this.cachedArenaInfosBySlug.TryGetValue(sourceSlug, out var cachedArenaInfos))
        {
            return cachedArenaInfos;
        }

        var sourceUri = new Uri(BaseUri, $"{sourceSlug}/gymleaders-elitefour");
        var html = await this.httpClient.GetStringAsync(sourceUri);
        var arenaInfos = ParseArenaInfos(html, edition);

        if (arenaInfos.Count == 0)
        {
            throw new InvalidOperationException($"Für '{edition}' konnten keine Arenadaten geladen werden.");
        }

        this.cachedArenaInfosBySlug[sourceSlug] = arenaInfos;
        return arenaInfos;
    }
}
