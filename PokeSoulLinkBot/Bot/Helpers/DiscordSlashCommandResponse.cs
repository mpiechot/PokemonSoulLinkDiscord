using Discord;
using Discord.WebSocket;
using Serilog;

namespace PokeSoulLinkBot.Bot.Helpers;

/// <summary>
/// Sends slash-command responses through the correct Discord interaction channel.
/// </summary>
public sealed class DiscordSlashCommandResponse : ISlashCommandResponse
{
    private readonly SocketSlashCommand command;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscordSlashCommandResponse"/> class.
    /// </summary>
    /// <param name="command">The slash command interaction.</param>
    public DiscordSlashCommandResponse(SocketSlashCommand command)
    {
        this.command = command ?? throw new ArgumentNullException(nameof(command));
    }

    /// <inheritdoc />
    public string CommandName => this.command.CommandName;

    /// <inheritdoc />
    public bool HasResponded => this.command.HasResponded;

    /// <inheritdoc />
    public Task DeferAsync()
    {
        return this.command.DeferAsync();
    }

    /// <inheritdoc />
    public Task SendAsync(string? text = null, Embed? embed = null, bool ephemeral = false)
    {
        return this.command.HasResponded
            ? this.command.FollowupAsync(text, embed: embed, ephemeral: ephemeral)
            : this.command.RespondAsync(text, embed: embed, ephemeral: ephemeral);
    }

    /// <inheritdoc />
    public Task SendFileAsync(
        FileAttachment fileAttachment,
        string? text = null,
        Embed? embed = null,
        bool ephemeral = false)
    {
        return this.command.HasResponded
            ? this.command.FollowupWithFileAsync(fileAttachment, text: text, embed: embed, ephemeral: ephemeral)
            : this.command.RespondWithFileAsync(fileAttachment, text: text, embed: embed, ephemeral: ephemeral);
    }

    /// <inheritdoc />
    public async Task SendFollowupsAsync(IEnumerable<string> messages, bool ephemeral = false)
    {
        ArgumentNullException.ThrowIfNull(messages);

        foreach (var message in messages.Where(message => !string.IsNullOrWhiteSpace(message)))
        {
            try
            {
                await this.command.FollowupAsync(message, ephemeral: ephemeral);
            }
            catch (Exception exception)
            {
                Log.Warning(
                    exception,
                    "Could not send slash-command followup for /{CommandName}. MessageLength={MessageLength}.",
                    this.command.CommandName,
                    message.Length);
            }
        }
    }
}
