using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "use" slash command.
/// </summary>
public class UseCommand : ISlashCommand
{
    private readonly IRunService runService;
    private readonly EmbedFactory embedFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="UseCommand"/> class.
    /// </summary>
    /// <param name="runService">The run service.</param>
    /// <param name="embedFactory">The embed factory.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when one of the parameters is <see langword="null"/>.
    /// </exception>
    public UseCommand(
        IRunService runService,
        EmbedFactory embedFactory)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
    }

    /// <inheritdoc />
    public string CommandName => "use";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Sets one route for every player as an active pokemon.")
            .AddOption("route", ApplicationCommandOptionType.String, "The number of the route to use.", isRequired: true)
            .AddOption("position", ApplicationCommandOptionType.Integer, "The position in the active team [1 - 6].", isRequired: true)
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var guildId = CommandOptionHelper.GetGuildId(command);

        var routeId = CommandOptionHelper.GetRequiredStringOption(command, "route").ToLowerInvariant().Trim();
        var position = CommandOptionHelper.GetRequiredIntegerOption(command, "position");

        if (position < 1 || position > 6)
        {
            var errorEmbed = this.embedFactory.CreateErrorEmbed("Position must be between 1 and 6.");
            await command.RespondAsync(embed: errorEmbed, ephemeral: true);
            return;
        }

        var activeRun = this.runService.UseRoute(guildId, routeId, (int)position);

        var message = this.embedFactory.CreateUseMessage(activeRun);
        await command.RespondAsync(message);
    }
}
