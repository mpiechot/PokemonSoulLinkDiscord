using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Bot.Helpers;
using PokeSoulLinkBot.Bot.Presentation;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "pokedex" slash command.
/// </summary>
public sealed class PokedexCommand : ISlashCommand
{
    private readonly IPokedexService pokedexService;
    private readonly PokedexPresenter pokedexPresenter;

    /// <summary>
    /// Initializes a new instance of the <see cref="PokedexCommand"/> class.
    /// </summary>
    /// <param name="pokedexService">The Pokédex service.</param>
    /// <param name="pokedexPresenter">The Pokédex presenter.</param>
    public PokedexCommand(
        IPokedexService pokedexService,
        PokedexPresenter pokedexPresenter)
    {
        this.pokedexService = pokedexService ?? throw new ArgumentNullException(nameof(pokedexService));
        this.pokedexPresenter = pokedexPresenter ?? throw new ArgumentNullException(nameof(pokedexPresenter));
    }

    /// <inheritdoc />
    public string CommandName => "pokedex";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Shows Pokédex information for a Pokémon.")
            .AddOption("name", ApplicationCommandOptionType.String, "The Pokémon name.", isRequired: true)
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var pokemonName = CommandOptionHelper.GetRequiredStringOption(command, "name");
        var entry = await this.pokedexService.GetPokedexEntryAsync(pokemonName);

        var embed = this.pokedexPresenter.CreateEmbed(entry, pokemonName);
        var tableMessage = this.pokedexPresenter.CreateTableMessage(entry);

        await command.RespondAsync(tableMessage, embed: embed);
    }
}
