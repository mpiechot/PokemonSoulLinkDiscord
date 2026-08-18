using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Bot.Handlers;
using PokeSoulLinkBot.Bot.Registration;
using Serilog;
using Serilog.Events;

namespace PokeSoulLinkBot.Bot.Hosting;

/// <summary>
/// Owns the Discord client lifecycle inside the generic host.
/// </summary>
public sealed class DiscordBotHostedService : IHostedService
{
    private readonly DiscordSocketClient client;
    private readonly IConfiguration configuration;
    private readonly SlashCommandRouter slashCommandRouter;
    private readonly SlashCommandRegistrationService slashCommandRegistrationService;
    private readonly IGameDataCatalogService gameDataCatalogService;
    private readonly PokemonDbArenaInfoService arenaInfoService;
    private readonly PokemonDataCacheStore pokemonDataCacheStore;
    private readonly PokeApiPokemonNameResolver pokemonNameResolver;
    private readonly IBotDiagnosticsService diagnosticsService;
    private ReadyStartupTaskRunner? readyStartupTaskRunner;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscordBotHostedService"/> class.
    /// </summary>
    public DiscordBotHostedService(
        DiscordSocketClient client,
        IConfiguration configuration,
        SlashCommandRouter slashCommandRouter,
        SlashCommandRegistrationService slashCommandRegistrationService,
        IGameDataCatalogService gameDataCatalogService,
        PokemonDbArenaInfoService arenaInfoService,
        PokemonDataCacheStore pokemonDataCacheStore,
        PokeApiPokemonNameResolver pokemonNameResolver,
        IBotDiagnosticsService diagnosticsService)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        this.slashCommandRouter = slashCommandRouter ?? throw new ArgumentNullException(nameof(slashCommandRouter));
        this.slashCommandRegistrationService = slashCommandRegistrationService ??
            throw new ArgumentNullException(nameof(slashCommandRegistrationService));
        this.gameDataCatalogService = gameDataCatalogService ??
            throw new ArgumentNullException(nameof(gameDataCatalogService));
        this.arenaInfoService = arenaInfoService ?? throw new ArgumentNullException(nameof(arenaInfoService));
        this.pokemonDataCacheStore = pokemonDataCacheStore ??
            throw new ArgumentNullException(nameof(pokemonDataCacheStore));
        this.pokemonNameResolver = pokemonNameResolver ??
            throw new ArgumentNullException(nameof(pokemonNameResolver));
        this.diagnosticsService = diagnosticsService ?? throw new ArgumentNullException(nameof(diagnosticsService));
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        string token = this.configuration["DISCORD_BOT_TOKEN"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("DISCORD_BOT_TOKEN wurde nicht gesetzt.");
        }

        this.readyStartupTaskRunner = new ReadyStartupTaskRunner(this.RegisterCommandsAfterReadyAsync);
        this.client.Log += this.OnLogAsync;
        this.client.Ready += this.readyStartupTaskRunner.HandleReadyAsync;
        this.client.SlashCommandExecuted += this.slashCommandRouter.HandleAsync;
        this.client.AutocompleteExecuted += this.slashCommandRouter.HandleAutocompleteAsync;

        Log.Information("Starting PokeSoulLinkBot.");
        await this.client.LoginAsync(TokenType.Bot, token);
        Log.Information("Discord login completed.");
        await this.client.StartAsync();
        Log.Information("Discord client started.");
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (this.client.ConnectionState is ConnectionState.Connected or ConnectionState.Connecting)
        {
            await this.client.StopAsync();
        }

