using GameDataCatalogGenerator;
using PokeSoulLinkBot.Core.Models;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class GameDataFallbackCatalogToolTests
{
    [Fact]
    public void Canonicalize_ShouldSortDeduplicateAndStampCatalog()
    {
        DateTime refreshedAtUtc = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc);
        var source = new GameDataCatalog
        {
            Editions =
            [
                new GameEditionInfo
                {
                    Name = "sapphire",
                    DisplayName = " Sapphire ",
                    Routes = ["Route 102", "route 101", "Route 102"],
                },
                new GameEditionInfo
                {
                    Name = "ruby",
                    DisplayName = "Ruby",
                    Routes = ["Route 103"],
                },
                new GameEditionInfo
                {
                    Name = "SAPPHIRE",
                    DisplayName = "Sapphire",
                    Routes = ["Route 100"],
                },
            ],
        };

        GameDataCatalog catalog = GameDataFallbackCatalogCanonicalizer.Canonicalize(source, refreshedAtUtc);

        Assert.Equal(1, catalog.SchemaVersion);
        Assert.Equal(refreshedAtUtc, catalog.RefreshedAtUtc);
        Assert.Equal(["ruby", "sapphire"], catalog.Editions.Select(edition => edition.Name));
        Assert.Equal(
            ["Route 100", "route 101", "Route 102"],
            catalog.Editions[1].Routes);
    }

    [Fact]
    public void Validate_ShouldRejectEmptyIncompatibleAndIncompleteCatalog()
    {
        var catalog = new GameDataCatalog
        {
            SchemaVersion = 999,
            RefreshedAtUtc = default,
            Editions =
            [
                new GameEditionInfo
                {
                    Name = "ruby",
                    DisplayName = "Ruby",
                    Routes = ["Route 101"],
                },
            ],
        };

        IReadOnlyList<string> errors = GameDataFallbackCatalogValidator.Validate(catalog);

        Assert.Contains(errors, error => error.Contains("schema", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("timestamp", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("edition", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(errors, error => error.Contains("route", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_ShouldAcceptCompleteCanonicalCatalog()
    {
        var catalog = CreateCompleteCatalog();

        IReadOnlyList<string> errors = GameDataFallbackCatalogValidator.Validate(catalog);

        Assert.Empty(errors);
    }

    [Fact]
    public void Serialize_ShouldProduceStableCamelCaseJsonWithCrlf()
    {
        var catalog = CreateCompleteCatalog();

        string firstJson = GameDataFallbackCatalogSerializer.Serialize(catalog);
        string secondJson = GameDataFallbackCatalogSerializer.Serialize(catalog);

        Assert.Equal(firstJson, secondJson);
        Assert.Contains("\"schemaVersion\": 1", firstJson, StringComparison.Ordinal);
        Assert.Contains("\"refreshedAtUtc\":", firstJson, StringComparison.Ordinal);
        Assert.EndsWith("\r\n", firstJson, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", firstJson.Replace("\r\n", string.Empty), StringComparison.Ordinal);
    }

    private static GameDataCatalog CreateCompleteCatalog()
    {
        return new GameDataCatalog
        {
            SchemaVersion = 1,
            RefreshedAtUtc = new DateTime(2026, 7, 27, 12, 0, 0, DateTimeKind.Utc),
            Editions = Enumerable.Range(1, 10)
                .Select(edition => new GameEditionInfo
                {
                    Name = $"edition-{edition:00}",
                    DisplayName = $"Edition {edition:00}",
                    Routes = Enumerable.Range(1, 10)
                        .Select(route => $"Route {route:00}")
                        .ToList(),
                })
                .ToList(),
        };
    }
}
