using Discord;
using Discord.WebSocket;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Automation;
using PokeSoulLinkBot.Bot.Commands;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Handlers;
using PokeSoulLinkBot.Bot.Presentation;
using PokeSoulLinkBot.Bot.Registration;
using PokeSoulLinkBot.Bot.Services;
using PokeSoulLinkBot.Core.Configuration;
using PokeSoulLinkBot.Core.GameIntegration;
using PokeSoulLinkBot.Infrastructure.Persistence;

namespace PokeSoulLinkBot.Bot.Hosting;

/// <summary>
/// Composes and runs the Discord bot host.
/// </summary>
public static class BotHost
{
    /// <summary>
    /// Builds the application host without starting external connections.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>The configured host builder.</returns>
    public static HostApplicationBuilder CreateBuilder(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);
        builder.Configuration.AddUserSecrets<DiscordBotHostedService>(optional: true);
        RegisterServices(builder.Services, builder.Configuration);

        return builder;
    }

    /// <summary>
    /// Builds and runs the application host.
    /// </summary>
    /// <param name="args">The process arguments.</param>
    /// <returns>A task representing the host lifetime.</returns>
    public static async Task RunAsync(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
        builder.Configuration.AddUserSecrets<DiscordBotHostedService>(optional: true);
        builder.WebHost.UseUrls(builder.Configuration["SoulLink:ListenUrl"] ?? "http://127.0.0.1:5080");
        builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = SoulLink.Protocol.MaxMessageBytes);
        RegisterServices(builder.Services, builder.Configuration);
        await using WebApplication app = builder.Build();
        app.MapAutomation();
        await app.RunAsync();
    }

    private static void RegisterServices(IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<HostOptions>(options =>
            options.BackgroundServiceExceptionBehavior = BackgroundServiceExceptionBehavior.StopHost);
        services.AddAutomation(configuration);

        services.AddOptions<SoulLinkOptions>()
            .Bind(configuration.GetSection(SoulLinkOptions.SectionName))
            .Validate(
                options => options.IsValid(),
                "This read-only build does not support EnableRemoteWrites or EnableAutoTeamSync.")
            .ValidateOnStart();

        services.AddSingleton<DiscordSocketClient>(_ => new DiscordSocketClient(new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.Guilds,
        }));

        services.AddSingleton(_ => new HttpClient
        {
            BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
            Timeout = TimeSpan.FromSeconds(5),
        });

        services.AddSingleton<PokemonDataCacheStore>(_ => new PokemonDataCacheStore(
            GetPokemonDataCachePath(configuration)));
        services.AddSingleton<PokeApiPokemonNameResolver>();
        services.AddSingleton<IPokemonNameResolver>(serviceProvider =>
            serviceProvider.GetRequiredService<PokeApiPokemonNameResolver>());
        services.AddSingleton<IPokemonLookupService, PokeApiPokemonLookupService>();
        services.AddSingleton<IPokemonReferenceService, PokeApiPokemonReferenceService>();
        services.AddSingleton<IPokedexService, PokeApiPokedexService>();
        services.AddSingleton<PokemonDbArenaInfoService>();
        services.AddSingleton<IArenaInfoService>(serviceProvider =>
            serviceProvider.GetRequiredService<PokemonDbArenaInfoService>());
        services.AddSingleton<IGameDataCatalogService>(serviceProvider =>
            new PokeApiGameDataCatalogService(
                serviceProvider.GetRequiredService<HttpClient>(),
                GetGameDataCachePath(configuration),
                GetGameDataFallbackCatalogPath()));

        services.AddSingleton<IRunStore>(_ => new ReadOnlyRunStore(new RunStore(GetRunStorePath(configuration))));
        services.AddSingleton<IRunService, RunService>();
        services.AddSingleton<ICatchEligibilityService, CatchEligibilityService>();
        services.AddSingleton<TeamCheckAnalyzer>();
        services.AddSingleton<IBotDiagnosticsService, BotDiagnosticsService>();
        services.AddSingleton<IBotHealthService, BotHealthService>();
        services.AddSingleton<IRomCompatibilityAdapter, PokemonRubyCompatibilityAdapter>();

        services.AddSingleton<EmbedFactory>();
        services.AddSingleton(_ => new EmbedImageFactory(GetResourcesDirectoryPath()));
        services.AddSingleton<PokedexPresenter>();
        services.AddSingleton<PokemonMoveLearnsetPresenter>();
        services.AddSingleton<SlashCommandRegistrationService>();

        RegisterCommands(services);

        services.AddSingleton<SlashCommandRouter>(serviceProvider =>
            new SlashCommandRouter(
                serviceProvider.GetServices<ISlashCommand>()
                    .Select(command => (ISlashCommand)new AutomationContextCommand(
                        command,
                        serviceProvider.GetRequiredService<SoulLink.AutomationEngine>()))
                    .ToList(),
                serviceProvider.GetRequiredService<EmbedFactory>(),
                serviceProvider.GetRequiredService<IBotDiagnosticsService>()));
        services.AddHostedService<DiscordBotHostedService>();
    }

    private static void RegisterCommands(IServiceCollection services)
    {
        foreach (string name in AutomationCommand.Names)
        {
            services.AddSingleton<ISlashCommand>(provider => new AutomationCommand(
                name, provider.GetRequiredService<SoulLink.AutomationEngine>()));
        }

        services.AddSingleton<ISlashCommand, RunStartCommand>();
        services.AddSingleton<ISlashCommand, RunEndCommand>();
        services.AddSingleton<ISlashCommand, CatchCommand>();
        services.AddSingleton<ISlashCommand, CatchEditCommand>();
        services.AddSingleton<ISlashCommand, CatchRemoveCommand>();
        services.AddSingleton<ISlashCommand, CatchCheckCommand>();
        services.AddSingleton<ISlashCommand, DeathCommand>();
        services.AddSingleton<ISlashCommand, DeathUndoCommand>();
        services.AddSingleton<ISlashCommand, RouteDeathCommand>();
        services.AddSingleton<ISlashCommand, StatusCommand>();
        services.AddSingleton<ISlashCommand, StatsCommand>();
        services.AddSingleton<ISlashCommand, TeamCommand>();
        services.AddSingleton<ISlashCommand, TeamCheckCommand>();
        services.AddSingleton<ISlashCommand, SwapCommand>();
        services.AddSingleton<ISlashCommand, UseCommand>();
        services.AddSingleton<ISlashCommand, PokedexCommand>();
        services.AddSingleton<ISlashCommand, MovesCommand>();
        services.AddSingleton<ISlashCommand, TypeCommand>();
        services.AddSingleton<ISlashCommand, AttackInfoCommand>();
        services.AddSingleton<ISlashCommand, ArenaCommand>();
        services.AddSingleton<ISlashCommand, ArenasCommand>();
        services.AddSingleton<ISlashCommand, ArenaCompleteCommand>();
        services.AddSingleton<ISlashCommand, HealthCommand>();
    }

    private static string GetRunStorePath(IConfiguration configuration)
    {
        return configuration["SoulLink:PersistencePath"] ??
            Path.Combine(AppContext.BaseDirectory, "Data", "runs.json");
    }

    private static string GetGameDataCachePath(IConfiguration configuration)
    {
        return configuration["SoulLink:GameDataCachePath"] ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PokeSoulLinkBot",
                "Data",
                "game-data-catalog.json");
    }

    private static string GetGameDataFallbackCatalogPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Data", "game-data-fallback.json");
    }

    private static string GetPokemonDataCachePath(IConfiguration configuration)
    {
        return configuration["SoulLink:PokemonDataCachePath"] ??
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PokeSoulLinkBot",
                "Data",
                "pokemon-data-cache.json");
    }

    private static string GetResourcesDirectoryPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "Resources");
    }
}