        if (this.client.LoginState is LoginState.LoggedIn or LoginState.LoggingIn)
        {
            await this.client.LogoutAsync();
        }
    }

    private static bool ShouldRegisterGlobalCommands(string registrationMode)
    {
        return registrationMode is "all" or "global";
    }

    private static bool ShouldRegisterGuildCommands(string registrationMode)
    {
        return registrationMode is "all" or "guild" or "guilds" or "development";
    }

    private static IReadOnlySet<ulong> ParseGuildIds(string? guildIds)
    {
        var parsedGuildIds = new HashSet<ulong>();
        if (string.IsNullOrWhiteSpace(guildIds))
        {
            return parsedGuildIds;
        }

        foreach (string value in guildIds.Split(
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

    private async Task RegisterCommandsAfterReadyAsync()
    {
        try
        {
            IReadOnlyCollection<ApplicationCommandProperties> definitions = this.slashCommandRouter.GetDefinitions();
            foreach (ApplicationCommandProperties definition in definitions)
            {
                Log.Debug("Prepared slash command definition {CommandName}.", definition.Name.Value);
            }

            await this.RegisterSlashCommandsAsync(definitions);
            _ = Task.Run(this.WarmPokemonDataCacheAsync);

            Log.Information("Initializing game data catalog.");
            await this.gameDataCatalogService.InitializeAsync();
            Log.Information("Game data catalog initialization completed.");

            Log.Information("Warming arena information.");
            await this.arenaInfoService.WarmUpKnownEditionsAsync();
            Log.Information("Arena information warmup completed.");
        }
        catch (Exception exception)
        {
            Log.Error(exception, "Ready startup failed.");
            this.diagnosticsService.RecordException("Error", "ReadyStartup", "Ready startup failed.", exception);
        }
    }

    private async Task WarmPokemonDataCacheAsync()
    {
        try
        {
            Log.Information("Warming Pokemon data cache.");
            await this.pokemonDataCacheStore.InitializeAsync();
            await this.pokemonNameResolver.WarmUpAsync();
            Log.Information("Pokemon data cache warmup completed.");
        }
        catch (Exception exception)
        {
            Log.Warning(exception, "Pokemon data cache warmup failed.");
            this.diagnosticsService.RecordException(
                "Warning",
                "PokemonDataWarmup",
                "Pokemon data cache warmup failed.",
                exception);
        }
    }

    private async Task RegisterSlashCommandsAsync(IReadOnlyCollection<ApplicationCommandProperties> definitions)
    {
        await this.slashCommandRegistrationService.RegisterAsync(definitions, this.CreateRegistrationTargets());
    }

    private IReadOnlyCollection<ISlashCommandRegistrationTarget> CreateRegistrationTargets()
    {
        string registrationMode = (this.configuration["DISCORD_COMMAND_REGISTRATION_MODE"] ?? "all")
            .Trim()
            .ToLowerInvariant();
        IReadOnlySet<ulong> configuredGuildIds = ParseGuildIds(this.configuration["DISCORD_COMMAND_GUILD_IDS"]);
        var targets = new List<ISlashCommandRegistrationTarget>();

        if (ShouldRegisterGlobalCommands(registrationMode))
        {
            targets.Add(new SlashCommandRegistrationTarget(
                "global commands",
                async () => (await this.client.GetGlobalApplicationCommandsAsync()).Cast<IApplicationCommand>().ToList(),
                async definitions => await this.client.BulkOverwriteGlobalApplicationCommandsAsync(definitions)));
        }

        if (!ShouldRegisterGuildCommands(registrationMode))
        {
            return targets;
        }

        foreach (SocketGuild guild in this.client.Guilds)
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

    private Task OnLogAsync(LogMessage logMessage)
    {
        LogEventLevel level = MapDiscordLogLevel(logMessage.Severity);
        string message = string.IsNullOrWhiteSpace(logMessage.Message)
            ? "Discord log event without message."
            : logMessage.Message;

        if (logMessage.Exception is not null)
        {
            Log.Write(level, logMessage.Exception, "Discord {Source}: {Message}", logMessage.Source, message);
            this.RecordDiscordDiagnostic(logMessage, message);
            return Task.CompletedTask;
        }

        Log.Write(level, "Discord {Source}: {Message}", logMessage.Source, message);
        this.RecordDiscordDiagnostic(logMessage, message);
        return Task.CompletedTask;
    }

    private void RecordDiscordDiagnostic(LogMessage logMessage, string message)
    {
        if (logMessage.Severity is not LogSeverity.Critical and not LogSeverity.Error and not LogSeverity.Warning)
        {
            return;
        }

        if (logMessage.Exception is not null)
        {
            this.diagnosticsService.RecordException(
                MapDiagnosticSeverity(logMessage.Severity),
                $"Discord:{logMessage.Source}",
                message,
                logMessage.Exception);
            return;
        }

        this.diagnosticsService.Record(new PokeSoulLinkBot.Core.Models.DiagnosticEvent
        {
            OccurredAtUtc = DateTimeOffset.UtcNow,
            Severity = MapDiagnosticSeverity(logMessage.Severity),
            Source = $"Discord:{logMessage.Source}",
            Message = message,
        });
    }
}
