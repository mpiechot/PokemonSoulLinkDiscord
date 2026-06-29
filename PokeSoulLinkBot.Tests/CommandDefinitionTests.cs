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
            { new ArenaCommand(new StubArenaInfoService(), new EmbedFactory(), CreateImageFactory(), new StubGameDataCatalogService(), new StubRunService()), "arena", new[] { "number", "edition" } },
            { new CatchCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory(), new StubPokemonLookupService(), new StubGameDataCatalogService()), "catch", new[] { "route", "player", "pokemon" } },
            { new CatchCheckCommand(new StubCatchEligibilityService(), new EmbedFactory()), "catch-check", new[] { "pokemon" } },
            { new DeathCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory()), "death", new[] { "route", "reason", "player" } },
            { new HealthCommand(new StubBotHealthService(), new EmbedFactory()), "health", Array.Empty<string>() },
            { new PokedexCommand(new StubPokedexService(), new PokedexPresenter()), "pokedex", new[] { "name" } },
            { new RouteDeathCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory(), new StubGameDataCatalogService()), "route-death", new[] { "route", "reason", "player" } },
            { new RunEndCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory()), "run-end", new[] { "reason" } },
            { new RunStartCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory(), new StubGameDataCatalogService()), "run-start", new[] { "name", "edition", "player1", "player2", "player3" } },
            { new StatsCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory()), "stats", Array.Empty<string>() },
            { new StatusCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory(), new StubPokemonLookupService()), "status", Array.Empty<string>() },
            { new TeamCommand(new StubRunService(), new EmbedFactory()), "team", Array.Empty<string>() },
            { new SwapCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory()), "swap", new[] { "team-route", "box-route" } },
            { new UseCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory()), "use", new[] { "route", "position" } },
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
        var command = new ArenaCommand(new StubArenaInfoService(), new EmbedFactory(), CreateImageFactory(), new StubGameDataCatalogService(), new StubRunService());

        var definition = command.BuildDefinition();

        var slashDefinition = Assert.IsType<SlashCommandProperties>(definition);
        var editionOption = Assert.Single(
            slashDefinition.Options.Value,
            option => option.Name == "edition");

        Assert.True(editionOption.IsAutocomplete);
    }

    [Fact]
    public void RunStartCommandDefinition_ShouldSetRequiredAndAutocompleteFlags()
    {
        var command = new RunStartCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory(), new StubGameDataCatalogService());

        var options = GetOptions(command);

        AssertOption(options, "name", isRequired: true, isAutocomplete: false);
        AssertOption(options, "edition", isRequired: true, isAutocomplete: true);
        AssertOption(options, "player1", isRequired: true, isAutocomplete: false);
        AssertOption(options, "player2", isRequired: true, isAutocomplete: false);
        AssertOption(options, "player3", isRequired: true, isAutocomplete: false);
    }

    [Fact]
    public void CatchCommandDefinition_ShouldSetRequiredAndAutocompleteFlags()
    {
        var command = new CatchCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory(), new StubPokemonLookupService(), new StubGameDataCatalogService());

        var options = GetOptions(command);

        AssertOption(options, "route", isRequired: true, isAutocomplete: true);
        AssertOption(options, "player", isRequired: true, isAutocomplete: false);
        AssertOption(options, "pokemon", isRequired: true, isAutocomplete: false);
    }

    [Fact]
    public void RouteDeathCommandDefinition_ShouldSetRequiredAndAutocompleteFlags()
    {
        var command = new RouteDeathCommand(new StubRunService(), new EmbedFactory(), CreateImageFactory(), new StubGameDataCatalogService());

        var options = GetOptions(command);

        AssertOption(options, "route", isRequired: true, isAutocomplete: true);
        AssertOption(options, "reason", isRequired: false, isAutocomplete: false);
        AssertOption(options, "player", isRequired: false, isAutocomplete: false);
    }

    private static IReadOnlyCollection<ApplicationCommandOptionProperties> GetOptions(ISlashCommand command)
    {
        var slashDefinition = Assert.IsType<SlashCommandProperties>(command.BuildDefinition());

        return slashDefinition.Options.IsSpecified
            ? slashDefinition.Options.Value
            : Array.Empty<ApplicationCommandOptionProperties>();
    }

    private static void AssertOption(
        IReadOnlyCollection<ApplicationCommandOptionProperties> options,
        string optionName,
        bool isRequired,
        bool isAutocomplete)
    {
        var option = Assert.Single(options, candidate => candidate.Name == optionName);

        Assert.Equal(isRequired, option.IsRequired ?? false);
        Assert.Equal(isAutocomplete, option.IsAutocomplete);
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

        public LinkGroup RegisterDeath(
            string guildId,
            string route,
            string reason,
            ulong? playerId,
            string? playerName)
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

    private sealed class StubCatchEligibilityService : ICatchEligibilityService
    {
        public Task<CatchCheckResult> CheckCatchAsync(string guildId, string pokemonName)
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

    private sealed class StubBotHealthService : IBotHealthService
    {
        public Task<BotHealthReport> GetReportAsync(string guildId)
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
        public GameDataCatalogStatus GetStatus()
        {
            return new GameDataCatalogStatus();
        }

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
