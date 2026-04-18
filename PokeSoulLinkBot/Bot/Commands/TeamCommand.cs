using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "team" slash command.
/// </summary>
public class TeamCommand : ISlashCommand
{
    private readonly IRunService runService;
    private readonly EmbedFactory embedFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="TeamCommand"/> class.
    /// </summary>
    /// <param name="runService">The run service.</param>
    /// <param name="embedFactory">The embed factory.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when one of the parameters is <see langword="null"/>.
    /// </exception>
    public TeamCommand(
        IRunService runService,
        EmbedFactory embedFactory)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
    }

    /// <inheritdoc />
    public string CommandName => "team";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName("team")
            .WithDescription("Show the currently used pokemons.")
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var guildId = CommandOptionHelper.GetGuildId(command);

        var activeRun = this.runService.GetActiveRun(guildId);
        var message = this.embedFactory.CreateTeamMessage(activeRun);
        await command.RespondAsync(message);
    }
}
