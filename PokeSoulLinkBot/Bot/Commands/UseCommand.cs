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
    private readonly EmbedImageFactory embedImageFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="UseCommand"/> class.
    /// </summary>
    /// <param name="runService">The run service.</param>
    /// <param name="embedFactory">The embed factory.</param>
    /// <param name="embedImageFactory">The embed image factory.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when one of the parameters is <see langword="null"/>.
    /// </exception>
    public UseCommand(
        IRunService runService,
        EmbedFactory embedFactory,
        EmbedImageFactory embedImageFactory)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
        this.embedImageFactory = embedImageFactory ?? throw new ArgumentNullException(nameof(embedImageFactory));
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

        var messages = this.embedFactory.CreateUseMessages(activeRun);
        var image = this.embedImageFactory.CreateUseImage();
        var embed = this.embedFactory.CreateRunSummaryEmbed("Active Team Updated", activeRun, image.AttachmentUrl);
        await command.RespondWithFileAsync(image.FileAttachment, text: messages[0], embed: embed);

        foreach (var message in messages.Skip(1))
        {
            await command.FollowupAsync(message);
        }
    }
}
