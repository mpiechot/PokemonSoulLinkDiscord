using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Core.Models;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class EmbedFactoryHealthTests
{
    [Fact]
    public void CreateHealthEmbed_ShouldIncludeShareableDiagnosticSummary()
    {
        var report = new BotHealthReport
        {
            DiscordConnectionState = "Connected",
            DiscordLatencyMilliseconds = 23,
            GuildCount = 1,
            ActiveRun = new ActiveRunHealthStatus
            {
                Name = "Run 4",
                Game = "Rubin",
                PlayerCount = 3,
                LinkGroupCount = 12,
                ActiveTeamCount = 6,
                DeadGroupCount = 2,
                LostRouteCount = 1,
            },
            GameDataCatalog = new GameDataCatalogStatus
            {
                IsReady = true,
                Source = "cache",
                EditionCount = 42,
                RouteCount = 900,
            },
            PokemonDataCache = new PokemonDataCacheStatus
            {
                IsLoaded = true,
                Version = 1,
                NameIndexCount = 1000,
                PokemonInfoCount = 25,
                PokedexEntryCount = 8,
            },
            RecentEvents = new[]
            {
                new DiagnosticEvent
                {
                    OccurredAtUtc = new DateTimeOffset(2026, 6, 29, 18, 0, 0, TimeSpan.Zero),
                    Severity = "Error",
                    Source = "SlashCommandRouter",
                    Message = "Slash command failed.",
                    CommandName = "status",
                    Parameters = "none",
                    ExceptionType = "InvalidOperationException",
                    ExceptionMessage = "No active run.",
                    ElapsedMilliseconds = 17,
                },
            },
        };
        var embedFactory = new EmbedFactory();

        var embed = embedFactory.CreateHealthEmbed(report);

        Assert.Equal("Bot Diagnostics", embed.Title);
        Assert.Contains(embed.Fields, field => field.Name == "Discord" && field.Value.Contains("Connected", StringComparison.Ordinal));
        Assert.Contains(embed.Fields, field => field.Name == "Active Run" && field.Value.Contains("Run 4", StringComparison.Ordinal));
        Assert.Contains(embed.Fields, field => field.Name == "Data" && field.Value.Contains("Names/info/dex: 1000/25/8", StringComparison.Ordinal));
        Assert.Contains(embed.Fields, field => field.Name == "Recent Diagnostics" && field.Value.Contains("/status", StringComparison.Ordinal));
        Assert.Contains(embed.Fields, field => field.Name == "Recent Diagnostics" && field.Value.Contains("No active run.", StringComparison.Ordinal));
    }
}
