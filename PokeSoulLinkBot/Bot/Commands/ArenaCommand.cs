using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Bot.Helpers;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "arena" slash command.
/// </summary>
public sealed class ArenaCommand : ISlashCommand
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, IReadOnlyList<int>>> ArenaLevelsByEdition =
        new Dictionary<string, IReadOnlyDictionary<int, IReadOnlyList<int>>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ruby"] = new Dictionary<int, IReadOnlyList<int>>
            {
                [1] = new[] { 15, 14, 14 },
                [2] = new[] { 19, 18, 17 },
                [3] = new[] { 24, 22, 20, 20 },
                [4] = new[] { 29, 26, 24, 24 },
                [5] = new[] { 31, 29, 27, 27 },
                [6] = new[] { 33, 31, 29, 29 },
                [7] = new[] { 41, 41 },
                [8] = new[] { 43, 42, 40, 40 },
            },
        };

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

        var edition = CommandOptionHelper.GetRequiredStringOption(command, "edition");
        var arenaNumber = CommandOptionHelper.GetRequiredIntegerOption(command, "number");

        if (!ArenaLevelsByEdition.TryGetValue(edition, out var arenaLevels))
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
