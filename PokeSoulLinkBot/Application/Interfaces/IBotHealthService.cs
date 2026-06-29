using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Interfaces;

/// <summary>
/// Creates shareable bot diagnostic reports.
/// </summary>
public interface IBotHealthService
{
    /// <summary>
    /// Creates a health report for the specified guild context.
    /// </summary>
    /// <param name="guildId">The Discord guild identifier.</param>
    /// <returns>The health report.</returns>
    Task<BotHealthReport> GetReportAsync(string guildId);
}
