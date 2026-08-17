using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;
using PokeSoulLinkBot.Core.Models;
using Serilog;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "arenas" slash command.
/// </summary>
public sealed class ArenasCommand : ISlashCommand
{
    private readonly IArenaInfoService arenaInfoService;
    private readonly EmbedFactory embedFactory;
    private readonly EmbedImageFactory embedImageFactory;
    private readonly IGameDataCatalogService gameDataCatalogService;
    private readonly IRunService runService;

    public ArenasCommand(
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

    public string CommandName => "arenas";

    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Zeigt den Fortschritt und die Level aller Arenen.")
            .AddOption("edition", ApplicationCommandOptionType.String, "Die Edition, falls sie vom aktuellen Run abweicht.", isRequired: false, isAutocomplete: true)
            .Build();
    }

    public async Task HandleAsync(SocketSlashCommand command, ISlashCommandResponse response)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(response);

        var guildId = CommandOptionHelper.GetGuildId(command);
        var activeRun = this.runService.GetActiveRun(guildId);
        var edition = CommandOptionHelper.GetOptionalStringOption(command, "edition")?.Trim()
            ?? activeRun.Game;
        var arenaInfos = await Task.WhenAll(
            Enumerable.Range(1, 8).Select(number => this.arenaInfoService.GetArenaInfoAsync(edition, number)));        var completedArenas = activeRun.CompletedArenas
            .Where(arena => string.Equals(arena.Edition, edition, StringComparison.OrdinalIgnoreCase))
            .Select(arena => arena.ArenaNumber)
            .ToHashSet();
        var image = this.embedImageFactory.CreateArenaImage();
        var embed = this.embedFactory.CreateArenasOverviewEmbed(edition, arenaInfos, completedArenas, image.AttachmentUrl);

        await response.SendFileAsync(image.FileAttachment, embed: embed);
    }

    public async Task HandleAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var editions = await this.gameDataCatalogService.GetEditionsAsync();
        var results = AutocompleteHelper.CreateResults(
            editions.Select(edition => edition.DisplayName),
            AutocompleteHelper.GetCurrentValue(interaction));

        Log.Debug("Arenas autocomplete returned {ResultCount} edition suggestions.", results.Count);
        await interaction.RespondAsync(results);
    }
}
