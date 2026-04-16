using System.Text.Json;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Loads arena level data from JSON.
/// </summary>
public static class ArenaLevelDataLoader
{
    /// <summary>
    /// Loads arena levels from the specified JSON file path.
    /// </summary>
    /// <param name="filePath">The JSON file path.</param>
    /// <returns>A read-only dictionary grouped by edition and arena number.</returns>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<int, IReadOnlyList<int>>> Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("Arena level data file not found.", filePath);
        }

        var json = File.ReadAllText(filePath);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        };

        var rawData = JsonSerializer.Deserialize<Dictionary<string, Dictionary<int, List<int>>>>(json, options)
            ?? throw new InvalidOperationException("Arena level data could not be parsed.");

        return rawData.ToDictionary(
            editionEntry => editionEntry.Key,
            editionEntry => (IReadOnlyDictionary<int, IReadOnlyList<int>>)editionEntry.Value.ToDictionary(
                arenaEntry => arenaEntry.Key,
                arenaEntry => (IReadOnlyList<int>)arenaEntry.Value),
            StringComparer.OrdinalIgnoreCase);
    }
}
