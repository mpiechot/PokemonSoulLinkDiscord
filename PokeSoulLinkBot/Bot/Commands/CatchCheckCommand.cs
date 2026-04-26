using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "catch-check" slash command.
/// </summary>
public sealed class CatchCheckCommand : ISlashCommand
{
    private readonly ICatchEligibilityService catchEligibilityService;
    private readonly EmbedFactory embedFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="CatchCheckCommand"/> class.
    /// </summary>
    /// <param name="catchEligibilityService">The catch eligibility service.</param>
    /// <param name="embedFactory">The embed factory.</param>
    public CatchCheckCommand(
        ICatchEligibilityService catchEligibilityService,
        EmbedFactory embedFactory)
    {
        this.catchEligibilityService = catchEligibilityService ?? throw new ArgumentNullException(nameof(catchEligibilityService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
    }

    /// <inheritdoc />
    public string CommandName => "catch-check";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Checks whether a Pokémon may still be caught in the active run.")
            .AddOption("pokemon", ApplicationCommandOptionType.String, "The Pokémon name.", isRequired: true)
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var guildId = CommandOptionHelper.GetGuildId(command);
        var pokemonName = CommandOptionHelper.GetRequiredStringOption(command, "pokemon");
        var result = await this.catchEligibilityService.CheckCatchAsync(guildId, pokemonName);
        var embed = this.embedFactory.CreateCatchCheckEmbed(result);

        await SlashCommandResponse.SendAsync(command, embed: embed);
    }
}
