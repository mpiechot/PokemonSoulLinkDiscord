using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Bot.Services;

/// <summary>
/// Creates diagnostic snapshots for the bot.
/// </summary>
public sealed class BotHealthService : IBotHealthService
{
    private const int RecentDiagnosticEventCount = 8;

    private readonly DiscordSocketClient client;
    private readonly IRunService runService;
    private readonly IGameDataCatalogService gameDataCatalogService;
    private readonly PokemonDataCacheStore pokemonDataCacheStore;
    private readonly IBotDiagnosticsService diagnosticsService;

    /// <summary>
    /// Initializes a new instance of the <see cref="BotHealthService"/> class.
    /// </summary>
    /// <param name="client">The Discord client.</param>
    /// <param name="runService">The run service.</param>
    /// <param name="gameDataCatalogService">The game-data catalog service.</param>
    /// <param name="pokemonDataCacheStore">The Pokémon data cache store.</param>
    /// <param name="diagnosticsService">The diagnostics service.</param>
    public BotHealthService(
        DiscordSocketClient client,
        IRunService runService,
        IGameDataCatalogService gameDataCatalogService,
        PokemonDataCacheStore pokemonDataCacheStore,
        IBotDiagnosticsService diagnosticsService)
    {
        this.client = client ?? throw new ArgumentNullException(nameof(client));
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.gameDataCatalogService = gameDataCatalogService ?? throw new ArgumentNullException(nameof(gameDataCatalogService));
        this.pokemonDataCacheStore = pokemonDataCacheStore ?? throw new ArgumentNullException(nameof(pokemonDataCacheStore));
        this.diagnosticsService = diagnosticsService ?? throw new ArgumentNullException(nameof(diagnosticsService));
    }

    /// <inheritdoc />
    public async Task<BotHealthReport> GetReportAsync(string guildId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);

        return new BotHealthReport
        {
            CreatedAtUtc = DateTimeOffset.UtcNow,
            DiscordConnectionState = this.client.ConnectionState.ToString(),
            DiscordLatencyMilliseconds = this.client.Latency,
            GuildCount = this.client.Guilds.Count,
            ActiveRun = this.GetActiveRunStatus(guildId),
            GameDataCatalog = this.gameDataCatalogService.GetStatus(),
            PokemonDataCache = await this.pokemonDataCacheStore.GetStatusAsync(),
            RecentEvents = this.diagnosticsService.GetRecentEvents(RecentDiagnosticEventCount),
        };
    }

    private ActiveRunHealthStatus? GetActiveRunStatus(string guildId)
    {
        try
        {
            var activeRun = this.runService.GetActiveRun(guildId);
            return new ActiveRunHealthStatus
            {
                Name = activeRun.Name,
                Game = activeRun.Game,
                PlayerCount = activeRun.Players.Count,
                LinkGroupCount = activeRun.LinkGroups.Count,
                ActiveTeamCount = activeRun.ActiveLinks.Count(linkGroup => linkGroup is not null),
                DeadGroupCount = activeRun.LinkGroups.Count(group => group.Entries.Count > 0 && !group.IsAlive),
                LostRouteCount = activeRun.LinkGroups.Count(group => group.IsLostWithoutEncounter),
            };
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }
}
