using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;
using PokeSoulLinkBot.Core.Models;
using Serilog;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "route-death" slash command.
/// </summary>
public sealed class RouteDeathCommand : ISlashCommand
{
    private readonly IRunService runService;
    private readonly EmbedFactory embedFactory;
    private readonly EmbedImageFactory embedImageFactory;
    private readonly IGameDataCatalogService gameDataCatalogService;

    /// <summary>
    /// Initializes a new instance of the <see cref="RouteDeathCommand"/> class.
    /// </summary>
    /// <param name="runService">The run service.</param>
    /// <param name="embedFactory">The embed factory.</param>
    /// <param name="embedImageFactory">The embed image factory.</param>
    /// <param name="gameDataCatalogService">The game data catalog service.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when one of the parameters is <see langword="null"/>.
    /// </exception>
    public RouteDeathCommand(
        IRunService runService,
        EmbedFactory embedFactory,
        EmbedImageFactory embedImageFactory,
        IGameDataCatalogService gameDataCatalogService)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
        this.embedImageFactory = embedImageFactory ?? throw new ArgumentNullException(nameof(embedImageFactory));
        this.gameDataCatalogService = gameDataCatalogService ?? throw new ArgumentNullException(nameof(gameDataCatalogService));
    }

    /// <inheritdoc />
    public string CommandName => "route-death";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Mark a route as lost because the first encounter was not caught.")
            .AddOption("route", ApplicationCommandOptionType.String, "The route or area.", isRequired: true, isAutocomplete: true)
            .AddOption("reason", ApplicationCommandOptionType.String, "Why the encounter was lost.", isRequired: false)
            .AddOption("player", ApplicationCommandOptionType.User, "The player who missed the encounter.", isRequired: false)
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var guildId = CommandOptionHelper.GetGuildId(command);
        var route = CommandOptionHelper.GetRequiredStringOption(command, "route");
        var reason = CommandOptionHelper.GetOptionalStringOption(command, "reason");
        var player = CommandOptionHelper.GetOptionalUserOption(command, "player");

        var linkGroup = this.runService.MarkRouteLost(
            guildId,
            route,
            reason,
            player?.Id,
            player?.Username);
        var image = this.embedImageFactory.CreateDeathImage();
        var embed = this.embedFactory.CreateRouteLostEmbed(linkGroup, image.AttachmentUrl);

        await SlashCommandResponse.SendFileAsync(command, image.FileAttachment, embed: embed);
    }

    /// <inheritdoc />
    public async Task HandleAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var guildId = interaction.GuildId?.ToString();
        if (string.IsNullOrWhiteSpace(guildId))
        {
            Log.Warning("Route death autocomplete received without guild id.");
            await interaction.RespondAsync(Array.Empty<AutocompleteResult>());
            return;
        }

        var activeRun = this.runService.GetActiveRun(guildId);
        var catalogRoutes = await this.gameDataCatalogService.GetRoutesAsync(activeRun.Game);
        var existingRoutes = activeRun.LinkGroups.Select(group => group.Route);
        var results = AutocompleteHelper.CreateResults(
            catalogRoutes.Concat(existingRoutes),
            AutocompleteHelper.GetCurrentValue(interaction));

        Log.Debug(
            "Route death autocomplete returned {ResultCount} route suggestions for edition '{Edition}' and value '{CurrentValue}'. CatalogRoutes={CatalogRouteCount}, ExistingRoutes={ExistingRouteCount}.",
            results.Count,
            activeRun.Game,
            AutocompleteHelper.GetCurrentValue(interaction),
            catalogRoutes.Count,
            existingRoutes.Count());

        await interaction.RespondAsync(results);
    }
}
