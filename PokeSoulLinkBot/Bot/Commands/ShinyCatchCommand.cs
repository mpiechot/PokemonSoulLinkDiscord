using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Registers extra shiny catches in the active run.
/// </summary>
public sealed class ShinyCatchCommand : ISlashCommand
{
    private readonly IRunService runService;
    private readonly EmbedFactory embedFactory;
    private readonly IPokemonLookupService pokemonLookupService;
    private readonly IGameDataCatalogService gameDataCatalogService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ShinyCatchCommand"/> class.
    /// </summary>
    /// <param name="runService">The run service.</param>
    /// <param name="embedFactory">The response formatter.</param>
    /// <param name="pokemonLookupService">The Pokémon lookup service.</param>
    /// <param name="gameDataCatalogService">The route catalog.</param>
    public ShinyCatchCommand(
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

    /// <inheritdoc />
    public string CommandName => "shiny-catch";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Registriert ein zusätzliches Shiny, ohne den Routenfang zu verbrauchen.")
            .AddOption("player", ApplicationCommandOptionType.User, "Der Besitzer des Shinys.", isRequired: true)
            .AddOption("pokemon", ApplicationCommandOptionType.String, "Der Pokémonname.", isRequired: true)
            .AddOption("route", ApplicationCommandOptionType.String, "Optionale Fundroute; unabhängig von der späteren Link-Route.", isRequired: false, isAutocomplete: true)
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command, ISlashCommandResponse response)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(response);

        var guildId = CommandOptionHelper.GetGuildId(command);
        var route = command.Data.Options.FirstOrDefault(option => option.Name == "route")?.Value?.ToString();
        var player = CommandOptionHelper.GetRequiredUserOption(command, "player");
        var pokemon = CommandOptionHelper.GetRequiredStringOption(command, "pokemon");
        var pokemonInfo = await this.pokemonLookupService.GetPokemonInfoAsync(pokemon)
            ?? throw new InvalidOperationException($"Pokémon '{pokemon}' wurde nicht gefunden.");
        var shiny = this.runService.RegisterShiny(guildId, route, player.Id, player.Username, pokemon, pokemonInfo.Types);

        var description = $"**{shiny.PlayerName}**: **{shiny.PokemonName}** (Fundroute: **{shiny.ShinyCaughtRoute ?? "nicht angegeben"}**). " +
            $"Mit /shiny-swap auf einer lebenden Link-Route einsetzen. Shiny-ID: `{shiny.ShinyId}`";
        await response.SendAsync(embed: this.embedFactory.CreateActionEmbed("✨ Shiny gefangen", description));
        await response.SendFollowupsAsync(this.embedFactory.CreateStatusMessages(this.runService.GetActiveRun(guildId)));
    }

    /// <inheritdoc />
    public async Task HandleAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        var guildId = interaction.GuildId?.ToString();
        if (string.IsNullOrWhiteSpace(guildId))
        {
            await interaction.RespondAsync(Array.Empty<AutocompleteResult>());
            return;
        }

        var run = this.runService.GetActiveRun(guildId);
        var routes = await this.gameDataCatalogService.GetRoutesAsync(run.Game);
        await interaction.RespondAsync(AutocompleteHelper.CreateResults(
            routes.Concat(run.LinkGroups.Select(group => group.Route)),
            AutocompleteHelper.GetCurrentValue(interaction)));
    }
}
