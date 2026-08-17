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
    /// Creates the first page of a move learnset table.
    /// </summary>
    public string CreateTableMessage(PokemonMoveLearnset learnset)
    {
        return this.CreateTableMessages(learnset).First();
    }

    /// <summary>
    /// Creates Discord-compatible pages for a move learnset table.
    /// </summary>
    public IReadOnlyList<string> CreateTableMessages(PokemonMoveLearnset learnset)
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

        var pages = new List<string>();
        var currentLines = new List<string>();
        foreach (var line in lines)
        {
            var candidateLines = currentLines.Append(line);
            var candidate = $"```{string.Join(Environment.NewLine, candidateLines)}```";
            if (candidate.Length > 2000 && currentLines.Count > 0)
            {
                pages.Add($"```{string.Join(Environment.NewLine, currentLines)}```");
                currentLines.Clear();
            }

            currentLines.Add(line.Length > 1994 ? line[..1994] : line);
        }

        if (currentLines.Count > 0)
        {
            pages.Add($"```{string.Join(Environment.NewLine, currentLines)}```");
        }

        return pages;
    }
}
