using System.Text.Json;
using PokeSoulLinkBot.Core.Models;

namespace GameDataCatalogGenerator;

/// <summary>
/// Serializes fallback catalogs in the repository's canonical JSON format.
/// </summary>
public static class GameDataFallbackCatalogSerializer
{
    private static readonly JsonSerializerOptions JsonOptions =
        new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
        };

    /// <summary>
    /// Serializes the supplied catalog deterministically.
    /// </summary>
    /// <param name="catalog">The canonical catalog.</param>
    /// <returns>UTF-8-compatible JSON text with CRLF line endings.</returns>
    public static string Serialize(GameDataCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);

        string json = JsonSerializer.Serialize(catalog, JsonOptions);
        return json.ReplaceLineEndings("\r\n") + "\r\n";
    }
}
