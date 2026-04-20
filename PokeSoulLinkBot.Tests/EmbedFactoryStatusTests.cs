using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Core.Models;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class EmbedFactoryStatusTests
{
    [Fact]
    public void CreateStatusMessage_ShouldGroupRoutesByCurrentTeamBoxAndDead()
    {
        var run = CreateRun();
        var teamRoute = CreateLinkGroup("101", true, "Bisasam");
        var boxRoute = CreateLinkGroup("102", true, "Pichu");
        var deadRoute = CreateLinkGroup("103", false, "Taubsi");
        run.LinkGroups.AddRange(new[] { teamRoute, boxRoute, deadRoute });
        run.ActiveLinks[0] = teamRoute;
        var embedFactory = new EmbedFactory();

        var message = embedFactory.CreateStatusMessage(run);

        Assert.Contains("Run Status", message, StringComparison.Ordinal);
        Assert.Contains("Run: **Ruby**", message, StringComparison.Ordinal);
        Assert.Contains("Edition: **ruby**", message, StringComparison.Ordinal);
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
    public void CreateTeamMessage_ShouldUseCurrentActiveLinks()
    {
        var run = CreateRun();
        var teamRoute = CreateLinkGroup("101", true, "Bisasam");
        var boxRoute = CreateLinkGroup("102", true, "Pichu");
        run.LinkGroups.AddRange(new[] { teamRoute, boxRoute });
        run.ActiveLinks[0] = boxRoute;
        var embedFactory = new EmbedFactory();

        var message = embedFactory.CreateTeamMessage(run);

        Assert.Contains("Active Team", message, StringComparison.Ordinal);
        Assert.Contains("102", message, StringComparison.Ordinal);
        Assert.DoesNotContain("101", message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateDeathRegisteredEmbed_ShouldIncludeReasonAndCausingPlayer()
    {
        var linkGroup = CreateLinkGroup("101", false, "Bisasam");
        var entry = Assert.Single(linkGroup.Entries);
        entry.DeathReason = "Critical hit.";
        entry.DeathCausedByPlayerName = "bene";
        var embedFactory = new EmbedFactory();

        var embed = embedFactory.CreateDeathRegisteredEmbed(linkGroup, "attachment://death.png");

        Assert.Contains(embed.Fields, field => field.Name == "Reason" && field.Value == "Critical hit.");
        Assert.Contains(embed.Fields, field => field.Name == "Player" && field.Value == "bene");
        Assert.Contains(embed.Fields, field => field.Name == "Affected Pokemon" && field.Value.Contains("Bisasam", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateStatusMessage_ShouldKeepTableCodeBlocksDiscordCompatible()
    {
        var run = CreateRun();
        for (var routeIndex = 1; routeIndex <= 80; routeIndex++)
        {
            run.LinkGroups.Add(CreateLinkGroup($"route-{routeIndex:000}", true, $"Pokemon-{routeIndex:000}"));
        }

        var embedFactory = new EmbedFactory();

        var message = embedFactory.CreateStatusMessage(run);

        Assert.True(message.Length <= 2000);
        Assert.Contains("Box", message, StringComparison.Ordinal);
        Assert.Contains("...```", message, StringComparison.Ordinal);
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
