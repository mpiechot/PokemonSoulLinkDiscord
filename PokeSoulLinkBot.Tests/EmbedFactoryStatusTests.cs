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

        Assert.Contains("⚔️ Current Team", message, StringComparison.Ordinal);
        Assert.Contains("📦 Box", message, StringComparison.Ordinal);
        Assert.Contains("💀 Dead", message, StringComparison.Ordinal);
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
    public void CreateTeamMessage_ShouldHideDeadActiveLinks()
    {
        var run = CreateRun();
        var deadRoute = CreateLinkGroup("101", false, "Bisasam");
        run.LinkGroups.Add(deadRoute);
        run.ActiveLinks[0] = deadRoute;
        var embedFactory = new EmbedFactory();

        var message = embedFactory.CreateTeamMessage(run);

        Assert.DoesNotContain("101", message, StringComparison.Ordinal);
    }

    [Fact]
    public void CreateTeamAndStatusMessages_ShouldShowCurrentTeamPositionsInSlotOrder()
    {
        var run = CreateRun();
        var firstSlotRoute = CreateLinkGroup("102", true, "Pichu");
        var secondSlotRoute = CreateLinkGroup("101", true, "Bisasam");
        run.LinkGroups.AddRange(new[] { firstSlotRoute, secondSlotRoute });
        run.ActiveLinks[0] = firstSlotRoute;
        run.ActiveLinks[1] = secondSlotRoute;
        var embedFactory = new EmbedFactory();

        var teamMessage = embedFactory.CreateTeamMessage(run);
        var statusMessage = embedFactory.CreateStatusMessage(run);

        AssertCurrentTeamPositions(teamMessage);
        AssertCurrentTeamPositions(statusMessage);
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
    public void CreateCatchCheckEmbed_ShouldUseShortGermanAllowedOutput()
    {
        var result = new CatchCheckResult
        {
            RequestedPokemonName = "Raichu",
            IsAllowed = true,
        };
        var embedFactory = new EmbedFactory();

        var embed = embedFactory.CreateCatchCheckEmbed(result);

        Assert.Equal("Fang-Check", embed.Title);
        Assert.Equal("✅ **Raichu** darf gefangen werden.", embed.Description);
        Assert.Empty(embed.Fields);
    }

    [Fact]
    public void CreateCatchCheckEmbed_ShouldUseShortGermanBlockedOutput()
    {
        var result = new CatchCheckResult
        {
            RequestedPokemonName = "Raichu",
            IsAllowed = false,
            Match = new CatchCheckMatch
            {
                PokemonName = "Pikachu",
                Route = "route 3",
                PlayerName = "Misty",
                Status = "Dead",
            },
        };
        var embedFactory = new EmbedFactory();

        var embed = embedFactory.CreateCatchCheckEmbed(result);

        Assert.Equal("Fang-Check", embed.Title);
        Assert.Equal("⛔ **Raichu** ist gesperrt.", embed.Description);
        var field = Assert.Single(embed.Fields);
        Assert.Equal("Fund", field.Name);
        Assert.Equal("Pikachu · route 3 · Misty · Tot", field.Value);
    }

    [Fact]
    public void CreateStatsEmbed_ShouldIncludeRouteTeamReasonAndPlayerStatistics()
    {
        var run = CreateRun();
        var teamRoute = CreateLinkGroup("101", true, "Bisasam");
        var deadRoute = CreateLinkGroup("102", false, "Pichu");
        var deadEntry = Assert.Single(deadRoute.Entries);
        deadEntry.DeathReason = "Critical hit.";
        deadEntry.DiedAtUtc = DateTime.UtcNow;
        run.LinkGroups.AddRange(new[] { teamRoute, deadRoute });
        run.ActiveLinks[0] = teamRoute;
        run.CompletedArenas.Add(new CompletedArena
        {
            ArenaNumber = 1,
            Edition = "ruby",
            LeaderName = "Roxanne",
            Location = "Rustboro City",
            CompletedAtUtc = DateTime.UtcNow,
        });
        var embedFactory = new EmbedFactory();

        var embed = embedFactory.CreateStatsEmbed([run], "attachment://stats.png");

        Assert.Contains(embed.Fields, field => field.Name == "Routes" && field.Value.Contains("Caught: 2", StringComparison.Ordinal));
        Assert.Contains(embed.Fields, field => field.Name == "Team / Box" && field.Value.Contains("Team: 1/6", StringComparison.Ordinal));
        Assert.Contains(embed.Fields, field => field.Name == "Arena Progress" && field.Value.Contains("Completed: 1/8", StringComparison.Ordinal));
        Assert.Contains(embed.Fields, field => field.Name == "Deaths by Reason" && field.Value.Contains("Critical hit.: 1", StringComparison.Ordinal));
        Assert.Contains(embed.Fields, field => field.Name == "Player Stats" && field.Value.Contains("marpie1: 2 caught, 1 alive, 1 dead", StringComparison.Ordinal));
        Assert.Contains(embed.Fields, field => field.Name == "Death Log" && field.Value.Contains("Pichu (102) - Critical hit.", StringComparison.Ordinal));
    }

    [Fact]
    public void CreateArenaInfoEmbed_ShouldIncludeProgressStatus()
    {
        var embedFactory = new EmbedFactory();

        var embed = embedFactory.CreateArenaInfoEmbed(
            "ruby",
            1,
            "Roxanne",
            "Rustboro City",
            new[] { 14, 15 },
            "attachment://arena.png",
            isCompleted: true);

        Assert.Contains(embed.Fields, field => field.Name == "Progress" && field.Value == "Completed");
    }

    [Fact]
    public void CreateArenasOverview_ShouldUseCompactFullWidthTableWithStatusIcons()
    {
        var arenas = new[]
        {
            new ArenaInfo { ArenaNumber = 1, LeaderName = "Roxanne", Levels = new[] { 14, 15 } },
            new ArenaInfo { ArenaNumber = 2, LeaderName = "Brawly", Levels = new[] { 17, 18 } },
            new ArenaInfo
            {
                ArenaNumber = 3,
                LeaderName = "An Arena Leader With A Very Long Name",
                Levels = new[] { 22, 20, 23 },
            },
        };
        var completedArenaNumbers = new HashSet<int> { 1 };
        var embedFactory = new EmbedFactory();

        var embed = embedFactory.CreateArenasOverviewEmbed(
            "Ruby",
            arenas,
            completedArenaNumbers,
            "attachment://arena.png");
        var message = embedFactory.CreateArenasOverviewMessage(arenas, completedArenaNumbers);
        var tableRows = message
            .Split(Environment.NewLine)
            .Where(line => line.Contains(" | ", StringComparison.Ordinal));

        Assert.Empty(embed.Fields);
        Assert.Contains("✅", message, StringComparison.Ordinal);
        Assert.Contains("➡️", message, StringComparison.Ordinal);
        Assert.Contains("⬜", message, StringComparison.Ordinal);
        Assert.Contains("Roxanne", message, StringComparison.Ordinal);
        Assert.Contains("14, 15", message, StringComparison.Ordinal);
        Assert.All(
            tableRows,
            row => Assert.True(row.Trim('`').Length <= 49, $"Arena table row is too long: {row}"));
    }

    [Fact]
    public void CreateArenaCompletedEmbed_ShouldIncludeRunAndProgress()
    {
        var run = CreateRun();
        var completedArena = new CompletedArena
        {
            ArenaNumber = 1,
            Edition = "ruby",
            LeaderName = "Roxanne",
            Location = "Rustboro City",
            CompletedAtUtc = DateTime.UtcNow,
        };
        run.CompletedArenas.Add(completedArena);
        var embedFactory = new EmbedFactory();

        var embed = embedFactory.CreateArenaCompletedEmbed(completedArena, run, "attachment://arena.png");

        Assert.Equal("Arena Completed", embed.Title);
        Assert.Contains(embed.Fields, field => field.Name == "Run" && field.Value == "Ruby");
        Assert.Contains(embed.Fields, field => field.Name == "Progress" && field.Value == "1/8");
    }

    [Fact]
    public void CreateStatusMessages_ShouldKeepAllTableRowsWithoutContinuationSections()
    {
        var run = CreateRun();
        for (var routeIndex = 1; routeIndex <= 80; routeIndex++)
        {
            run.LinkGroups.Add(CreateLinkGroup($"route-{routeIndex:000}", true, $"Pokemon-{routeIndex:000}"));
        }

        var embedFactory = new EmbedFactory();

        var messages = embedFactory.CreateStatusMessages(run);
        var fullMessage = string.Join(Environment.NewLine, messages);

        Assert.Contains("**⚔️ Current Team**", fullMessage, StringComparison.Ordinal);
        Assert.Contains("**📦 Box**", fullMessage, StringComparison.Ordinal);
        Assert.Contains("**💀 Dead**", fullMessage, StringComparison.Ordinal);
        Assert.Contains("```", fullMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("(continued)", fullMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("...```", fullMessage, StringComparison.Ordinal);
        Assert.All(messages, message => Assert.InRange(message.Length, 1, 2000));
        for (var routeIndex = 1; routeIndex <= 80; routeIndex++)
        {
            Assert.Contains($"route-{routeIndex:000}", fullMessage, StringComparison.Ordinal);
        }
    }

    private static SoulLinkRun CreateRun()
    {
        return CreateRunWithPlayers("marpie1");
    }

    private static SoulLinkRun CreateRunWithPlayers(params string[] playerNames)
    {
        var run = new SoulLinkRun
        {
            GuildId = "guild-1",
            Name = "Ruby",
            Game = "ruby",
            StartedAtUtc = DateTime.UtcNow,
        };

        for (var playerIndex = 0; playerIndex < playerNames.Length; playerIndex++)
        {
            run.Players.Add(new RunPlayer
            {
                UserId = (ulong)(playerIndex + 1),
                UserName = playerNames[playerIndex],
            });
        }

        return run;
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

    private static void AssertCurrentTeamPositions(string message)
    {
        Assert.Contains("1: 102", message, StringComparison.Ordinal);
        Assert.Contains("2: 101", message, StringComparison.Ordinal);
        Assert.True(
            message.IndexOf("1: 102", StringComparison.Ordinal) <
            message.IndexOf("2: 101", StringComparison.Ordinal));
    }
}
