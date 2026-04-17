using System.Net.Http.Json;
using System.Text.Json;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Core.Dtos;
using PokeSoulLinkBot.Core.Models;
using Serilog;

namespace PokeSoulLinkBot.Application.Services;

/// <summary>
/// Loads Pokemon edition and encounter location data from PokéAPI and caches it locally.
/// </summary>
public sealed class PokeApiGameDataCatalogService : IGameDataCatalogService
{
    private static readonly IReadOnlyCollection<(string Name, string DisplayName)> FallbackEditions =
    [
        ("red", "Red"),
        ("blue", "Blue"),
        ("yellow", "Yellow"),
        ("gold", "Gold"),
        ("silver", "Silver"),
        ("crystal", "Crystal"),
        ("ruby", "Ruby"),
        ("sapphire", "Sapphire"),
        ("emerald", "Emerald"),
        ("firered", "Fire Red"),
        ("leafgreen", "Leaf Green"),
        ("diamond", "Diamond"),
        ("pearl", "Pearl"),
        ("platinum", "Platinum"),
        ("heartgold", "Heart Gold"),
        ("soulsilver", "Soul Silver"),
        ("black", "Black"),
        ("white", "White"),
        ("black-2", "Black 2"),
        ("white-2", "White 2"),
        ("x", "X"),
        ("y", "Y"),
        ("omega-ruby", "Omega Ruby"),
        ("alpha-sapphire", "Alpha Sapphire"),
        ("sun", "Sun"),
        ("moon", "Moon"),
        ("ultra-sun", "Ultra Sun"),
        ("ultra-moon", "Ultra Moon"),
        ("sword", "Sword"),
        ("shield", "Shield"),
        ("brilliant-diamond", "Brilliant Diamond"),
        ("shining-pearl", "Shining Pearl"),
        ("legends-arceus", "Legends Arceus"),
        ("scarlet", "Scarlet"),
        ("violet", "Violet"),
    ];

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string cacheFilePath;
    private readonly HttpClient httpClient;
    private readonly SemaphoreSlim initializationLock = new SemaphoreSlim(1, 1);
    private GameDataCatalog? catalog;

