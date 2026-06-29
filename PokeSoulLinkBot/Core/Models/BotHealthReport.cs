namespace PokeSoulLinkBot.Core.Models;

/// <summary>
/// Represents a shareable diagnostic snapshot of the bot state.
/// </summary>
public sealed class BotHealthReport
{
    /// <summary>
    /// Gets or sets the UTC date and time when the report was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets or sets the Discord connection state.
    /// </summary>
    public string DiscordConnectionState { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the Discord gateway latency in milliseconds.
    /// </summary>
    public int DiscordLatencyMilliseconds { get; set; }

    /// <summary>
    /// Gets or sets the number of guilds visible to the bot.
    /// </summary>
    public int GuildCount { get; set; }

    /// <summary>
    /// Gets or sets the active run summary.
    /// </summary>
    public ActiveRunHealthStatus? ActiveRun { get; set; }

    /// <summary>
    /// Gets or sets the game-data catalog status.
    /// </summary>
    public GameDataCatalogStatus GameDataCatalog { get; set; } = new GameDataCatalogStatus();

    /// <summary>
    /// Gets or sets the Pokémon data cache status.
    /// </summary>
    public PokemonDataCacheStatus PokemonDataCache { get; set; } = new PokemonDataCacheStatus();

    /// <summary>
    /// Gets or sets recent diagnostic events.
    /// </summary>
    public IReadOnlyList<DiagnosticEvent> RecentEvents { get; set; } = Array.Empty<DiagnosticEvent>();
}
