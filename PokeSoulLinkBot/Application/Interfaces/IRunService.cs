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
    /// <param name="pokemonTypes">The Pokémon types.</param>
    /// <returns>The updated link group for the route.</returns>
    LinkGroup RegisterCatch(
        string guildId,
        string route,
        ulong playerId,
        string playerName,
        string pokemon,
        IReadOnlyList<string> pokemonTypes);

    /// <summary>
    /// Sets a route as active at the specified team position.
    /// </summary>
    /// <param name="guildId">The Discord guild identifier.</param>
    /// <param name="route">The route or area name.</param>
    /// <param name="position">The one-based team position.</param>
    /// <returns>The active run.</returns>
    SoulLinkRun UseRoute(string guildId, string route, int position);

    /// <summary>
    /// Swaps an active team route with a boxed route.
    /// </summary>
    /// <param name="guildId">The Discord guild identifier.</param>
    /// <param name="teamRoute">The route currently in the team.</param>
    /// <param name="boxRoute">The route currently in the box.</param>
    /// <returns>The active run.</returns>
    SoulLinkRun SwapRoute(string guildId, string teamRoute, string boxRoute);

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
