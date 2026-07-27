using PokeSoulLinkBot.Core.Models;

namespace GameDataCatalogGenerator;

/// <summary>
/// Validates generated fallback catalogs before they are committed.
/// </summary>
public static class GameDataFallbackCatalogValidator
{
    /// <summary>
    /// The supported fallback catalog schema version.
    /// </summary>
    public const int CurrentSchemaVersion = 1;

    private const int MinimumEditionCount = 10;
    private const int MinimumRouteCount = 100;

    /// <summary>
    /// Validates the supplied catalog.
    /// </summary>
    /// <param name="catalog">The catalog to validate.</param>
    /// <returns>Validation errors, or an empty list when valid.</returns>
    public static IReadOnlyList<string> Validate(GameDataCatalog? catalog)
    {
        var errors = new List<string>();
        if (catalog is null)
        {
            errors.Add("Catalog is empty.");
            return errors;
        }

        if (catalog.SchemaVersion != CurrentSchemaVersion)
        {
            errors.Add(
                $"Catalog schema version must be {CurrentSchemaVersion}, but was {catalog.SchemaVersion}.");
        }

        if (catalog.RefreshedAtUtc == default)
        {
            errors.Add("Catalog refresh timestamp is missing.");
        }

        if (catalog.Editions.Count < MinimumEditionCount)
        {
            errors.Add(
                $"Catalog must contain at least {MinimumEditionCount} editions, but contains {catalog.Editions.Count}.");
        }

        int routeCount = catalog.Editions.Sum(edition => edition.Routes.Count);
        if (routeCount < MinimumRouteCount)
        {
            errors.Add(
                $"Catalog must contain at least {MinimumRouteCount} routes, but contains {routeCount}.");
        }

        ValidateEditions(catalog.Editions, errors);
        return errors;
    }

    private static void ValidateEditions(
        IReadOnlyList<GameEditionInfo> editions,
        ICollection<string> errors)
    {
        if (editions.Any(edition =>
            string.IsNullOrWhiteSpace(edition.Name) ||
            string.IsNullOrWhiteSpace(edition.DisplayName) ||
            edition.Routes.Count == 0))
        {
            errors.Add("Every edition must have a name, display name, and at least one route.");
        }

        if (editions
            .GroupBy(edition => edition.Name, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() > 1))
        {
            errors.Add("Edition names must be unique.");
        }

        foreach (GameEditionInfo edition in editions)
        {
            if (edition.Routes.Any(string.IsNullOrWhiteSpace))
            {
                errors.Add($"Routes for edition '{edition.Name}' must not be blank.");
            }

            if (edition.Routes
                .GroupBy(route => route, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() > 1))
            {
                errors.Add($"Routes for edition '{edition.Name}' must be unique.");
            }
        }
    }
}
