using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Bot.Helpers;

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
    /// <param name="response">The response adapter for this command.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task HandleAsync(SocketSlashCommand command, ISlashCommandResponse response);

    /// <summary>
    /// Handles autocomplete interactions for this slash command.
    /// </summary>
    /// <param name="interaction">The incoming autocomplete interaction.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task HandleAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        return interaction.RespondAsync(Array.Empty<AutocompleteResult>());
    }
}
