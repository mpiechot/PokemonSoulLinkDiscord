using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Bot.Commands;
using PokeSoulLinkBot.Bot.Factories;

namespace PokeSoulLinkBot.Bot.Handlers;

/// <summary>
/// Routes slash commands to their dedicated command implementations.
/// </summary>
public sealed class SlashCommandRouter
{
    private readonly IReadOnlyDictionary<string, ISlashCommand> commands;
    private readonly EmbedFactory embedFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SlashCommandRouter"/> class.
    /// </summary>
    /// <param name="commands">The available slash commands.</param>
    /// <param name="embedFactory">The embed factory used for error messages.</param>
    public SlashCommandRouter(
        IReadOnlyCollection<ISlashCommand> commands,
        EmbedFactory embedFactory)
    {
        ArgumentNullException.ThrowIfNull(commands);

        this.commands = commands.ToDictionary(command => command.CommandName, StringComparer.OrdinalIgnoreCase);
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
    }

    /// <summary>
    /// Gets all slash command definitions.
    /// </summary>
    /// <returns>A read-only collection of command definitions.</returns>
    public IReadOnlyCollection<ApplicationCommandProperties> GetDefinitions()
    {
        return this.commands.Values
            .Select(command => command.BuildDefinition())
            .ToList();
    }

    /// <summary>
    /// Routes the incoming slash command to the matching handler.
    /// </summary>
    /// <param name="command">The incoming slash command.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    public async Task HandleAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        try
        {
            if (!this.commands.TryGetValue(command.CommandName, out var slashCommand))
            {
                await command.RespondAsync("Unknown command.", ephemeral: true);
                return;
            }

            await slashCommand.HandleAsync(command);
        }
        catch (Exception exception)
        {
            var errorEmbed = this.embedFactory.CreateErrorEmbed(exception.Message);

            if (command.HasResponded)
            {
                await command.FollowupAsync(embed: errorEmbed, ephemeral: true);
                return;
            }

            await command.RespondAsync(embed: errorEmbed, ephemeral: true);
        }
    }
}