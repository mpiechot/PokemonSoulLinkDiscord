using Discord;
using Discord.WebSocket;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Represents a Discord slash command including its definition and execution logic.
/// </summary>
public interface ISlashCommand
{
    /// <summary>
    /// Gets the unique slash command name.
    /// </summary>
    string CommandName { get; }

    /// <summary>
    /// Creates the Discord slash command definition.
    /// </summary>
    /// <returns>The created command definition.</returns>
    ApplicationCommandProperties BuildDefinition();

    /// <summary>
    /// Executes the slash command.
    /// </summary>
    /// <param name="command">The incoming slash command.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task HandleAsync(SocketSlashCommand command);
}