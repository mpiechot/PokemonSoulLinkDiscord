using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;
using Serilog;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "arena-complete" slash command.
/// </summary>
public sealed class ArenaCompleteCommand : ISlashCommand
{
    private readonly IArenaInfoService arenaInfoService;
    private readonly EmbedFactory embedFactory;
    private readonly EmbedImageFactory embedImageFactory;
    private readonly IGameDataCatalogService gameDataCatalogService;
    private readonly IRunService runService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArenaCompleteCommand"/> class.
    /// </summary>
    /// <param name="arenaInfoService">The arena info service.</param>
    /// <param name="embedFactory">The embed factory.</param>
    /// <param name="embedImageFactory">The embed image factory.</param>
    /// <param name="gameDataCatalogService">The game data catalog service.</param>
    /// <param name="runService">The run service.</param>
    public ArenaCompleteCommand(
        IArenaInfoService arenaInfoService,
        EmbedFactory embedFactory,
        EmbedImageFactory embedImageFactory,
        IGameDataCatalogService gameDataCatalogService,
        IRunService runService)
    {
        this.arenaInfoService = arenaInfoService ?? throw new ArgumentNullException(nameof(arenaInfoService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
        this.embedImageFactory = embedImageFactory ?? throw new ArgumentNullException(nameof(embedImageFactory));
        this.gameDataCatalogService = gameDataCatalogService ?? throw new ArgumentNullException(nameof(gameDataCatalogService));
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
    }

    /// <inheritdoc />
    public string CommandName => "arena-complete";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Markiert eine Arena im aktuellen Run als erledigt.")
            .AddOption("number", ApplicationCommandOptionType.Integer, "Die Arena-Nummer (1-8).", isRequired: true)
            .AddOption("edition", ApplicationCommandOptionType.String, "Die Edition, falls sie vom aktuellen Run abweicht.", isRequired: false, isAutocomplete: true)
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command, ISlashCommandResponse response)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(response);

        var guildId = CommandOptionHelper.GetGuildId(command);
        var activeRun = this.runService.GetActiveRun(guildId);
        var edition = CommandOptionHelper.GetOptionalStringOption(command, "edition")?.Trim()
            ?? activeRun.Game;
        var arenaNumber = CommandOptionHelper.GetRequiredIntegerOption(command, "number");

        var arenaInfo = await this.arenaInfoService.GetArenaInfoAsync(edition, arenaNumber);
        var completedArena = this.runService.CompleteArena(
            guildId,
            arenaNumber,
            edition,
            arenaInfo.LeaderName,
            arenaInfo.Location);
        var image = this.embedImageFactory.CreateArenaImage();
        var embed = this.embedFactory.CreateArenaCompletedEmbed(completedArena, activeRun, image.AttachmentUrl);

        await response.SendFileAsync(image.FileAttachment, embed: embed);
    }

    /// <inheritdoc />
    public async Task HandleAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var editions = await this.gameDataCatalogService.GetEditionsAsync();
        var results = AutocompleteHelper.CreateResults(
            editions.Select(edition => edition.DisplayName),
            AutocompleteHelper.GetCurrentValue(interaction));

        Log.Debug(
            "Arena complete autocomplete returned {ResultCount} edition suggestions for value '{CurrentValue}'.",
            results.Count,
            AutocompleteHelper.GetCurrentValue(interaction));

        await interaction.RespondAsync(results);
    }
}
