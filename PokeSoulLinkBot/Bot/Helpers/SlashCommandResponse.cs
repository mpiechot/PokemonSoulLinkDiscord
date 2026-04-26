using Discord;
using Discord.WebSocket;

namespace PokeSoulLinkBot.Bot.Helpers;

/// <summary>
/// Sends slash-command responses through the correct Discord interaction channel.
/// </summary>
public static class SlashCommandResponse
{
    /// <summary>
    /// Sends a slash-command response or followup depending on whether the interaction was already acknowledged.
    /// </summary>
    /// <param name="command">The slash command interaction.</param>
    /// <param name="text">The response text.</param>
    /// <param name="embed">The response embed.</param>
    /// <param name="ephemeral">A value indicating whether the response should be visible only to the caller.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static Task SendAsync(
        SocketSlashCommand command,
        string? text = null,
        Embed? embed = null,
        bool ephemeral = false)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.HasResponded
            ? command.FollowupAsync(text, embed: embed, ephemeral: ephemeral)
            : command.RespondAsync(text, embed: embed, ephemeral: ephemeral);
    }

    /// <summary>
    /// Sends a slash-command file response or followup depending on whether the interaction was already acknowledged.
    /// </summary>
    /// <param name="command">The slash command interaction.</param>
    /// <param name="fileAttachment">The file attachment.</param>
    /// <param name="text">The response text.</param>
    /// <param name="embed">The response embed.</param>
    /// <param name="ephemeral">A value indicating whether the response should be visible only to the caller.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public static Task SendFileAsync(
        SocketSlashCommand command,
        FileAttachment fileAttachment,
        string? text = null,
        Embed? embed = null,
        bool ephemeral = false)
    {
        ArgumentNullException.ThrowIfNull(command);

        return command.HasResponded
            ? command.FollowupWithFileAsync(fileAttachment, text: text, embed: embed, ephemeral: ephemeral)
            : command.RespondWithFileAsync(fileAttachment, text: text, embed: embed, ephemeral: ephemeral);
    }
}
