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
var gameDataCachePath = Path.Combine(AppContext.BaseDirectory, "Data", "game-data-catalog.json");
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
var embedFactory = new EmbedFactory();
var embedImageFactory = new EmbedImageFactory(resourcesDirectoryPath);

var commands = new List<ISlashCommand>
{
    new RunStartCommand(runService, embedFactory, embedImageFactory, gameDataCatalogService),
    new RunEndCommand(runService, embedFactory, embedImageFactory),
    new CatchCommand(runService, embedFactory, embedImageFactory, pokemonLookupService, gameDataCatalogService),
    new DeathCommand(runService, embedFactory, embedImageFactory),
    new StatusCommand(runService, embedFactory, embedImageFactory, pokemonLookupService),
    new StatsCommand(runService, embedFactory, embedImageFactory),
    new TeamCommand(runService, embedFactory, embedImageFactory),
    new SwapCommand(runService, embedFactory),
    new PokedexCommand(pokedexService, pokedexPresenter),
    new ArenaCommand(arenaInfoService, gameDataCatalogService, runService),
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
        Log.Information("Registering global slash commands.");
        var definitions = slashCommandRouter.GetDefinitions();
        foreach (var definition in definitions)
        {
            Log.Debug("Prepared slash command definition {CommandName}.", definition.Name.Value);
        }

        await client.BulkOverwriteGlobalApplicationCommandsAsync(definitions.ToArray());
        Log.Information("Registered {CommandCount} global slash commands.", definitions.Count);

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

Task OnLogAsync(LogMessage logMessage)
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

LogEventLevel MapDiscordLogLevel(LogSeverity severity)
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
