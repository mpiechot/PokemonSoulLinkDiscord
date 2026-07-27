using System.Text.Json;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Core.Models;

namespace GameDataCatalogGenerator;

/// <summary>
/// Generates a validated fallback catalog from PokeAPI data.
/// </summary>
public sealed class GameDataFallbackCatalogGenerationService
{
    private static readonly JsonSerializerOptions JsonOptions =
        new JsonSerializerOptions(JsonSerializerDefaults.Web);

    private readonly HttpClient httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="GameDataFallbackCatalogGenerationService"/> class.
    /// </summary>
    /// <param name="httpClient">The configured PokeAPI client.</param>
    public GameDataFallbackCatalogGenerationService(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <summary>
    /// Generates and writes the fallback catalog.
    /// </summary>
    /// <param name="outputPath">The destination path.</param>
    /// <param name="refreshedAtUtc">The timestamp to record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public async Task GenerateAsync(
        string outputPath,
        DateTime refreshedAtUtc,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string fullOutputPath = Path.GetFullPath(outputPath);
        string sourceFilePath = $"{fullOutputPath}.{Guid.NewGuid():N}.source.tmp";

        try
        {
            var catalogService = new PokeApiGameDataCatalogService(this.httpClient, sourceFilePath);
            await catalogService.InitializeAsync();
            await WaitForRefreshAsync(catalogService, sourceFilePath, cancellationToken);

            await using FileStream stream = File.OpenRead(sourceFilePath);
            GameDataCatalog sourceCatalog =
                await JsonSerializer.DeserializeAsync<GameDataCatalog>(stream, JsonOptions, cancellationToken)
                ?? throw new InvalidDataException("PokeAPI produced an empty game-data catalog.");

            GameDataCatalog catalog =
                GameDataFallbackCatalogCanonicalizer.Canonicalize(sourceCatalog, refreshedAtUtc);
            IReadOnlyList<string> errors = GameDataFallbackCatalogValidator.Validate(catalog);
            if (errors.Count > 0)
            {
                throw new InvalidDataException(
                    $"Generated game-data catalog is invalid:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}");
            }

            GameDataFallbackCatalogFile.WriteAtomically(fullOutputPath, catalog);
        }
        finally
        {
            if (File.Exists(sourceFilePath))
            {
                File.Delete(sourceFilePath);
            }
        }
    }

    private static async Task WaitForRefreshAsync(
        PokeApiGameDataCatalogService catalogService,
        string sourceFilePath,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GameDataCatalogStatus status = catalogService.GetStatus();
            if (!status.IsRefreshRunning)
            {
                if (string.Equals(status.Source, "PokeAPI", StringComparison.Ordinal) &&
                    File.Exists(sourceFilePath))
                {
                    return;
                }

                throw new InvalidDataException("PokeAPI refresh finished without producing a catalog file.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
    }
}
