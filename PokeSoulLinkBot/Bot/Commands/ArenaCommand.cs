using Discord;
using Discord.WebSocket;
using PokeSoulLinkBot.Bot.Helpers;

namespace PokeSoulLinkBot.Bot.Commands;

/// <summary>
/// Handles the "arena" slash command.
/// </summary>
public sealed class ArenaCommand : ISlashCommand
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<int, string>> ArenaLeadersByEdition =
        new Dictionary<string, IReadOnlyDictionary<int, string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["ruby"] = new Dictionary<int, string>
            {
                [1] = "Felizia",
                [2] = "Kamillo",
                [3] = "Walter",
                [4] = "Flavia",
                [5] = "Norman",
                [6] = "Wibke",
                [7] = "Ben und Svenja",
                [8] = "Wassili",
            },
        };

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
        var leaderName = GetArenaLeaderName(edition, arenaNumber);

        await command.RespondAsync(
            $"Angefragt: **{edition}**, Arena **{arenaNumber}** ({leaderName}).{Environment.NewLine}" +
            $"In dieser Arena gibt es Pokemon auf dem Level: {joinedLevels}");
    }

    private static string GetArenaLeaderName(string edition, int arenaNumber)
    {
        if (ArenaLeadersByEdition.TryGetValue(edition, out var leadersByArena) &&
            leadersByArena.TryGetValue(arenaNumber, out var leaderName))
        {
            return leaderName;
        }

        return "Arenaleiter unbekannt";
    }
}
