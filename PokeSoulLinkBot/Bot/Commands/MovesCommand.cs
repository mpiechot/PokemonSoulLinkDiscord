using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Bot.Helpers;
using PokeSoulLinkBot.Bot.Presentation;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "moves" slash command.
/// </summary>
public sealed class MovesCommand : ISlashCommand
{
    private readonly IPokedexService pokedexService;
    private readonly PokemonMoveLearnsetPresenter presenter;

    /// <summary>
    /// Initializes a new instance of the <see cref="MovesCommand"/> class.
    /// </summary>
    public MovesCommand(IPokedexService pokedexService, PokemonMoveLearnsetPresenter presenter)
    {
        this.pokedexService = pokedexService ?? throw new ArgumentNullException(nameof(pokedexService));
        this.presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
    }

    /// <inheritdoc />
    public string CommandName => "moves";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Shows level-up moves and TM/HM compatibility for a Pokémon.")
            .AddOption("pokemon", ApplicationCommandOptionType.String, "The Pokémon name.", isRequired: true)
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command, ISlashCommandResponse response)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(response);

        var pokemonName = CommandOptionHelper.GetRequiredStringOption(command, "pokemon");
        var learnset = await this.pokedexService.GetMoveLearnsetAsync(pokemonName);
        var messages = this.presenter.CreateTableMessages(learnset, pokemonName);

        await response.SendAsync(messages[0]);
        await response.SendFollowupsAsync(messages.Skip(1));
    }
}
