using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Bot.Helpers;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "swap" slash command.
/// </summary>
public sealed class SwapCommand : ISlashCommand
{
    private readonly IRunService runService;
    private readonly EmbedFactory embedFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="SwapCommand"/> class.
    /// </summary>
    /// <param name="runService">The run service.</param>
    /// <param name="embedFactory">The embed factory.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when one of the parameters is <see langword="null"/>.
    /// </exception>
    public SwapCommand(IRunService runService, EmbedFactory embedFactory)
    {
        this.runService = runService ?? throw new ArgumentNullException(nameof(runService));
        this.embedFactory = embedFactory ?? throw new ArgumentNullException(nameof(embedFactory));
    }

    /// <inheritdoc />
    public string CommandName => "swap";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Tauscht eine Team-Route gegen eine Box-Route.")
            .AddOption("team-route", ApplicationCommandOptionType.String, "Die Route, die aktuell im Team ist.", isRequired: true)
            .AddOption("box-route", ApplicationCommandOptionType.String, "Die Route, die aus der Box ins Team soll.", isRequired: true)
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var guildId = CommandOptionHelper.GetGuildId(command);
        var teamRoute = CommandOptionHelper.GetRequiredStringOption(command, "team-route").ToLowerInvariant().Trim();
        var boxRoute = CommandOptionHelper.GetRequiredStringOption(command, "box-route").ToLowerInvariant().Trim();

        var activeRun = this.runService.SwapRoute(guildId, teamRoute, boxRoute);
        var message = this.embedFactory.CreateTeamMessage(activeRun);

        await command.RespondAsync(message);
    }
}
