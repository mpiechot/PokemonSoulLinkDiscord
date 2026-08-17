using Discord;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Bot.Presentation;

/// <summary>
/// Creates compact Discord output for Pokémon move learnsets.
/// </summary>
public sealed class PokemonMoveLearnsetPresenter
{
    /// <summary>
    /// Creates the move learnset embed.
    /// </summary>
    public Embed CreateEmbed(PokemonMoveLearnset learnset, string requestedPokemonName)
    {
        ArgumentNullException.ThrowIfNull(learnset);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPokemonName);

        return new EmbedBuilder()
            .WithTitle("Pokémon Moves")
            .AddField("Pokémon", learnset.PokemonName, true)
            .AddField("Requested", requestedPokemonName, true)
            .Build();
    }

    /// <summary>
    /// Creates a compact table containing level-up and TM/HM moves.
    /// </summary>
    public string CreateTableMessage(PokemonMoveLearnset learnset)
    {
        ArgumentNullException.ThrowIfNull(learnset);

        var lines = new List<string>
        {
            $"Pokémon: {learnset.PokemonName}",
            string.Empty,
            "Level-up",
        };

        lines.AddRange(learnset.LevelUpMoves.Count == 0
            ? new[] { "- Keine Level-Up-Attacken" }
            : learnset.LevelUpMoves
                .OrderBy(move => move.Level)
                .ThenBy(move => move.MoveName, StringComparer.OrdinalIgnoreCase)
                .Select(move => $"Lv {move.Level}: {move.MoveName}"));

        lines.Add(string.Empty);
        lines.Add("TM/HM");
        lines.AddRange(learnset.MachineMoves.Count == 0
            ? new[] { "- Keine TM/HM-Attacken" }
            : learnset.MachineMoves
                .OrderBy(move => move.MachineName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(move => move.MoveName, StringComparer.OrdinalIgnoreCase)
                .Select(move => $"{move.MachineName}: {move.MoveName}"));

        return $"```{string.Join(Environment.NewLine, lines)}```";
    }
}
