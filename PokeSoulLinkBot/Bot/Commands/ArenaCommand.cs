using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Bot.Helpers;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "arena" slash command.
/// </summary>
public sealed class ArenaCommand : ISlashCommand
{
    private readonly IArenaInfoService arenaInfoService;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArenaCommand"/> class.
    /// </summary>
    /// <param name="arenaInfoService">The arena info service.</param>
    public ArenaCommand(IArenaInfoService arenaInfoService)
    {
        this.arenaInfoService = arenaInfoService ?? throw new ArgumentNullException(nameof(arenaInfoService));
    }

    /// <inheritdoc />
    public string CommandName => "arena";

    /// <inheritdoc />
    public ApplicationCommandProperties BuildDefinition()
    {
        return new SlashCommandBuilder()
            .WithName(this.CommandName)
            .WithDescription("Zeigt die Level der Pokémon in einer Arena.")
            .AddOption("edition", ApplicationCommandOptionType.String, "Die Edition (z. B. ruby).", isRequired: true)
            .AddOption("number", ApplicationCommandOptionType.Integer, "Die Arena-Nummer (1-8).", isRequired: true)
            .Build();
    }

    /// <inheritdoc />
    public async Task HandleAsync(SocketSlashCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);

        var edition = CommandOptionHelper.GetRequiredStringOption(command, "edition").Trim();
        var arenaNumber = CommandOptionHelper.GetRequiredIntegerOption(command, "number");

        var arenaInfo = await this.arenaInfoService.GetArenaInfoAsync(edition, arenaNumber);
        var joinedLevels = string.Join(", ", arenaInfo.Levels);

        await command.RespondAsync(
            $"Angefragt: **{edition}**, Arena **{arenaNumber}** ({arenaInfo.LeaderName}, {arenaInfo.Location}).{Environment.NewLine}" +
            $"In dieser Arena gibt es Pokemon auf dem Level: {joinedLevels}");
    }
}
