using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;

namespace PokeSoulLinkBot.Bot.Commands;

public sealed class CatchRemoveCommand : ISlashCommand
{
    private readonly IRunService runService;
    private readonly EmbedFactory embedFactory;
    private readonly IGameDataCatalogService gameDataCatalogService;

    public CatchRemoveCommand(IRunService runService, EmbedFactory embedFactory, IGameDataCatalogService gameDataCatalogService)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
        this.gameDataCatalogService = gameDataCatalogService ?? throw new ArgumentNullException(nameof(gameDataCatalogService));
    }

    public string CommandName => "catch-remove";

    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Entfernt einen Fang aus einer Link-Gruppe.")
            .AddOption("route", ApplicationCommandOptionType.String, "Die Route oder das Gebiet.", isRequired: true, isAutocomplete: true)
            .AddOption("player", ApplicationCommandOptionType.User, "Der Spieler des Eintrags.", isRequired: true)
            .Build();
    }

    public async Task HandleAsync(SocketSlashCommand command, ISlashCommandResponse response)
    {
        var guildId = CommandOptionHelper.GetGuildId(command);
        var route = CommandOptionHelper.GetRequiredStringOption(command, "route");
        var player = CommandOptionHelper.GetRequiredUserOption(command, "player");
        var linkGroup = this.runService.RemoveCatch(guildId, route, player.Id);
        var detail = linkGroup.Entries.Count == 0
            ? $"Die leere Route **{linkGroup.Route}** wurde ebenfalls aus dem Run entfernt."
            : $"Die Link-Gruppe enthält noch {linkGroup.Entries.Count} Fang/Fänge.";

        await response.SendAsync(embed: this.embedFactory.CreateActionEmbed(
            "Fang entfernt",
            $"Der Eintrag von **{player.Username}** auf **{linkGroup.Route}** wurde entfernt.\n{detail}"));
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
