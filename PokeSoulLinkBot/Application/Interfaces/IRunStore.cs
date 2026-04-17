using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Interfaces;

/// <summary>
/// Provides persistence operations for Soul Link runs.
/// </summary>
public interface IRunStore
{
    /// <summary>
    /// Gets the currently active run for the specified guild.
    /// </summary>
    /// <param name="guildId">The Discord guild identifier.</param>
    /// <returns>
    /// The active <see cref="SoulLinkRun"/> if one exists; otherwise, <see langword="null"/>.
    /// </returns>
    SoulLinkRun? GetActiveRun(string guildId);

    /// <summary>
    /// Gets all stored runs for the specified guild.
    /// </summary>
    /// <param name="guildId">The Discord guild identifier.</param>
    /// <returns>A read-only list of runs for the guild.</returns>
    IReadOnlyList<SoulLinkRun> GetRunsForGuild(string guildId);

    /// <summary>
    /// Adds a new run to the store.
    /// </summary>
    /// <param name="run">The run to add.</param>
    void AddRun(SoulLinkRun run);

    /// <summary>
    /// Persists the current in-memory state.
    /// </summary>
    void Save();
}
