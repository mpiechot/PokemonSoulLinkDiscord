using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Bot;
using PokeSoulLinkBot.Bot.Commands;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Handlers;
using PokeSoulLinkBot.Bot.Presentation;
using PokeSoulLinkBot.Bot.Registration;
using PokeSoulLinkBot.Bot.Services;
using PokeSoulLinkBot.Core.Models;
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
        var gameDataFallbackCatalogPath = Path.Combine(AppContext.BaseDirectory, "Data", "game-data-fallback.json");
        var pokemonDataCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PokeSoulLinkBot",
            "Data",
            "pokemon-data-cache.json");
        var resourcesDirectoryPath = Path.Combine(AppContext.BaseDirectory, "Resources");
        var httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
            Timeout = TimeSpan.FromSeconds(5),
        };

        var pokemonDataCacheStore = new PokemonDataCacheStore(pokemonDataCachePath);
        var pokemonNameResolver = new PokeApiPokemonNameResolver(httpClient, pokemonDataCacheStore);
        var pokemonLookupService = new PokeApiPokemonLookupService(
            httpClient,
            pokemonNameResolver,
            pokemonDataCacheStore);
        var pokemonReferenceService = new PokeApiPokemonReferenceService(httpClient);
        var pokedexService = new PokeApiPokedexService(httpClient, pokemonNameResolver, pokemonDataCacheStore);
        var pokedexPresenter = new PokedexPresenter();
        var movesPresenter = new PokemonMoveLearnsetPresenter();
        var arenaInfoService = new PokemonDbArenaInfoService(httpClient);
        var gameDataCatalogService = new PokeApiGameDataCatalogService(
            httpClient,
            gameDataCachePath,
            gameDataFallbackCatalogPath);

        var runStore = new RunStore(filePath);
        var runService = new RunService(runStore);
        var teamCheckAnalyzer = new TeamCheckAnalyzer();
        var catchEligibilityService = new CatchEligibilityService(runService, pokedexService);
        var diagnosticsService = new BotDiagnosticsService();
        var healthService = new BotHealthService(
            client,
            runService,
            gameDataCatalogService,
            pokemonDataCacheStore,
            diagnosticsService);
        var embedFactory = new EmbedFactory();
        var embedImageFactory = new EmbedImageFactory(resourcesDirectoryPath);

        var commands = new List<ISlashCommand>
        {
            new RunStartCommand(runService, embedFactory, embedImageFactory, gameDataCatalogService),
            new RunEndCommand(runService, embedFactory, embedImageFactory),
            new CatchCommand(runService, embedFactory, embedImageFactory, pokemonLookupService, gameDataCatalogService),
            new CatchEditCommand(runService, embedFactory, pokemonLookupService, gameDataCatalogService),
            new CatchRemoveCommand(runService, embedFactory, gameDataCatalogService),
            new CatchCheckCommand(catchEligibilityService, embedFactory),
            new DeathCommand(runService, embedFactory, embedImageFactory),
            new DeathUndoCommand(runService, embedFactory, gameDataCatalogService),
            new RouteDeathCommand(runService, embedFactory, embedImageFactory, gameDataCatalogService),
            new StatusCommand(runService, embedFactory, embedImageFactory, pokemonLookupService),
            new StatsCommand(runService, embedFactory, embedImageFactory),
            new TeamCommand(runService, embedFactory),
            new TeamCheckCommand(runService, embedFactory, teamCheckAnalyzer),
            new SwapCommand(runService, embedFactory, embedImageFactory),
            new UseCommand(runService, embedFactory, embedImageFactory),
            new PokedexCommand(pokedexService, pokedexPresenter),
            new MovesCommand(pokedexService, movesPresenter),
            new TypeCommand(pokemonReferenceService, embedFactory),
            new AttackInfoCommand(pokemonReferenceService, embedFactory),
            new ArenaCommand(arenaInfoService, embedFactory, embedImageFactory, gameDataCatalogService, runService),
            new ArenasCommand(arenaInfoService, embedFactory, embedImageFactory, gameDataCatalogService, runService),
            new ArenaCompleteCommand(arenaInfoService, embedFactory, embedImageFactory, gameDataCatalogService, runService),
        };

        var slashCommandRouter = new SlashCommandRouter(commands, embedFactory, diagnosticsService);
        var slashCommandRegistrationService = new SlashCommandRegistrationService();
        var readyStartupTaskRunner = new ReadyStartupTaskRunner(RegisterCommandsAfterReadyAsync);

        client.Log += logMessage => OnLogAsync(logMessage, diagnosticsService);
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

                _ = Task.Run(WarmPokemonDataCacheAsync);

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
                diagnosticsService.RecordException(
                    "Error",
                    "ReadyStartup",
                    "Ready startup failed.",
                    exception);
            }
        }

        async Task WarmPokemonDataCacheAsync()
        {
            try
            {
                Log.Information("Warming Pokemon data cache.");
                await pokemonDataCacheStore.InitializeAsync();
                await pokemonNameResolver.WarmUpAsync();
                Log.Information("Pokemon data cache warmup completed.");
            }
            catch (Exception exception)
            {
                Log.Warning(exception, "Pokemon data cache warmup failed.");
                diagnosticsService.RecordException(
                    "Warning",
                    "PokemonDataWarmup",
                    "Pokemon data cache warmup failed.",
                    exception);
            }
        }

        async Task RegisterSlashCommandsAsync(IReadOnlyCollection<ApplicationCommandProperties> definitions)
        {
            var commandDefinitions = definitions.ToArray();
            var registrationTargets = CreateRegistrationTargets();

            await slashCommandRegistrationService.RegisterAsync(commandDefinitions, registrationTargets);
        }

        IReadOnlyCollection<ISlashCommandRegistrationTarget> CreateRegistrationTargets()
        {
            var registrationMode = (configuration["DISCORD_COMMAND_REGISTRATION_MODE"] ?? "all")
                .Trim()
                .ToLowerInvariant();
            var configuredGuildIds = ParseGuildIds(configuration["DISCORD_COMMAND_GUILD_IDS"]);
            var targets = new List<ISlashCommandRegistrationTarget>();

            if (ShouldRegisterGlobalCommands(registrationMode))
            {
                targets.Add(new SlashCommandRegistrationTarget(
                    "global commands",
                    async () => (await client.GetGlobalApplicationCommandsAsync()).Cast<IApplicationCommand>().ToList(),
                    async definitions => await client.BulkOverwriteGlobalApplicationCommandsAsync(definitions)));
            }

            if (!ShouldRegisterGuildCommands(registrationMode))
            {
                return targets;
            }

            foreach (var guild in client.Guilds)
            {
                if (configuredGuildIds.Count > 0 && !configuredGuildIds.Contains(guild.Id))
                {
                    continue;
                }

                targets.Add(new SlashCommandRegistrationTarget(
                    $"guild {guild.Name} ({guild.Id})",
                    async () => (await guild.GetApplicationCommandsAsync()).Cast<IApplicationCommand>().ToList(),
                    async definitions => await guild.BulkOverwriteApplicationCommandAsync(definitions)));
            }

            return targets;
        }

        static bool ShouldRegisterGlobalCommands(string registrationMode)
        {
            return registrationMode is "all" or "global";
        }

        static bool ShouldRegisterGuildCommands(string registrationMode)
        {
            return registrationMode is "all" or "guild" or "guilds" or "development";
        }

        static IReadOnlySet<ulong> ParseGuildIds(string? guildIds)
        {
            var parsedGuildIds = new HashSet<ulong>();
            if (string.IsNullOrWhiteSpace(guildIds))
            {
                return parsedGuildIds;
            }

            foreach (var value in guildIds.Split(
                new[] { ',', ';', ' ' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (ulong.TryParse(value, out ulong guildId))
                {
                    parsedGuildIds.Add(guildId);
                }
                else
                {
                    Log.Warning("Ignoring invalid Discord guild id '{GuildId}' in DISCORD_COMMAND_GUILD_IDS.", value);
                }
            }

            return parsedGuildIds;
        }
    }

    private static Task OnLogAsync(LogMessage logMessage, BotDiagnosticsService diagnosticsService)
    {
        var level = MapDiscordLogLevel(logMessage.Severity);
        var message = string.IsNullOrWhiteSpace(logMessage.Message)
            ? "Discord log event without message."
            : logMessage.Message;

        if (logMessage.Exception is not null)
        {
            Log.Write(level, logMessage.Exception, "Discord {Source}: {Message}", logMessage.Source, message);
            RecordDiscordDiagnostic(logMessage, diagnosticsService, message);
            return Task.CompletedTask;
        }

        Log.Write(level, "Discord {Source}: {Message}", logMessage.Source, message);
        RecordDiscordDiagnostic(logMessage, diagnosticsService, message);

        return Task.CompletedTask;
    }

    private static void RecordDiscordDiagnostic(
        LogMessage logMessage,
        BotDiagnosticsService diagnosticsService,
        string message)
    {
        if (logMessage.Severity is not LogSeverity.Critical and not LogSeverity.Error and not LogSeverity.Warning)
        {
            return;
        }

        if (logMessage.Exception is not null)
        {
            diagnosticsService.RecordException(
                MapDiagnosticSeverity(logMessage.Severity),
                $"Discord:{logMessage.Source}",
                message,
                logMessage.Exception);
            return;
        }

        diagnosticsService.Record(new DiagnosticEvent
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Severity = MapDiagnosticSeverity(logMessage.Severity),
            Source = $"Discord:{logMessage.Source}",
            Message = message,
        });
    }

    private static string MapDiagnosticSeverity(LogSeverity severity)
    {
        return severity switch
        {
            LogSeverity.Critical => "Fatal",
            LogSeverity.Error => "Error",
            LogSeverity.Warning => "Warning",
            _ => "Info",
        };
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
