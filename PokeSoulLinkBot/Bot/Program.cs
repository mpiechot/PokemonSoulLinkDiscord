using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Bot;
using PokeSoulLinkBot.Bot.Commands;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Handlers;
using PokeSoulLinkBot.Bot.Presentation;
using PokeSoulLinkBot.Infrastructure.Persistence;
using Serilog;
using Serilog.Events;

internal sealed class Program
{
    private Program()
    {
    }

    public static async Task Main()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        var configuration = new ConfigurationBuilder()
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();

        var token = configuration["DISCORD_BOT_TOKEN"];

        if (string.IsNullOrWhiteSpace(token))
        {
            Log.Fatal("DISCORD_BOT_TOKEN wurde nicht gesetzt.");
            throw new InvalidOperationException("DISCORD_BOT_TOKEN wurde nicht gesetzt.");
        }

        Log.Information("Starting PokeSoulLinkBot.");

        var socketConfig = new DiscordSocketConfig()
        {
            GatewayIntents = GatewayIntents.Guilds,
        };

        var client = new DiscordSocketClient(socketConfig);

        var filePath = Path.Combine(AppContext.BaseDirectory, "Data", "runs.json");
        var gameDataCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PokeSoulLinkBot",
            "Data",
            "game-data-catalog.json");
        var resourcesDirectoryPath = Path.Combine(AppContext.BaseDirectory, "Resources");
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
        };

        var pokemonNameResolver = new PokeApiPokemonNameResolver(httpClient);
        var pokemonLookupService = new PokeApiPokemonLookupService(httpClient, pokemonNameResolver);
        var pokedexService = new PokeApiPokedexService(httpClient, pokemonNameResolver);
        var pokedexPresenter = new PokedexPresenter();
        var arenaInfoService = new PokemonDbArenaInfoService(httpClient);
        var gameDataCatalogService = new PokeApiGameDataCatalogService(httpClient, gameDataCachePath);

        var runStore = new RunStore(filePath);
        var runService = new RunService(runStore);
        var catchEligibilityService = new CatchEligibilityService(runService, pokedexService);
        var embedFactory = new EmbedFactory();
        var embedImageFactory = new EmbedImageFactory(resourcesDirectoryPath);

        var commands = new List<ISlashCommand>
        {
            new RunStartCommand(runService, embedFactory, embedImageFactory, gameDataCatalogService),
            new RunEndCommand(runService, embedFactory, embedImageFactory),
            new CatchCommand(runService, embedFactory, embedImageFactory, pokemonLookupService, gameDataCatalogService),
            new CatchCheckCommand(catchEligibilityService, embedFactory),
            new DeathCommand(runService, embedFactory, embedImageFactory),
            new RouteDeathCommand(runService, embedFactory, embedImageFactory, gameDataCatalogService),
            new StatusCommand(runService, embedFactory, embedImageFactory, pokemonLookupService),
            new StatsCommand(runService, embedFactory, embedImageFactory),
            new TeamCommand(runService, embedFactory),
            new SwapCommand(runService, embedFactory, embedImageFactory),
            new UseCommand(runService, embedFactory, embedImageFactory),
            new PokedexCommand(pokedexService, pokedexPresenter),
            new ArenaCommand(arenaInfoService, embedFactory, embedImageFactory, gameDataCatalogService, runService),
        };

        var slashCommandRouter = new SlashCommandRouter(commands, embedFactory);
        var readyStartupTaskRunner = new ReadyStartupTaskRunner(RegisterCommandsAfterReadyAsync);

        client.Log += OnLogAsync;
        client.Ready += readyStartupTaskRunner.HandleReadyAsync;
        client.SlashCommandExecuted += slashCommandRouter.HandleAsync;
        client.AutocompleteExecuted += slashCommandRouter.HandleAutocompleteAsync;

        await client.LoginAsync(TokenType.Bot, token);
        Log.Information("Discord login completed.");
        await client.StartAsync();
        Log.Information("Discord client started.");
        await Task.Delay(Timeout.Infinite);

        async Task RegisterCommandsAfterReadyAsync()
        {
            try
            {
                var definitions = slashCommandRouter.GetDefinitions();
                foreach (var definition in definitions)
                {
                    Log.Debug("Prepared slash command definition {CommandName}.", definition.Name.Value);
                }

                await RegisterSlashCommandsAsync(definitions);

                Log.Information("Initializing game data catalog.");
                await gameDataCatalogService.InitializeAsync();
                Log.Information("Game data catalog initialization completed.");

                Log.Information("Warming arena information.");
                await arenaInfoService.WarmUpKnownEditionsAsync();
                Log.Information("Arena information warmup completed.");
            }
            catch (Exception exception)
            {
                Log.Error(exception, "Ready startup failed.");
            }
        }

        async Task RegisterSlashCommandsAsync(IReadOnlyCollection<ApplicationCommandProperties> definitions)
        {
            var commandDefinitions = definitions.ToArray();

            Log.Information("Registering {CommandCount} global slash commands.", commandDefinitions.Length);
            await client.BulkOverwriteGlobalApplicationCommandsAsync(commandDefinitions);
            Log.Information("Registered {CommandCount} global slash commands.", commandDefinitions.Length);

            foreach (var guild in client.Guilds)
            {
                Log.Information(
                    "Registering {CommandCount} slash commands for guild {GuildName} ({GuildId}).",
                    commandDefinitions.Length,
                    guild.Name,
                    guild.Id);

                await guild.BulkOverwriteApplicationCommandAsync(commandDefinitions);

                Log.Information(
                    "Registered {CommandCount} slash commands for guild {GuildName} ({GuildId}).",
                    commandDefinitions.Length,
                    guild.Name,
                    guild.Id);
            }
        }
    }

    private static Task OnLogAsync(LogMessage logMessage)
    {
        var level = MapDiscordLogLevel(logMessage.Severity);
        var message = string.IsNullOrWhiteSpace(logMessage.Message)
            ? "Discord log event without message."
            : logMessage.Message;

        if (logMessage.Exception is not null)
        {
            Log.Write(level, logMessage.Exception, "Discord {Source}: {Message}", logMessage.Source, message);
            return Task.CompletedTask;
        }

        Log.Write(level, "Discord {Source}: {Message}", logMessage.Source, message);

        return Task.CompletedTask;
    }

    private static LogEventLevel MapDiscordLogLevel(LogSeverity severity)
    {
        return severity switch
        {
            LogSeverity.Critical => LogEventLevel.Fatal,
            LogSeverity.Error => LogEventLevel.Error,
            LogSeverity.Warning => LogEventLevel.Warning,
            LogSeverity.Info => LogEventLevel.Information,
            LogSeverity.Verbose => LogEventLevel.Verbose,
            LogSeverity.Debug => LogEventLevel.Debug,
            _ => LogEventLevel.Information,
        };
    }
}
