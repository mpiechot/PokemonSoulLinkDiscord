using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Interfaces;

/// <summary>
/// Provides business logic for managing Soul Link runs.
/// </summary>
public interface IRunService
{
    /// <summary>
    /// Starts a new Soul Link run for the specified guild.
    /// </summary>
    /// <param name="guildId">The Discord guild identifier.</param>
    /// <param name="name">The name of the run.</param>
    /// <param name="game">The game name.</param>
    /// <param name="players">The participating players.</param>
    /// <returns>The created run.</returns>
    SoulLinkRun StartRun(string guildId, string name, string game, IReadOnlyList<RunPlayer> players);

    /// <summary>
    /// Ends the currently active run for the specified guild.
    /// </summary>
    /// <param name="guildId">The Discord guild identifier.</param>
    /// <param name="reason">The optional reason why the run ended.</param>
    /// <returns>The ended run.</returns>
    SoulLinkRun EndRun(string guildId, string? reason);

    /// <summary>
    /// Registers a caught Pokémon for a player on a specific route.
    /// </summary>
    /// <param name="guildId">The Discord guild identifier.</param>
    /// <param name="route">The route or area name.</param>
    /// <param name="playerId">The Discord user identifier of the player.</param>
    /// <param name="playerName">The display name of the player.</param>
    /// <param name="pokemon">The Pokémon name.</param>
    /// <returns>The updated link group for the route.</returns>
    LinkGroup RegisterCatch(string guildId, string route, ulong playerId, string playerName, string pokemon);

    /// <summary>
    /// Registers the death of a Pokémon and marks the whole linked group as dead.
    /// </summary>
    /// <param name="guildId">The Discord guild identifier.</param>
    /// <param name="pokemon">The Pokémon name.</param>
    /// <returns>The affected link group.</returns>
    LinkGroup RegisterDeath(string guildId, string pokemon);

    /// <summary>
    /// Gets the currently active run for the specified guild.
    /// </summary>
    /// <param name="guildId">The Discord guild identifier.</param>
    /// <returns>The active run.</returns>
    SoulLinkRun GetActiveRun(string guildId);

    /// <summary>
    /// Gets all stored runs for the specified guild.
    /// </summary>
    /// <param name="guildId">The Discord guild identifier.</param>
    /// <returns>A read-only list of runs.</returns>
    IReadOnlyList<SoulLinkRun> GetRuns(string guildId);
}