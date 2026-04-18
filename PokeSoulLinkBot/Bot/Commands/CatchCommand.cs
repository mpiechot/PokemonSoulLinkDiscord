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
public class CatchCommand : ISlashCommand
{
    private readonly IRunService runService;
    private readonly EmbedFactory embedFactory;
    private readonly EmbedImageFactory embedImageFactory;
    private readonly IPokemonLookupService pokemonLookupService;
    private readonly IGameDataCatalogService gameDataCatalogService;

    /// <summary>
    /// Initializes a new instance of the <see cref="CatchCommand"/> class.
    /// </summary>
    /// <param name="runService">The run service.</param>
    /// <param name="embedFactory">The embed factory.</param>
    /// <param name="embedImageFactory">The embed image factory.</param>
    /// <param name="gameDataCatalogService">The game data catalog service.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when one of the parameters is <see langword="null"/>.
    /// </exception>
    public CatchCommand(
        IRunService runService,
        EmbedFactory embedFactory,
        EmbedImageFactory embedImageFactory,
        IPokemonLookupService pokemonLookupService,
        IGameDataCatalogService gameDataCatalogService)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
        this.embedImageFactory = embedImageFactory ?? throw new ArgumentNullException(nameof(embedImageFactory));
        this.pokemonLookupService = pokemonLookupService ?? throw new ArgumentNullException(nameof(pokemonLookupService));
        this.gameDataCatalogService = gameDataCatalogService ?? throw new ArgumentNullException(nameof(gameDataCatalogService));
    }

    /// <inheritdoc />
    public string CommandName => "catch";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Register a caught Pokémon for a route.")
            .AddOption("route", ApplicationCommandOptionType.String, "The route or area.", isRequired: true, isAutocomplete: true)
            .AddOption("player", ApplicationCommandOptionType.User, "The player.", isRequired: true)
            .AddOption("pokemon", ApplicationCommandOptionType.String, "The Pokémon name.", isRequired: true)
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var guildId = CommandOptionHelper.GetGuildId(command);

        var route = CommandOptionHelper.GetRequiredStringOption(command, "route").ToLowerInvariant().Trim();
        var player = CommandOptionHelper.GetRequiredUserOption(command, "player");
        var pokemon = CommandOptionHelper.GetRequiredStringOption(command, "pokemon");
        var pokemonInfo = await this.pokemonLookupService.GetPokemonInfoAsync(pokemon);
        var pokemonTypes = pokemonInfo?.Types ?? Array.Empty<string>();

        var linkGroup = this.runService.RegisterCatch(guildId, route, player.Id, player.Username, pokemon, pokemonTypes);
        var activeRun = this.runService.GetActiveRun(guildId);

        var image = this.embedImageFactory.CreateCatchImage();
        var catchEmbed = this.embedFactory.CreateCatchRegisteredEmbed(
            route,
            player.Username,
            pokemon,
            linkGroup.Entries.Count,
            activeRun.Players.Count,
            pokemonInfo);

        await command.RespondWithFileAsync(image.FileAttachment, embed: catchEmbed);

        var statusEmbed = this.embedFactory.CreateStatusEmbed(activeRun);
        await command.FollowupAsync(embed: statusEmbed);
    }

    /// <inheritdoc />
    public async Task HandleAutocompleteAsync(SocketAutocompleteInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        var guildId = interaction.GuildId?.ToString();
        if (string.IsNullOrWhiteSpace(guildId))
        {
            Log.Warning("Catch autocomplete received without guild id.");
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
            "Catch autocomplete returned {ResultCount} route suggestions for edition '{Edition}' and value '{CurrentValue}'. CatalogRoutes={CatalogRouteCount}, ExistingRoutes={ExistingRouteCount}.",
            results.Count,
            activeRun.Game,
            AutocompleteHelper.GetCurrentValue(interaction),
            catalogRoutes.Count,
            existingRoutes.Count());

        await interaction.RespondAsync(results);
    }
}
