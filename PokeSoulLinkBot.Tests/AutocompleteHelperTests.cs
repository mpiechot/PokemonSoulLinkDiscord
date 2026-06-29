using PokeSoulLinkBot.Bot.Helpers;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class AutocompleteHelperTests
{
    [Fact]
    public void CreateResults_ShouldOrderExactPrefixContainsAndFuzzyMatches()
    {
        var values = new[]
        {
            "Petalburg",
            "Petalburg Woods",
            "Route 101",
            "Route 110",
            "Rusturf Tunnel",
            "Petalburg City",
        };

        var results = AutocompleteHelper.CreateResults(values, "petalburg")
            .Select(result => result.Name)
            .ToList();

        Assert.Equal("Petalburg", results[0]);
        Assert.Equal("Petalburg City", results[1]);
        Assert.Equal("Petalburg Woods", results[2]);
    }

    [Fact]
    public void CreateResults_ShouldReturnStableDistinctResultsForEmptyInput()
    {
        var values = new[]
        {
            "Route 102",
            "route 102",
            "Route 101",
            string.Empty,
            "  Route 103  ",
        };

        var results = AutocompleteHelper.CreateResults(values, string.Empty)
            .Select(result => result.Name)
            .ToList();

        Assert.Equal(["Route 101", "Route 102", "Route 103"], results);
    }

    [Fact]
    public void CreateResults_ShouldHandlePartialRouteInput()
    {
        var values = new[] { "Route 101", "Petalburg Woods", "Rusturf Tunnel" };

        var results = AutocompleteHelper.CreateResults(values, "101")
            .Select(result => result.Name)
            .ToList();

        Assert.Equal(["Route 101"], results);
    }

    [Fact]
    public void CreateResults_ShouldHandleSmallTypos()
    {
        var values = new[] { "Pikachu", "Raichu" };

        var results = AutocompleteHelper.CreateResults(values, "pikchu")
            .Select(result => result.Name)
            .ToList();

        Assert.Equal("Pikachu", Assert.Single(results));
    }

    [Fact]
    public void CreateResults_ShouldNormalizeDiacritics()
    {
        var values = new[] { "Route 1", "Ewigenau", "Écruteak City" };

        var results = AutocompleteHelper.CreateResults(values, "ecruteak")
            .Select(result => result.Name)
            .ToList();

        Assert.Equal("Écruteak City", Assert.Single(results));
    }
}
