using System.Text;
using System.Text.Json;
using PokeSoulLinkBot.Core.Models;

namespace GameDataCatalogGenerator;

/// <summary>
/// Reads, validates, and atomically writes fallback catalog files.
/// </summary>
public static class GameDataFallbackCatalogFile
{
    private static readonly JsonSerializerOptions JsonOptions =
        new JsonSerializerOptions(JsonSerializerDefaults.Web);

    /// <summary>
    /// Loads a catalog file.
    /// </summary>
    /// <param name="filePath">The catalog path.</param>
    /// <returns>The loaded catalog.</returns>
    public static GameDataCatalog Load(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using FileStream stream = File.OpenRead(filePath);
        return JsonSerializer.Deserialize<GameDataCatalog>(stream, JsonOptions)
            ?? throw new InvalidDataException("The game-data catalog is empty.");
    }

    /// <summary>
    /// Validates a catalog file.
    /// </summary>
    /// <param name="filePath">The catalog path.</param>
    /// <returns>Validation errors, or an empty list for a valid file.</returns>
    public static IReadOnlyList<string> Validate(string filePath)
    {
        return GameDataFallbackCatalogValidator.Validate(Load(filePath));
    }

    /// <summary>
    /// Writes a catalog without exposing a partially written target file.
    /// </summary>
    /// <param name="filePath">The destination path.</param>
    /// <param name="catalog">The catalog to write.</param>
    public static void WriteAtomically(string filePath, GameDataCatalog catalog)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(catalog);

        string fullPath = Path.GetFullPath(filePath);
        string? directoryPath = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        string tempFilePath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            string json = GameDataFallbackCatalogSerializer.Serialize(catalog);
            File.WriteAllText(tempFilePath, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(tempFilePath, fullPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tempFilePath))
            {
                File.Delete(tempFilePath);
            }
        }
    }
}
