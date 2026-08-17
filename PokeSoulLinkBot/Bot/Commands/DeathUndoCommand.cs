using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;

namespace PokeSoulLinkBot.Bot.Commands;

public sealed class DeathUndoCommand : ISlashCommand
{
    private readonly IRunService runService;
    private readonly EmbedFactory embedFactory;
    private readonly IGameDataCatalogService gameDataCatalogService;

    public DeathUndoCommand(IRunService runService, EmbedFactory embedFactory, IGameDataCatalogService gameDataCatalogService)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
        this.gameDataCatalogService = gameDataCatalogService ?? throw new ArgumentNullException(nameof(gameDataCatalogService));
    }

    public string CommandName => "death-undo";

    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Macht einen Tod oder den Verlust einer Route rückgängig.")
            .AddOption("route", ApplicationCommandOptionType.String, "Die Route oder das Gebiet.", isRequired: true, isAutocomplete: true)
            .Build();
    }

    public async Task HandleAsync(SocketSlashCommand command, ISlashCommandResponse response)
    {
        var guildId = CommandOptionHelper.GetGuildId(command);
        var route = CommandOptionHelper.GetRequiredStringOption(command, "route");
        var linkGroup = this.runService.UndoDeath(guildId, route);
        var detail = linkGroup.Entries.Count == 0
            ? "Die Route kann wieder für eine Begegnung verwendet werden."
            : "Die Link-Gruppe ist wieder lebendig und kann verwendet werden.";

        await response.SendAsync(embed: this.embedFactory.CreateActionEmbed("Tod rückgängig", $"**{linkGroup.Route}** wurde wiederhergestellt.\n{detail}"));
    }

    public async Task HandleAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        var guildId = interaction.GuildId?.ToString();
        if (string.IsNullOrWhiteSpace(guildId))
        {
            await interaction.RespondAsync(Array.Empty<AutocompleteResult>());
            return;
        }

        var activeRun = this.runService.GetActiveRun(guildId);
        var catalogRoutes = await this.gameDataCatalogService.GetRoutesAsync(activeRun.Game);
        var routes = activeRun.LinkGroups.Select(group => group.Route).Concat(catalogRoutes);
        await interaction.RespondAsync(AutocompleteHelper.CreateResults(routes, AutocompleteHelper.GetCurrentValue(interaction)));
    }
}
