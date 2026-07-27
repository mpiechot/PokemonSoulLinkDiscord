using PokeSoulLinkBot.Core.Models;

namespace GameDataCatalogGenerator;

/// <summary>
/// Creates a deterministic fallback catalog from refreshed game data.
/// </summary>
public static class GameDataFallbackCatalogCanonicalizer
{
    /// <summary>
    /// Canonicalizes the supplied catalog.
    /// </summary>
    /// <param name="source">The source catalog.</param>
    /// <param name="refreshedAtUtc">The timestamp to persist.</param>
    /// <returns>The canonical catalog.</returns>
    public static GameDataCatalog Canonicalize(GameDataCatalog source, DateTime refreshedAtUtc)
    {
        ArgumentNullException.ThrowIfNull(source);

        DateTime normalizedTimestamp = refreshedAtUtc.Kind == DateTimeKind.Utc
            ? refreshedAtUtc
            : refreshedAtUtc.ToUniversalTime();

        List<GameEditionInfo> editions = source.Editions
            .Where(edition => !string.IsNullOrWhiteSpace(edition.Name))
            .GroupBy(edition => NormalizeName(edition.Name), StringComparer.OrdinalIgnoreCase)
            .Select(group => new GameEditionInfo
            {
                Name = group.Key,
                DisplayName = group
                    .Select(edition => edition.DisplayName?.Trim())
                    .Where(displayName => !string.IsNullOrWhiteSpace(displayName))
                    .OrderBy(displayName => displayName, StringComparer.Ordinal)
                    .FirstOrDefault() ?? group.Key,
                Routes = group
                    .SelectMany(edition => edition.Routes)
                    .Where(route => !string.IsNullOrWhiteSpace(route))
                    .Select(route => route.Trim())
                    .GroupBy(route => route, StringComparer.OrdinalIgnoreCase)
                    .Select(routeGroup => routeGroup.OrderBy(route => route, StringComparer.Ordinal).First())
                    .OrderBy(route => route, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(route => route, StringComparer.Ordinal)
                    .ToList(),
            })
            .Where(edition => edition.Routes.Count > 0)
            .OrderBy(edition => edition.Name, StringComparer.Ordinal)
            .ToList();

        return new GameDataCatalog
        {
            SchemaVersion = GameDataFallbackCatalogValidator.CurrentSchemaVersion,
            RefreshedAtUtc = normalizedTimestamp,
            Editions = editions,
        };
    }

    private static string NormalizeName(string name)
    {
        return name.Trim().ToLowerInvariant().Replace(' ', '-');
    }
}
