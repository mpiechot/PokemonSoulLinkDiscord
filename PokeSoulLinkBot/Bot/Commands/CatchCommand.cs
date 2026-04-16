using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;
using PokeSoulLinkBot.Core.Models;

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

    /// <summary>
    /// Initializes a new instance of the <see cref="CatchCommand"/> class.
    /// </summary>
    /// <param name="runService">The run service.</param>
    /// <param name="embedFactory">The embed factory.</param>
    /// <param name="embedImageFactory">The embed image factory.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when one of the parameters is <see langword="null"/>.
    /// </exception>
    public CatchCommand(
        IRunService runService,
        EmbedFactory embedFactory,
        EmbedImageFactory embedImageFactory,
        IPokemonLookupService pokemonLookupService)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
        this.embedImageFactory = embedImageFactory ?? throw new ArgumentNullException(nameof(embedImageFactory));
        this.pokemonLookupService = pokemonLookupService ?? throw new ArgumentNullException(nameof(pokemonLookupService));
    }

    /// <inheritdoc />
    public string CommandName => "catch";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(CommandName)
            .WithDescription("Register a caught Pokémon for a route.")
            .AddOption("route", ApplicationCommandOptionType.String, "The route or area.", isRequired: true)
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

        var linkGroup = this.runService.RegisterCatch(guildId, route, player.Id, player.Username, pokemon);
        var activeRun = this.runService.GetActiveRun(guildId);

        activeRun.TryAddToActive(linkGroup);

        var pokemonInfo = await this.pokemonLookupService.GetPokemonInfoAsync(pokemon);

        var image = this.embedImageFactory.CreateCatchImage();
        var catchEmbed = this.embedFactory.CreateCatchRegisteredEmbed(
            route,
            player.Username,
            pokemon,
            linkGroup.Entries.Count,
            activeRun.Players.Count,
            pokemonInfo);
        var statusMessage = this.embedFactory.CreateStatusMessage(activeRun);

        await command.RespondWithFileAsync(image.FileAttachment, embed: catchEmbed, text: statusMessage);
    }
}
