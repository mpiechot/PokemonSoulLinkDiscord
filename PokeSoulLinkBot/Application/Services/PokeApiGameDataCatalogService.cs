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
    private const int CurrentSchemaVersion = 1;
    private const int MaxParallelLocationAreaRequests = 8;

    private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    private readonly string cacheFilePath;
    private readonly string? fallbackCatalogFilePath;
    private readonly HttpClient httpClient;
    private readonly SemaphoreSlim initializationLock = new SemaphoreSlim(1, 1);
    private GameDataCatalog? catalog;
    private Task? refreshTask;

    /// <summary>
    /// Initializes a new instance of the <see cref="PokeApiGameDataCatalogService"/> class.
    /// </summary>
    /// <param name="httpClient">The HTTP client used to access PokéAPI.</param>
    /// <param name="cacheFilePath">The local cache file path.</param>
    /// <param name="fallbackCatalogFilePath">The bundled fallback catalog file path.</param>
    public PokeApiGameDataCatalogService(
        HttpClient httpClient,
        string cacheFilePath,
        string? fallbackCatalogFilePath = null)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        this.cacheFilePath = cacheFilePath ?? throw new ArgumentNullException(nameof(cacheFilePath));
        this.fallbackCatalogFilePath = fallbackCatalogFilePath;
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

            Log.Information("Initializing game data catalog. Cache path: {CacheFilePath}.", this.cacheFilePath);
            this.catalog = await this.LoadCatalogFromFileAsync(this.cacheFilePath, "cache");
            if (this.catalog is not null)
            {
                Log.ForContext("EventName", "GameDataCatalogLoaded").Information(
                    "Using game data catalog source cache with {EditionCount} editions and {RouteCount} routes.",
                    this.catalog.Editions.Count,
                    this.catalog.Editions.Sum(edition => edition.Routes.Count));
                this.StartRefreshInBackground();
                return;
            }

            this.catalog = await this.LoadFallbackCatalogAsync();
            if (this.catalog is not null)
            {
                Log.ForContext("EventName", "GameDataCatalogLoaded").Information(
                    "Using game data catalog source fallback with {EditionCount} editions and {RouteCount} routes.",
                    this.catalog.Editions.Count,
                    this.catalog.Editions.Sum(edition => edition.Routes.Count));
                this.StartRefreshInBackground();
                return;
            }

            this.StartRefreshInBackground();
            Log.ForContext("EventName", "GameDataCatalogUnavailable").Information(
                "Game data catalog is not ready. No cache was found, and PokeAPI refresh is running in the background.");
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
        if (editions.Count == 0)
        {
            Log.Information("Game data catalog is not ready. Returning no game edition suggestions.");
        }
        else
        {
            Log.Debug("Returning {EditionCount} game edition suggestions.", editions.Count);
        }

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
        if (this.catalog is null)
        {
            Log.Information(
                "Game data catalog is not ready. Returning no route suggestions for edition '{Edition}'.",
                edition);
        }
        else
        {
            Log.Debug(
                "Returning {RouteCount} route suggestions for edition '{Edition}'.",
                routes.Count,
                edition);
        }

        return routes;
    }

    private static async IAsyncEnumerable<T> ParallelizeAsync<T>(
        IEnumerable<Task<T>> tasks,
        int maxDegreeOfParallelism)
    {
        var pendingTasks = new List<Task<T>>(maxDegreeOfParallelism);
        using var enumerator = tasks.GetEnumerator();

        while (pendingTasks.Count < maxDegreeOfParallelism && enumerator.MoveNext())
        {
            pendingTasks.Add(enumerator.Current);
        }

        while (pendingTasks.Count > 0)
        {
            var completedTask = await Task.WhenAny(pendingTasks);
            pendingTasks.Remove(completedTask);

            if (enumerator.MoveNext())
            {
                pendingTasks.Add(enumerator.Current);
            }

            yield return await completedTask;
        }
    }

    private static string CreateDisplayName(string value)
    {
        var textInfo = System.Globalization.CultureInfo.InvariantCulture.TextInfo;
        return textInfo.ToTitleCase(value.Replace('-', ' '));
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

    private static bool IsCacheWriteException(Exception exception)
    {
        return exception is IOException or UnauthorizedAccessException or NotSupportedException;
    }

    private static bool IsValidCatalog(GameDataCatalog? catalog)
    {
        return catalog is { SchemaVersion: CurrentSchemaVersion } &&
            catalog.Editions.Count > 0 &&
            catalog.Editions.All(edition =>
                !string.IsNullOrWhiteSpace(edition.Name) &&
                !string.IsNullOrWhiteSpace(edition.DisplayName));
    }

    private async Task<GameDataCatalog?> LoadCatalogFromFileAsync(string filePath, string sourceName)
    {
        if (!File.Exists(filePath))
        {
            Log.Debug(
                "Game data catalog {SourceName} file does not exist: {CatalogFilePath}.",
                sourceName,
                filePath);
            return null;
        }

        await using var stream = File.OpenRead(filePath);
        try
        {
            var loadedCatalog = await JsonSerializer.DeserializeAsync<GameDataCatalog>(stream, JsonOptions);
            if (!IsValidCatalog(loadedCatalog))
            {
                Log.Warning(
                    "Game data catalog {SourceName} file is empty or incompatible: {CatalogFilePath}.",
                    sourceName,
                    filePath);
                return null;
            }

            return loadedCatalog;
        }
        catch (JsonException exception)
        {
            Log.Warning(
                exception,
                "Game data catalog {SourceName} file could not be read from {CatalogFilePath}.",
                sourceName,
                filePath);
            return null;
        }
    }

    private async Task<GameDataCatalog?> LoadFallbackCatalogAsync()
    {
        if (string.IsNullOrWhiteSpace(this.fallbackCatalogFilePath))
        {
            Log.Debug("No bundled game data fallback catalog path was configured.");
            return null;
        }

        return await this.LoadCatalogFromFileAsync(this.fallbackCatalogFilePath, "fallback");
    }

    private async Task<GameDataCatalog> RefreshCatalogAsync()
    {
        Log.ForContext("EventName", "GameDataCatalogRefreshStarted").Information(
            "Using game data catalog source PokeAPI for refresh.");
        Log.Debug("Fetching Pokemon versions from PokeAPI.");
        var versionList = await HttpRequestHelper.GetFromJsonAsync<NamedApiResourceListDto>(
            this.httpClient,
            "version?limit=10000",
            JsonOptions);
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
            SchemaVersion = CurrentSchemaVersion,
            RefreshedAtUtc = DateTime.UtcNow,
            Editions = editionsByName.Values.OrderBy(edition => edition.DisplayName).ToList(),
        };
    }

    private async Task AddEncounterRoutesAsync(Dictionary<string, GameEditionInfo> editionsByName)
    {
        Log.Debug("Fetching Pokemon location areas from PokeAPI.");
        var locationAreaList = await HttpRequestHelper.GetFromJsonAsync<NamedApiResourceListDto>(
            this.httpClient,
            "location-area?limit=10000",
            JsonOptions);
        var locationAreas = locationAreaList?.Results ?? new List<NamedApiResourceDto>();
        var routeNamesByVersion = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        var locationAreaTasks = locationAreas
            .Where(resource => !string.IsNullOrWhiteSpace(resource.Name))
            .Select(resource => this.GetLocationAreaAsync(resource.Name!));

        var processedLocationAreaCount = 0;
        await foreach (var locationArea in ParallelizeAsync(locationAreaTasks, MaxParallelLocationAreaRequests))
        {
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
                if (!editionsByName.ContainsKey(versionName))
                {
                    continue;
                }

                if (!routeNamesByVersion.TryGetValue(versionName, out var routeNames))
                {
                    routeNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    routeNamesByVersion[versionName] = routeNames;
                }

                routeNames.Add(routeName);
            }
        }

        foreach (var edition in editionsByName.Values)
        {
            if (routeNamesByVersion.TryGetValue(edition.Name, out var routeNames))
            {
                edition.Routes.AddRange(routeNames);
            }

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

    private async Task<LocationAreaDto?> GetLocationAreaAsync(string locationAreaName)
    {
        try
        {
            return await HttpRequestHelper.GetFromJsonAsync<LocationAreaDto>(
                this.httpClient,
                $"location-area/{locationAreaName}",
                JsonOptions);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            Log.Warning(
                exception,
                "Could not fetch location area {LocationAreaName} from PokeAPI.",
                locationAreaName);
            return null;
        }
    }

    private void StartRefreshInBackground()
    {
        this.refreshTask ??= Task.Run(this.RefreshCatalogInBackgroundAsync);
    }

    private async Task RefreshCatalogInBackgroundAsync()
    {
        try
        {
            var refreshedCatalog = await this.RefreshCatalogAsync();
            this.catalog = refreshedCatalog;

            try
            {
                await this.SaveCatalogAsync(refreshedCatalog);
            }
            catch (Exception exception) when (IsCacheWriteException(exception))
            {
                Log.Warning(
                    exception,
                    "Using refreshed game data catalog from PokeAPI, but cache could not be saved to {CacheFilePath}.",
                    this.cacheFilePath);
                return;
            }

            Log.ForContext("EventName", "GameDataCatalogLoaded").Information(
                "Using game data catalog source PokeAPI with {EditionCount} editions and {RouteCount} routes. Saved cache to {CacheFilePath}.",
                refreshedCatalog.Editions.Count,
                refreshedCatalog.Editions.Sum(edition => edition.Routes.Count),
                this.cacheFilePath);
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or TaskCanceledException)
        {
            Log.ForContext("EventName", "GameDataCatalogRefreshFailed").Warning(
                exception,
                "Game data catalog PokeAPI refresh failed. The current catalog remains unchanged.");
        }
        finally
        {
            this.refreshTask = null;
        }
    }
}
