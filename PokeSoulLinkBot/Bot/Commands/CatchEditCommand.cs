using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;

namespace PokeSoulLinkBot.Bot.Commands;

public sealed class CatchEditCommand : ISlashCommand
{
    private readonly IRunService runService;
    private readonly EmbedFactory embedFactory;
    private readonly IPokemonLookupService pokemonLookupService;
    private readonly IGameDataCatalogService gameDataCatalogService;

    public CatchEditCommand(
        IRunService runService,
        EmbedFactory embedFactory,
        IPokemonLookupService pokemonLookupService,
        IGameDataCatalogService gameDataCatalogService)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
        this.pokemonLookupService = pokemonLookupService ?? throw new ArgumentNullException(nameof(pokemonLookupService));
        this.gameDataCatalogService = gameDataCatalogService ?? throw new ArgumentNullException(nameof(gameDataCatalogService));
    }

    public string CommandName => "catch-edit";

    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Korrigiert ein Pokémon in einer bestehenden Link-Gruppe.")
            .AddOption("route", ApplicationCommandOptionType.String, "Die Route oder das Gebiet.", isRequired: true, isAutocomplete: true)
            .AddOption("player", ApplicationCommandOptionType.User, "Der Spieler des Eintrags.", isRequired: true)
            .AddOption("pokemon", ApplicationCommandOptionType.String, "Der korrigierte Pokémonname.", isRequired: true)
            .Build();
    }

    public async Task HandleAsync(SocketSlashCommand command, ISlashCommandResponse response)
    {
        var guildId = CommandOptionHelper.GetGuildId(command);
        var route = CommandOptionHelper.GetRequiredStringOption(command, "route");
        var player = CommandOptionHelper.GetRequiredUserOption(command, "player");
        var pokemon = CommandOptionHelper.GetRequiredStringOption(command, "pokemon");
        var pokemonInfo = await this.pokemonLookupService.GetPokemonInfoAsync(pokemon)
            ?? throw new InvalidOperationException($"Pokémon '{pokemon}' wurde nicht gefunden.");
        var linkGroup = this.runService.EditCatch(guildId, route, player.Id, pokemon, pokemonInfo.Types);

        await response.SendAsync(embed: this.embedFactory.CreateActionEmbed(
            "Fang korrigiert",
            $"Der Eintrag von **{player.Username}** auf **{linkGroup.Route}** wurde auf **{pokemon}** geändert."));
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
