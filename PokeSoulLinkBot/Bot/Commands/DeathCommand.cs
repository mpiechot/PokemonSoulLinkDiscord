using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;
using PokeSoulLinkBot.Core.Models;
using Serilog;

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
            .WithName(this.CommandName)
            .WithDescription("Register the death of a linked Pokémon group.")
            .AddOption("route", ApplicationCommandOptionType.String, "The route that is now dead.", isRequired: true, isAutocomplete: true)
            .AddOption("reason", ApplicationCommandOptionType.String, "Why the linked Pokémon died.", isRequired: true)
            .AddOption("player", ApplicationCommandOptionType.User, "The player who caused the death.", isRequired: false)
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var guildId = CommandOptionHelper.GetGuildId(command);

        var route = CommandOptionHelper.GetRequiredStringOption(command, "route");
        var reason = CommandOptionHelper.GetRequiredStringOption(command, "reason");
        var player = CommandOptionHelper.GetOptionalUserOption(command, "player");

        var linkGroup = this.runService.RegisterDeath(
            guildId,
            route,
            reason,
            player?.Id,
            player?.Username);
        var image = this.embedImageFactory.CreateDeathImage();
        var embed = this.embedFactory.CreateDeathRegisteredEmbed(linkGroup, image.AttachmentUrl);

        await command.RespondWithFileAsync(image.FileAttachment, embed: embed);
    }

    /// <inheritdoc />
    public async Task HandleAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var guildId = interaction.GuildId?.ToString();
        if (string.IsNullOrWhiteSpace(guildId))
        {
            Log.Warning("Death autocomplete received without guild id.");
            await interaction.RespondAsync(Array.Empty<AutocompleteResult>());
            return;
        }

        var activeRun = this.runService.GetActiveRun(guildId);
        var results = AutocompleteHelper.CreateResults(
            activeRun.LinkGroups.Select(group => group.Route),
            AutocompleteHelper.GetCurrentValue(interaction));

        Log.Debug(
            "Death autocomplete returned {ResultCount} route suggestions for value '{CurrentValue}'. ExistingRoutes={ExistingRouteCount}.",
            results.Count,
            AutocompleteHelper.GetCurrentValue(interaction),
            activeRun.LinkGroups.Count);

        await interaction.RespondAsync(results);
    }
}
