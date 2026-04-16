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
public class DeathCommand : ISlashCommand
{
    private readonly IRunService runService;
    private readonly EmbedFactory embedFactory;
    private readonly EmbedImageFactory embedImageFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeathCommand"/> class.
    /// </summary>
    /// <param name="runService">The run service.</param>
    /// <param name="embedFactory">The embed factory.</param>
    /// <param name="embedImageFactory">The embed image factory.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when one of the parameters is <see langword="null"/>.
    /// </exception>
    public DeathCommand(
        IRunService runService,
        EmbedFactory embedFactory,
        EmbedImageFactory embedImageFactory)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
        this.embedImageFactory = embedImageFactory ?? throw new ArgumentNullException(nameof(embedImageFactory));
    }

    /// <inheritdoc />
    public string CommandName => "death";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(CommandName)
            .WithDescription("Register the death of a linked Pokémon group.")
            .AddOption("route", ApplicationCommandOptionType.String, "The route that is now dead.", isRequired: true)
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var guildId = CommandOptionHelper.GetGuildId(command);

        var route = CommandOptionHelper.GetRequiredStringOption(command, "route");

        var linkGroup = this.runService.RegisterDeath(guildId, route);
        var image = this.embedImageFactory.CreateDeathImage();
        var embed = this.embedFactory.CreateDeathRegisteredEmbed(linkGroup, image.AttachmentUrl);

        await command.RespondWithFileAsync(image.FileAttachment, embed: embed);
    }
}
