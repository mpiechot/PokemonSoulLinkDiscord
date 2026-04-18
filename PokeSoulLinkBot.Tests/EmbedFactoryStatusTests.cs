using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Core.Models;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class EmbedFactoryStatusTests
{
    [Fact]
    public void CreateStatusEmbed_ShouldGroupRoutesByCurrentTeamBoxAndDead()
    {
        var run = CreateRun();
        var teamRoute = CreateLinkGroup("101", true, "Bisasam");
        var boxRoute = CreateLinkGroup("102", true, "Pichu");
        var deadRoute = CreateLinkGroup("103", false, "Taubsi");
        run.LinkGroups.AddRange(new[] { teamRoute, boxRoute, deadRoute });
        run.ActiveLinks[0] = teamRoute;
        var embedFactory = new EmbedFactory();

        var embed = embedFactory.CreateStatusEmbed(run);
        var message = string.Join(
            Environment.NewLine,
            embed.Fields.Select(field => $"{field.Name}{Environment.NewLine}{field.Value}"));

        Assert.Equal("Run Status", embed.Title);
        Assert.Contains(embed.Fields, field => field.Name == "Run" && field.Value == "Ruby");
        Assert.Contains(embed.Fields, field => field.Name == "Edition" && field.Value == "ruby");
        Assert.Contains("Current Team", message, StringComparison.Ordinal);
        Assert.Contains("Box", message, StringComparison.Ordinal);
        Assert.Contains("Dead", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Alive", message, StringComparison.Ordinal);
        Assert.Contains("101", message, StringComparison.Ordinal);
        Assert.Contains("102", message, StringComparison.Ordinal);
        Assert.Contains("103", message, StringComparison.Ordinal);
        Assert.True(message.IndexOf("101", StringComparison.Ordinal) < message.IndexOf("102", StringComparison.Ordinal));
        Assert.True(message.IndexOf("102", StringComparison.Ordinal) < message.IndexOf("103", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateTeamEmbed_ShouldUseCurrentActiveLinks()
    {
        var run = CreateRun();
        var teamRoute = CreateLinkGroup("101", true, "Bisasam");
        var boxRoute = CreateLinkGroup("102", true, "Pichu");
        run.LinkGroups.AddRange(new[] { teamRoute, boxRoute });
        run.ActiveLinks[0] = boxRoute;
        var embedFactory = new EmbedFactory();

        var embed = embedFactory.CreateTeamEmbed(run);
        var message = string.Join(
            Environment.NewLine,
            embed.Fields.Select(field => $"{field.Name}{Environment.NewLine}{field.Value}"));

        Assert.Equal("Active Team", embed.Title);
        Assert.Contains("102", message, StringComparison.Ordinal);
        Assert.DoesNotContain("101", message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateStatusEmbed_ShouldKeepTableFieldsDiscordCompatible()
    {
        var run = CreateRun();
        for (var routeIndex = 1; routeIndex <= 80; routeIndex++)
        {
            run.LinkGroups.Add(CreateLinkGroup($"route-{routeIndex:000}", true, $"Pokemon-{routeIndex:000}"));
        }

        var embedFactory = new EmbedFactory();

        var embed = embedFactory.CreateStatusEmbed(run);

        Assert.All(embed.Fields, field => Assert.True(field.Value.Length <= 1024));
        Assert.Contains(embed.Fields, field => field.Name == "Box" && field.Value.EndsWith("...```", StringComparison.Ordinal));
    }

    private static SoulLinkRun CreateRun()
    {
        return new SoulLinkRun
        {
            GuildId = "guild-1",
            Name = "Ruby",
            Game = "ruby",
            StartedAtUtc = DateTime.UtcNow,
            Players = new List<RunPlayer>
            {
                new RunPlayer { UserId = 1, UserName = "marpie1" },
            },
        };
    }

    private static LinkGroup CreateLinkGroup(string route, bool isAlive, string pokemonName)
    {
        return new LinkGroup
        {
            Id = Guid.NewGuid(),
            Route = route,
            Entries = new List<LinkedPokemon>
            {
                new LinkedPokemon
                {
                    PlayerUserId = 1,
                    PlayerName = "marpie1",
                    PokemonName = pokemonName,
                    IsAlive = isAlive,
                },
            },
        };
    }
}
