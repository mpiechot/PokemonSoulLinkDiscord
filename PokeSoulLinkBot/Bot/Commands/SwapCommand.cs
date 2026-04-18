using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;
using Serilog;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "swap" slash command.
/// </summary>
public sealed class SwapCommand : ISlashCommand
{
    private readonly IRunService runService;
    private readonly EmbedFactory embedFactory;
    private readonly EmbedImageFactory embedImageFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SwapCommand"/> class.
    /// </summary>
    /// <param name="runService">The run service.</param>
    /// <param name="embedFactory">The embed factory.</param>
    /// <param name="embedImageFactory">The embed image factory.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when one of the parameters is <see langword="null"/>.
    /// </exception>
    public SwapCommand(
        IRunService runService,
        EmbedFactory embedFactory,
        EmbedImageFactory embedImageFactory)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
        this.embedImageFactory = embedImageFactory ?? throw new ArgumentNullException(nameof(embedImageFactory));
    }

    /// <inheritdoc />
    public string CommandName => "swap";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Tauscht eine Team-Route gegen eine Box-Route.")
            .AddOption("team-route", ApplicationCommandOptionType.String, "Die Route, die aktuell im Team ist.", isRequired: true, isAutocomplete: true)
            .AddOption("box-route", ApplicationCommandOptionType.String, "Die Route, die aus der Box ins Team soll.", isRequired: true, isAutocomplete: true)
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var guildId = CommandOptionHelper.GetGuildId(command);
        var teamRoute = CommandOptionHelper.GetRequiredStringOption(command, "team-route").ToLowerInvariant().Trim();
        var boxRoute = CommandOptionHelper.GetRequiredStringOption(command, "box-route").ToLowerInvariant().Trim();

        var activeRun = this.runService.SwapRoute(guildId, teamRoute, boxRoute);
        var message = this.embedFactory.CreateTeamMessage(activeRun);
        var image = this.embedImageFactory.CreateSwapImage();
        var embed = this.embedFactory.CreateRunSummaryEmbed("Team Swapped", activeRun, image.AttachmentUrl);

        await command.RespondWithFileAsync(image.FileAttachment, text: message, embed: embed);
    }

    /// <inheritdoc />
    public async Task HandleAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var guildId = interaction.GuildId?.ToString();
        if (string.IsNullOrWhiteSpace(guildId))
        {
            Log.Warning("Swap autocomplete received without guild id.");
            await interaction.RespondAsync(Array.Empty<AutocompleteResult>());
            return;
        }

        var activeRun = this.runService.GetActiveRun(guildId);
        var activeRoutes = activeRun.ActiveLinks
            .Where(linkGroup => linkGroup is not null)
            .Select(linkGroup => linkGroup!.Route)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var routes = string.Equals(interaction.Data.Current.Name, "team-route", StringComparison.OrdinalIgnoreCase)
            ? activeRoutes
            : activeRun.LinkGroups
                .Where(linkGroup => linkGroup.IsAlive && !activeRoutes.Contains(linkGroup.Route))
                .Select(linkGroup => linkGroup.Route);

        var results = AutocompleteHelper.CreateResults(
            routes,
            AutocompleteHelper.GetCurrentValue(interaction));

        Log.Debug(
            "Swap autocomplete returned {ResultCount} route suggestions for option {OptionName} and value '{CurrentValue}'. ActiveRoutes={ActiveRouteCount}, LinkGroups={LinkGroupCount}.",
            results.Count,
            interaction.Data.Current.Name,
            AutocompleteHelper.GetCurrentValue(interaction),
            activeRoutes.Count,
            activeRun.LinkGroups.Count);

        await interaction.RespondAsync(results);
    }
}
