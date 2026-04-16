using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Bot.Helpers;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "arena" slash command.
/// </summary>
public sealed class ArenaCommand : ISlashCommand
{
    private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, IReadOnlyList<int>>> arenaLevelsByEdition;

    /// <summary>
    /// Initializes a new instance of the <see cref="ArenaCommand"/> class.
    /// </summary>
    /// <param name="arenaLevelsByEdition">Arena levels grouped by edition and arena number.</param>
    public ArenaCommand(IReadOnlyDictionary<string, IReadOnlyDictionary<int, IReadOnlyList<int>>> arenaLevelsByEdition)
    {
        this.arenaLevelsByEdition = arenaLevelsByEdition ?? throw new ArgumentNullException(nameof(arenaLevelsByEdition));
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

        var edition = CommandOptionHelper.GetRequiredStringOption(command, "edition").Trim().ToLowerInvariant();
        var arenaNumber = CommandOptionHelper.GetRequiredIntegerOption(command, "number");

        if (!this.arenaLevelsByEdition.TryGetValue(edition, out var arenaLevels))
        {
            await command.RespondAsync($"Für die Edition '{edition}' sind keine Arenadaten vorhanden.", ephemeral: true);
            return;
        }

        if (!arenaLevels.TryGetValue(arenaNumber, out var levels))
        {
            await command.RespondAsync($"Für die Arena '{arenaNumber}' gibt es in '{edition}' keine Daten.", ephemeral: true);
            return;
        }

        var joinedLevels = string.Join(", ", levels);
        await command.RespondAsync($"In dieser Arena gibt es Pokemon auf dem Level: {joinedLevels}");
    }
}
