using Discord;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Bot.Commands;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Presentation;
using PokeSoulLinkBot.Core.Models;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class CommandDefinitionTests
{
    public static TheoryData<ISlashCommand, string, string[]> Commands =>
        new()
        {
            { new ArenaCommand(new StubArenaInfoService(), new StubGameDataCatalogService(), new StubRunService()), "arena", new[] { "number", "edition" } },
            { new CatchCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory(), new StubPokemonLookupService(), new StubGameDataCatalogService()), "catch", new[] { "route", "player", "pokemon" } },
            { new DeathCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory()), "death", new[] { "route" } },
            { new PokedexCommand(new StubPokedexService(), new PokedexPresenter()), "pokedex", new[] { "name" } },
            { new RouteDeathCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory(), new StubGameDataCatalogService()), "route-death", new[] { "route", "reason", "player" } },
            { new RunEndCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory()), "run-end", new[] { "reason" } },
            { new RunStartCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory(), new StubGameDataCatalogService()), "run-start", new[] { "name", "edition", "player1", "player2", "player3" } },
            { new StatsCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory()), "stats", Array.Empty<string>() },
            { new StatusCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory(), new StubPokemonLookupService()), "status", Array.Empty<string>() },
            { new TeamCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory()), "team", Array.Empty<string>() },
            { new SwapCommand(new StubRunService(), new EmbedFactory()), "swap", new[] { "team-route", "box-route" } },
        };

    [Theory]
    [MemberData(nameof(Commands))]
    public void BuildDefinition_ShouldUseCommandNameAndExpectedOptions(
        ISlashCommand command,
        string expectedName,
        string[] expectedOptions)
    {
        var definition = command.BuildDefinition();

        Assert.Equal(expectedName, command.CommandName);
        Assert.Equal(expectedName, definition.Name.Value);
        var slashDefinition = Assert.IsType<SlashCommandProperties>(definition);
        var optionNames = slashDefinition.Options.IsSpecified
            ? slashDefinition.Options.Value.Select(option => option.Name).ToArray()
            : Array.Empty<string>();

        Assert.Equal(expectedOptions, optionNames);
    }

    [Fact]
    public void ArenaCommandDefinition_ShouldEnableAutocompleteForEditionOption()
    {
        var command = new ArenaCommand(new StubArenaInfoService(), new StubGameDataCatalogService(), new StubRunService());

        var definition = command.BuildDefinition();

        var slashDefinition = Assert.IsType<SlashCommandProperties>(definition);
        var editionOption = Assert.Single(
            slashDefinition.Options.Value,
            option => option.Name == "edition");

        Assert.True(editionOption.IsAutocomplete);
    }

    private static EmbedImageFactory CreateImageFactory()
    {
        return new EmbedImageFactory(AppContext.BaseDirectory);
    }

    private sealed class StubRunService : IRunService
    {
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

        public LinkGroup RegisterDeath(string guildId, string pokemon)
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

        public SoulLinkRun GetActiveRun(string guildId)
        {
            throw new NotSupportedException();
        }

        public IReadOnlyList<SoulLinkRun> GetRuns(string guildId)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubPokemonLookupService : IPokemonLookupService
    {
        public Task<PokemonInfo?> GetPokemonInfoAsync(string pokemonName)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubPokedexService : IPokedexService
    {
        public Task<PokedexEntry> GetPokedexEntryAsync(string pokemonName)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubArenaInfoService : IArenaInfoService
    {
        public Task<ArenaInfo> GetArenaInfoAsync(string edition, int arenaNumber)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class StubGameDataCatalogService : IGameDataCatalogService
    {
        public Task InitializeAsync()
        {
            return Task.CompletedTask;
        }

        public Task<IReadOnlyCollection<GameEditionInfo>> GetEditionsAsync()
        {
            return Task.FromResult<IReadOnlyCollection<GameEditionInfo>>(Array.Empty<GameEditionInfo>());
        }

        public Task<IReadOnlyCollection<string>> GetRoutesAsync(string edition)
        {
            return Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>());
        }
    }
}
