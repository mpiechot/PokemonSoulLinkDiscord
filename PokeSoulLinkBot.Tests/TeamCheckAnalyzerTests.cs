using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Core.Models;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class TeamCheckAnalyzerTests
{
    [Fact]
    public void Analyze_ShouldSelectLivingGroupsWithBroadTypeCoverage()
    {
        var run = new SoulLinkRun
        {
            Players = new List<RunPlayer>
            {
                new() { UserId = 1, UserName = "A" },
                new() { UserId = 2, UserName = "B" },
            },
            LinkGroups = new List<LinkGroup>
            {
                CreateGroup("route-a", (1, "fire"), (2, "water")),
                CreateGroup("route-b", (1, "grass"), (2, "electric")),
                CreateGroup("route-c", (1, "psychic"), (2, "ice")),
                CreateGroup("route-dead", (1, "dark"), (2, "ghost"), alive: false),
            },
        };

        var analysis = new TeamCheckAnalyzer().Analyze(run);

        Assert.Equal(new[] { "fire", "grass", "psychic" }, analysis.OptimalCoverage[0].Types);
        Assert.Equal(new[] { "electric", "ice", "water" }, analysis.OptimalCoverage[1].Types);
        Assert.DoesNotContain(analysis.OptimalLinkGroups, group => group.Route == "route-dead");
    }

    private static LinkGroup CreateGroup(string route, (ulong PlayerId, string Type) first, (ulong PlayerId, string Type) second, bool alive = true)
    {
        return new LinkGroup
        {
            Route = route,
            Entries = new List<LinkedPokemon>
            {
                new() { PlayerUserId = first.PlayerId, Types = new List<string> { first.Type }, IsAlive = alive },
                new() { PlayerUserId = second.PlayerId, Types = new List<string> { second.Type }, IsAlive = alive },
            },
        };
    }
}
