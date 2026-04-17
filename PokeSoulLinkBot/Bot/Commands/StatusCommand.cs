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
public class StatusCommand : ISlashCommand
{
    private readonly IRunService runService;
    private readonly EmbedFactory embedFactory;
    private readonly EmbedImageFactory embedImageFactory;
    private readonly IPokemonLookupService pokemonLookupService;

    /// <summary>
    /// Initializes a new instance of the <see cref="StatusCommand"/> class.
    /// </summary>
    /// <param name="runService">The run service.</param>
    /// <param name="embedFactory">The embed factory.</param>
    /// <param name="embedImageFactory">The embed image factory.</param>
    /// <param name="pokemonLookupService">The Pokémon lookup service.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when one of the parameters is <see langword="null"/>.
    /// </exception>
    public StatusCommand(
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
    public string CommandName => "status";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName("status")
            .WithDescription("Show the current run status.")
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var guildId = CommandOptionHelper.GetGuildId(command);

        var activeRun = this.runService.GetActiveRun(guildId);
        await this.EnrichMissingPokemonTypesAsync(activeRun);

        var image = this.embedImageFactory.CreateStatusImage();
        var message = this.embedFactory.CreateStatusMessage(activeRun);
        await command.RespondAsync(message);
    }

    private async Task EnrichMissingPokemonTypesAsync(SoulLinkRun run)
    {
        foreach (var entry in run.LinkGroups.SelectMany(group => group.Entries))
        {
            if (entry.Types.Count > 0)
            {
                continue;
            }

            var pokemonInfo = await this.pokemonLookupService.GetPokemonInfoAsync(entry.PokemonName);
            if (pokemonInfo?.Types.Count > 0)
            {
                entry.Types = pokemonInfo.Types.ToList();
            }
        }
    }
}
