using Discord;

namespace PokeSoulLinkBot.Bot.Helpers;

/// <summary>
/// Wraps Discord slash-command response operations behind a testable adapter.
/// </summary>
public interface ISlashCommandResponse
{
    /// <summary>
    /// Gets the command name used for telemetry.
    /// </summary>
    string CommandName { get; }

    /// <summary>
    /// Gets a value indicating whether the interaction has already received an initial response.
    /// </summary>
    bool HasResponded { get; }

    /// <summary>
    /// Acknowledges the command so slow handlers can answer with followups.
    /// </summary>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task DeferAsync();

    /// <summary>
    /// Sends a text or embed response.
    /// </summary>
    /// <param name="text">The response text.</param>
    /// <param name="embed">The response embed.</param>
    /// <param name="ephemeral">A value indicating whether the response should be visible only to the caller.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SendAsync(string? text = null, Embed? embed = null, bool ephemeral = false);

    /// <summary>
    /// Sends a file response.
    /// </summary>
    /// <param name="fileAttachment">The file attachment.</param>
    /// <param name="text">The response text.</param>
    /// <param name="embed">The response embed.</param>
    /// <param name="ephemeral">A value indicating whether the response should be visible only to the caller.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SendFileAsync(FileAttachment fileAttachment, string? text = null, Embed? embed = null, bool ephemeral = false);

    /// <summary>
    /// Sends supplemental followup messages.
    /// </summary>
    /// <param name="messages">The followup messages.</param>
    /// <param name="ephemeral">A value indicating whether the responses should be visible only to the caller.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task SendFollowupsAsync(IEnumerable<string> messages, bool ephemeral = false);
}
