using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Bot.Commands;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Handlers;
using PokeSoulLinkBot.Bot.Presentation;
using PokeSoulLinkBot.Infrastructure.Persistence;

var configuration = new ConfigurationBuilder()
    .AddUserSecrets<Program>(optional: true)
    .AddEnvironmentVariables()
    .Build();

var token = configuration["DISCORD_BOT_TOKEN"];

if (string.IsNullOrWhiteSpace(token))
{
    throw new InvalidOperationException("DISCORD_BOT_TOKEN wurde nicht gesetzt.");
}

var socketConfig = new DiscordSocketConfig()
{
    GatewayIntents = GatewayIntents.Guilds,
};

var client = new DiscordSocketClient(socketConfig);

var filePath = Path.Combine(AppContext.BaseDirectory, "Data", "runs.json");
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

var runStore = new RunStore(filePath);
var runService = new RunService(runStore);
var embedFactory = new EmbedFactory();
var embedImageFactory = new EmbedImageFactory(resourcesDirectoryPath);

var commands = new List<ISlashCommand>
{
    new RunStartCommand(runService, embedFactory, embedImageFactory),
    new RunEndCommand(runService, embedFactory, embedImageFactory),
    new CatchCommand(runService, embedFactory, embedImageFactory, pokemonLookupService),
    new DeathCommand(runService, embedFactory, embedImageFactory),
    new StatusCommand(runService, embedFactory, embedImageFactory, pokemonLookupService),
    new StatsCommand(runService, embedFactory, embedImageFactory),
    new TeamCommand(runService, embedFactory, embedImageFactory),
    new UseCommand(runService, embedFactory, embedImageFactory),
    new SwapCommand(runService, embedFactory),
    new PokedexCommand(pokedexService, pokedexPresenter),
    new ArenaCommand(arenaInfoService),
};

var slashCommandRouter = new SlashCommandRouter(commands, embedFactory);

client.Log += OnLogAsync;
client.Ready += OnReadyAsync;
client.SlashCommandExecuted += slashCommandRouter.HandleAsync;

await client.LoginAsync(TokenType.Bot, token);
await client.StartAsync();
await Task.Delay(Timeout.Infinite);

async Task OnReadyAsync()
{
    try
    {
        var definitions = slashCommandRouter.GetDefinitions();
        foreach (var definition in definitions)
        {
            Console.WriteLine(definition.Name);
        }

        await client.BulkOverwriteGlobalApplicationCommandsAsync(definitions.ToArray());
        Console.WriteLine("Slash commands registered.");
    }
    catch (Exception exception)
    {
        Console.WriteLine($"Command registration failed: {exception.Message}");
    }
}

Task OnLogAsync(LogMessage logMessage)
{
    Console.WriteLine($"[{logMessage.Severity}] {logMessage.Source}: {logMessage.Message}");

    if (logMessage.Exception is not null)
    {
        Console.WriteLine(logMessage.Exception);
    }

    return Task.CompletedTask;
}
