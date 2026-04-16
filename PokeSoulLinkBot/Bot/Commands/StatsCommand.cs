using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "run-start" slash command.
/// </summary>
public class StatsCommand : ISlashCommand
{
    private readonly IRunService runService;
    private readonly EmbedFactory embedFactory;
    private readonly EmbedImageFactory embedImageFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatsCommand"/> class.
    /// </summary>
    /// <param name="runService">The run service.</param>
    /// <param name="embedFactory">The embed factory.</param>
    /// <param name="embedImageFactory">The embed image factory.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when one of the parameters is <see langword="null"/>.
    /// </exception>
    public StatsCommand(
        IRunService runService,
        EmbedFactory embedFactory,
        EmbedImageFactory embedImageFactory)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
        this.embedImageFactory = embedImageFactory ?? throw new ArgumentNullException(nameof(embedImageFactory));
    }

    /// <inheritdoc />
    public string CommandName => "stats";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName("stats")
            .WithDescription("Show historical run statistics.")
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var guildId = CommandOptionHelper.GetGuildId(command);
        var runs = this.runService.GetRuns(guildId);

        if (runs.Count == 0)
        {
            var emptyEmbed = this.embedFactory.CreateErrorEmbed("No runs stored yet.");
            await command.RespondAsync(embed: emptyEmbed, ephemeral: true);
            return;
        }

        var image = this.embedImageFactory.CreateStatsImage();
        var embed = this.embedFactory.CreateStatsEmbed(runs, image.AttachmentUrl);

        await command.RespondWithFileAsync(image.FileAttachment, embed: embed);
    }
}
