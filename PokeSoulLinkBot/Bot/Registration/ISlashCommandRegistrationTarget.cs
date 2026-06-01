using Discord;

namespace PokeSoulLinkBot.Bot.Registration;

/// <summary>
/// Represents a Discord location where slash commands can be registered.
/// </summary>
public interface ISlashCommandRegistrationTarget
{
    /// <summary>
    /// Gets the display name used in logs.
    /// </summary>
    string DisplayName { get; }

    /// <summary>
    /// Gets remote command snapshots for this target.
    /// </summary>
    /// <returns>The remote command snapshots.</returns>
    Task<IReadOnlyCollection<SlashCommandDefinitionSnapshot>> GetRemoteCommandSnapshotsAsync();

    /// <summary>
    /// Replaces remote commands for this target.
    /// </summary>
    /// <param name="definitions">The local command definitions.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task BulkOverwriteAsync(ApplicationCommandProperties[] definitions);
}
