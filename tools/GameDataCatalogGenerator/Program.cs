using System.Globalization;
using System.Text.Json;

namespace GameDataCatalogGenerator;

internal static class Program
{
    private const string DefaultOutputPath = "PokeSoulLinkBot/Data/game-data-fallback.json";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args.Contains("--help", StringComparer.OrdinalIgnoreCase))
            {
                PrintUsage();
                return args.Length == 0 ? 1 : 0;
            }

            return args[0].ToLowerInvariant() switch
            {
                "generate" => await GenerateAsync(args[1..]),
                "validate" => Validate(args[1..]),
                _ => throw new ArgumentException($"Unknown command '{args[0]}'."),
            };
        }
        catch (Exception exception) when (
            exception is ArgumentException or IOException or InvalidDataException or
            JsonException or HttpRequestException or TaskCanceledException)
        {
            Console.Error.WriteLine(exception.Message);
            return 1;
        }
    }

    private static async Task<int> GenerateAsync(string[] args)
    {
        string outputPath = GetOption(args, "--output") ?? DefaultOutputPath;
        DateTime refreshedAtUtc = ParseTimestamp(GetOption(args, "--refreshed-at-utc"));

        using var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
            Timeout = TimeSpan.FromSeconds(30),
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(30));
        var service = new GameDataFallbackCatalogGenerationService(httpClient);

        await service.GenerateAsync(outputPath, refreshedAtUtc, timeout.Token);
        Console.WriteLine($"Generated and validated fallback catalog: {Path.GetFullPath(outputPath)}");
        return 0;
    }

    private static int Validate(string[] args)
    {
        string inputPath = GetOption(args, "--input") ?? DefaultOutputPath;
        IReadOnlyList<string> errors = GameDataFallbackCatalogFile.Validate(inputPath);
        if (errors.Count > 0)
        {
            Console.Error.WriteLine($"Fallback catalog is invalid:{Environment.NewLine}- {string.Join($"{Environment.NewLine}- ", errors)}");
            return 1;
        }

        Console.WriteLine($"Fallback catalog is valid: {Path.GetFullPath(inputPath)}");
        return 0;
    }

    private static string? GetOption(IReadOnlyList<string> args, string optionName)
    {
        for (var index = 0; index < args.Count; index++)
        {
            if (!string.Equals(args[index], optionName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                throw new ArgumentException($"Option '{optionName}' requires a value.");
            }

            return args[index + 1];
        }

        return null;
    }

    private static DateTime ParseTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return DateTime.UtcNow;
        }

        if (!DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out DateTimeOffset timestamp))
        {
            throw new ArgumentException(
                $"Invalid --refreshed-at-utc value '{value}'. Use an ISO-8601 timestamp.");
        }

        return timestamp.UtcDateTime;
    }

    private static void PrintUsage()
    {
        Console.WriteLine(
            """
            GameDataCatalogGenerator

            Generate from PokeAPI:
              dotnet run --project tools/GameDataCatalogGenerator -- generate [--output <path>] [--refreshed-at-utc <ISO-8601>]

            Validate without network access:
              dotnet run --project tools/GameDataCatalogGenerator -- validate [--input <path>]
            """);
    }
}