    /// <summary>
    /// Initializes a new instance of the <see cref="PokeApiGameDataCatalogService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used to access PokéAPI.</param>
    /// <param name="cacheFilePath">The local cache file path.</param>
    public PokeApiGameDataCatalogService(HttpClient httpClient, string cacheFilePath)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.cacheFilePath = cacheFilePath ?? throw new ArgumentNullException(nameof(cacheFilePath));
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await this.initializationLock.WaitAsync();
        try
        {
            if (this.catalog is not null)
            {
                Log.Debug(
                    "Game data catalog already initialized with {EditionCount} editions.",
                    this.catalog.Editions.Count);
                return;
            }

            Log.Information("Initializing game data catalog from {CacheFilePath}.", this.cacheFilePath);
            this.catalog = await this.LoadCatalogFromCacheAsync();
            if (this.catalog is not null)
            {
                AddMissingFallbackEditions(this.catalog);
                Log.Information(
                    "Loaded game data catalog from cache with {EditionCount} editions.",
                    this.catalog.Editions.Count);
                return;
            }

            try
            {
                Log.Information("No game data catalog cache found. Refreshing from PokeAPI.");
                this.catalog = await this.RefreshCatalogAsync();
                AddMissingFallbackEditions(this.catalog);
                await this.SaveCatalogAsync(this.catalog);
                Log.Information(
                    "Refreshed game data catalog with {EditionCount} editions and saved it to cache.",
                    this.catalog.Editions.Count);
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
            {
                Log.Warning(
                    exception,
                    "Game data catalog refresh failed. Falling back to built-in edition list.");
                this.catalog = CreateFallbackCatalog();
            }
        }
        finally
        {
            this.initializationLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<GameEditionInfo>> GetEditionsAsync()
    {
        await this.InitializeAsync();

        var editions = this.catalog?.Editions ?? (IReadOnlyCollection<GameEditionInfo>)Array.Empty<GameEditionInfo>();
        Log.Debug("Returning {EditionCount} game edition suggestions.", editions.Count);

        return editions;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<string>> GetRoutesAsync(string edition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(edition);

        await this.InitializeAsync();

        var normalizedEdition = Normalize(edition);
        var matchingEdition = this.catalog?.Editions.FirstOrDefault(item =>
            string.Equals(item.Name, normalizedEdition, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(item.DisplayName, edition.Trim(), StringComparison.OrdinalIgnoreCase));

        var routes = matchingEdition?.Routes ?? (IReadOnlyCollection<string>)Array.Empty<string>();
        Log.Debug(
            "Returning {RouteCount} route suggestions for edition '{Edition}'.",
            routes.Count,
            edition);

        return routes;
    }

    private static string CreateDisplayName(string value)
    {
        var textInfo = System.Globalization.CultureInfo.InvariantCulture.TextInfo;
        return textInfo.ToTitleCase(value.Replace('-', ' '));
    }

    private static GameDataCatalog CreateFallbackCatalog()
    {
        var catalog = new GameDataCatalog { RefreshedAtUtc = DateTime.UtcNow };
        AddMissingFallbackEditions(catalog);

        return catalog;
    }

    private static void AddMissingFallbackEditions(GameDataCatalog catalog)
    {
        foreach (var fallbackEdition in FallbackEditions)
        {
            var hasEdition = catalog.Editions.Any(edition =>
                string.Equals(edition.Name, fallbackEdition.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(edition.DisplayName, fallbackEdition.DisplayName, StringComparison.OrdinalIgnoreCase));

            if (!hasEdition)
            {
                catalog.Editions.Add(new GameEditionInfo
                {
                    Name = fallbackEdition.Name,
                    DisplayName = fallbackEdition.DisplayName,
                });
            }
        }

        catalog.Editions = catalog.Editions
            .OrderBy(edition => edition.DisplayName)
            .ToList();
    }

    private static string GetDisplayName(LocationAreaDto locationArea)
    {
        var germanName = locationArea.Names.FirstOrDefault(name =>
            string.Equals(name.Language?.Name, "de", StringComparison.OrdinalIgnoreCase));

        var englishName = locationArea.Names.FirstOrDefault(name =>
            string.Equals(name.Language?.Name, "en", StringComparison.OrdinalIgnoreCase));

        return germanName?.Name ?? englishName?.Name ?? CreateDisplayName(locationArea.Name);
    }

    private static string Normalize(string value)
    {
        return value.Trim().ToLowerInvariant().Replace(' ', '-');
    }

    private async Task<GameDataCatalog?> LoadCatalogFromCacheAsync()
    {
        if (!File.Exists(this.cacheFilePath))
        {
            Log.Debug("Game data catalog cache file does not exist: {CacheFilePath}.", this.cacheFilePath);
            return null;
        }

        await using var stream = File.OpenRead(this.cacheFilePath);
        return await JsonSerializer.DeserializeAsync<GameDataCatalog>(stream, JsonOptions);
    }

    private async Task<GameDataCatalog> RefreshCatalogAsync()
    {
        Log.Debug("Fetching Pokemon versions from PokeAPI.");
        var versionList = await this.httpClient.GetFromJsonAsync<NamedApiResourceListDto>("version?limit=10000", JsonOptions);
        var versions = versionList?.Results ?? new List<NamedApiResourceDto>();
        var editionsByName = versions
            .Where(version => !string.IsNullOrWhiteSpace(version.Name))
            .Select(version => new GameEditionInfo
            {
                Name = Normalize(version.Name!),
                DisplayName = CreateDisplayName(version.Name!),
            })
            .ToDictionary(edition => edition.Name, StringComparer.OrdinalIgnoreCase);

        Log.Information("Fetched {EditionCount} Pokemon versions from PokeAPI.", editionsByName.Count);
        await this.AddEncounterRoutesAsync(editionsByName);

        return new GameDataCatalog
        {
            RefreshedAtUtc = DateTime.UtcNow,
            Editions = editionsByName.Values.OrderBy(edition => edition.DisplayName).ToList(),
        };
    }

    private async Task AddEncounterRoutesAsync(Dictionary<string, GameEditionInfo> editionsByName)
    {
        Log.Debug("Fetching Pokemon location areas from PokeAPI.");
        var locationAreaList = await this.httpClient.GetFromJsonAsync<NamedApiResourceListDto>("location-area?limit=10000", JsonOptions);
        var locationAreas = locationAreaList?.Results ?? new List<NamedApiResourceDto>();
        var processedLocationAreaCount = 0;

        foreach (var resource in locationAreas.Where(resource => !string.IsNullOrWhiteSpace(resource.Name)))
        {
            var locationArea = await this.httpClient.GetFromJsonAsync<LocationAreaDto>(
                $"location-area/{resource.Name}",
                JsonOptions);

            if (locationArea is null)
            {
                continue;
            }

            processedLocationAreaCount++;
            var routeName = GetDisplayName(locationArea);
            var versionNames = locationArea.PokemonEncounters
                .SelectMany(encounter => encounter.VersionDetails)
                .Select(versionDetail => versionDetail.Version.Name)
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Select(name => Normalize(name!))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var versionName in versionNames)
            {
                if (editionsByName.TryGetValue(versionName, out var edition) &&
                    !edition.Routes.Contains(routeName, StringComparer.OrdinalIgnoreCase))
                {
                    edition.Routes.Add(routeName);
                }
            }
        }

        foreach (var edition in editionsByName.Values)
        {
            edition.Routes = edition.Routes.OrderBy(route => route).ToList();
        }

        Log.Information(
            "Processed {LocationAreaCount} location areas for route autocomplete data.",
            processedLocationAreaCount);
    }

    private async Task SaveCatalogAsync(GameDataCatalog refreshedCatalog)
    {
        var directoryPath = Path.GetDirectoryName(this.cacheFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using var stream = File.Create(this.cacheFilePath);
        await JsonSerializer.SerializeAsync(stream, refreshedCatalog, JsonOptions);
    }
}
