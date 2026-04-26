using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Core.Models;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class CatchEligibilityServiceTests
{
    private const string GuildId = "guild-1";

    [Fact]
    public async Task CheckCatchAsync_ShouldBlockGermanNameWhenEvolutionLineWasAlreadyCaught()
    {
        var linkGroup = CreateLinkGroup("route 1", 1, "Ash", "Bulbasaur", isAlive: true);
        var service = CreateService(CreateRun(linkGroup));

        var result = await service.CheckCatchAsync(GuildId, "Bisasam");

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.Match);
        Assert.Equal("route 1", result.Match.Route);
        Assert.Equal("Ash", result.Match.PlayerName);
        Assert.Equal("Bulbasaur", result.Match.PokemonName);
        Assert.Equal("Team", result.Match.Status);
    }

    [Fact]
    public async Task CheckCatchAsync_ShouldBlockEnglishEvolutionWhenBaseFormWasAlreadyCaught()
    {
        var linkGroup = CreateLinkGroup("route 1", 1, "Ash", "Bulbasaur", isAlive: true);
        var service = CreateService(CreateRun(linkGroup));

        var result = await service.CheckCatchAsync(GuildId, "Venusaur");

        Assert.False(result.IsAllowed);
        Assert.Equal("Bulbasaur", result.Match?.PokemonName);
    }

    [Fact]
    public async Task CheckCatchAsync_ShouldConsiderDeadPokemonAsBlocked()
    {
        var linkGroup = CreateLinkGroup("route 2", 2, "Misty", "Charmander", isAlive: false);
        var service = CreateService(CreateRun(linkGroup));

        var result = await service.CheckCatchAsync(GuildId, "Charmeleon");

        Assert.False(result.IsAllowed);
        Assert.NotNull(result.Match);
        Assert.Equal("Charmander", result.Match.PokemonName);
        Assert.Equal("Dead", result.Match.Status);
    }

    [Fact]
    public async Task CheckCatchAsync_ShouldAllowPokemonWithoutEvolutionLineMatch()
    {
        var linkGroup = CreateLinkGroup("route 1", 1, "Ash", "Bulbasaur", isAlive: true);
        var service = CreateService(CreateRun(linkGroup));

        var result = await service.CheckCatchAsync(GuildId, "Squirtle");

        Assert.True(result.IsAllowed);
        Assert.Null(result.Match);
    }

    [Fact]
    public async Task CheckCatchAsync_ShouldReportUnclearPokemonName()
    {
        var service = CreateService(CreateRun());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.CheckCatchAsync(GuildId, "Missingno"));

        Assert.Contains("not found or is ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static CatchEligibilityService CreateService(SoulLinkRun run)
    {
        return new CatchEligibilityService(new StubRunService(run), new StubPokedexService());
    }

    private static SoulLinkRun CreateRun(params LinkGroup[] linkGroups)
    {
        var run = new SoulLinkRun
        {
            GuildId = GuildId,
            Name = "Test Run",
            Game = "red",
            Players = new List<RunPlayer>
            {
                new RunPlayer { UserId = 1, UserName = "Ash" },
                new RunPlayer { UserId = 2, UserName = "Misty" },
            },
            LinkGroups = linkGroups.ToList(),
        };

        if (linkGroups.FirstOrDefault(group => group.IsAlive) is { } aliveGroup)
        {
            run.ActiveLinks[0] = aliveGroup;
        }

        return run;
    }

    private static LinkGroup CreateLinkGroup(
        string route,
        ulong playerId,
        string playerName,
        string pokemonName,
        bool isAlive)
    {
        return new LinkGroup
        {
            Id = Guid.NewGuid(),
            Route = route,
            Entries = new List<LinkedPokemon>
            {
                new LinkedPokemon
                {
                    PlayerUserId = playerId,
                    PlayerName = playerName,
                    PokemonName = pokemonName,
                    IsAlive = isAlive,
                },
            },
        };
    }

    private sealed class StubRunService : IRunService
    {
        private readonly SoulLinkRun run;

        public StubRunService(SoulLinkRun run)
        {
            this.run = run;
        }

        public SoulLinkRun StartRun(string guildId, string name, string game, IReadOnlyList<RunPlayer> players)
        {
            throw new NotSupportedException();
        }

        public SoulLinkRun EndRun(string guildId, string? reason)
        {
            throw new NotSupportedException();
        }

        public LinkGroup RegisterCatch(
            string guildId,
            string route,
            ulong playerId,
            string playerName,
            string pokemon,
            IReadOnlyList<string> pokemonTypes)
        {
            throw new NotSupportedException();
        }

        public LinkGroup MarkRouteLost(
            string guildId,
            string route,
            string? reason,
            ulong? playerId,
            string? playerName)
        {
            throw new NotSupportedException();
        }

        public SoulLinkRun UseRoute(string guildId, string route, int position)
        {
            throw new NotSupportedException();
        }

        public SoulLinkRun SwapRoute(string guildId, string teamRoute, string boxRoute)
        {
            throw new NotSupportedException();
        }

        public LinkGroup RegisterDeath(
            string guildId,
            string route,
            string reason,
            ulong? playerId,
            string? playerName)
        {
            throw new NotSupportedException();
        }

        public SoulLinkRun GetActiveRun(string guildId)
        {
            return this.run;
        }

        public IReadOnlyList<SoulLinkRun> GetRuns(string guildId)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubPokedexService : IPokedexService
    {
        private static readonly IReadOnlyDictionary<string, string[]> Families =
            new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["Bulbasaur"] = new[] { "Bulbasaur", "Ivysaur", "Venusaur" },
                ["Bisasam"] = new[] { "Bulbasaur", "Ivysaur", "Venusaur" },
                ["Ivysaur"] = new[] { "Bulbasaur", "Ivysaur", "Venusaur" },
                ["Venusaur"] = new[] { "Bulbasaur", "Ivysaur", "Venusaur" },
                ["Charmander"] = new[] { "Charmander", "Charmeleon", "Charizard" },
                ["Charmeleon"] = new[] { "Charmander", "Charmeleon", "Charizard" },
                ["Squirtle"] = new[] { "Squirtle", "Wartortle", "Blastoise" },
            };

        public Task<PokedexEntry> GetPokedexEntryAsync(string pokemonName)
        {
            if (!Families.TryGetValue(pokemonName, out var family))
            {
                throw new InvalidOperationException("Pokémon was not found.");
            }

            var entry = new PokedexEntry
            {
                PokemonName = pokemonName,
                Rows = family.Select(name => new PokedexTableRow { PokemonName = name }).ToList(),
            };

            return Task.FromResult(entry);
        }
    }
}
