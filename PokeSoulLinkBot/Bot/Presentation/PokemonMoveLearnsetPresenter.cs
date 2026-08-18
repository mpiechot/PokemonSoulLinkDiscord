using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Bot.Presentation;

/// <summary>
/// Creates compact Discord output for Pokémon move learnsets.
/// </summary>
public sealed class PokemonMoveLearnsetPresenter
{
    private const int DiscordMessageMaxLength = 2000;
    private const int MaxColumns = 4;
    private const int MaxTableWidth = 100;
    private const string ColumnSeparator = " | ";

    /// <summary>
    /// Creates the first page of a move learnset table.
    /// </summary>
    /// <param name="learnset">The move learnset to format.</param>
    /// <returns>The first formatted message.</returns>
    public string CreateTableMessage(PokemonMoveLearnset learnset)
    {
        ArgumentNullException.ThrowIfNull(learnset);

        return this.CreateTableMessages(learnset, learnset.PokemonName).First();
    }

    /// <summary>
    /// Creates Discord-compatible pages for a move learnset table.
    /// </summary>
    /// <param name="learnset">The move learnset to format.</param>
    /// <returns>The formatted messages.</returns>
    public IReadOnlyList<string> CreateTableMessages(PokemonMoveLearnset learnset)
    {
        ArgumentNullException.ThrowIfNull(learnset);

        return this.CreateTableMessages(learnset, learnset.PokemonName);
    }

    /// <summary>
    /// Creates Discord-compatible pages for a move learnset table.
    /// </summary>
    /// <param name="learnset">The move learnset to format.</param>
    /// <param name="requestedPokemonName">The Pokémon name entered by the user.</param>
    /// <returns>The formatted messages.</returns>
    public IReadOnlyList<string> CreateTableMessages(
        PokemonMoveLearnset learnset,
        string requestedPokemonName)
    {
        ArgumentNullException.ThrowIfNull(learnset);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPokemonName);

        var levelUpMoves = learnset.LevelUpMoves
            .OrderBy(move => move.Level)
            .ThenBy(move => move.MoveName, StringComparer.OrdinalIgnoreCase)
            .Select(move => $"Lv {move.Level}: {move.MoveName}")
            .ToList();
        var machineMoves = learnset.MachineMoves
            .OrderBy(move => move.MachineName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(move => move.MoveName, StringComparer.OrdinalIgnoreCase)
            .Select(move => $"{move.MachineName}: {move.MoveName}")
            .ToList();
        var blocks = new List<string>
        {
            $"**Attacken: {learnset.PokemonName}**{Environment.NewLine}" +
            $"Gesucht: {requestedPokemonName}",
        };

        blocks.AddRange(CreateTableSections("Level-Up", levelUpMoves, "Keine Level-Up-Attacken"));
        blocks.AddRange(CreateTableSections("TM/HM", machineMoves, "Keine TM/HM-Attacken"));

        return CombineBlocks(blocks);
    }

    private static IReadOnlyList<string> CreateTableSections(
        string title,
        IReadOnlyList<string> values,
        string emptyText)
    {
        if (values.Count == 0)
        {
            return new[] { $"**{title}**{Environment.NewLine}{emptyText}" };
        }

        var displayValues = values.Select(FormatCell).ToList();
        var cellWidth = displayValues.Max(value => value.Length);
        var columnCount = Math.Clamp(
            (MaxTableWidth + ColumnSeparator.Length) / (cellWidth + ColumnSeparator.Length),
            1,
            MaxColumns);
        var rows = displayValues
            .Chunk(columnCount)
            .Select(chunk => CreateTableRow(chunk, cellWidth))
            .ToList();
        var sections = new List<string>();
        var currentRows = new List<string>();

        foreach (var row in rows)
        {
            var candidateRows = currentRows.Append(row).ToList();
            if (currentRows.Count > 0 && CreateTableSection(title, candidateRows).Length > DiscordMessageMaxLength)
            {
                sections.Add(CreateTableSection(title, currentRows));
                currentRows.Clear();
            }

            currentRows.Add(row);
        }

        sections.Add(CreateTableSection(title, currentRows));
        return sections;
    }

    private static string CreateTableRow(IReadOnlyList<string> values, int cellWidth)
    {
        return string.Join(
            ColumnSeparator,
            values.Select((value, index) => index == values.Count - 1 ? value : value.PadRight(cellWidth)));
    }

    private static string FormatCell(string value)
    {
        var singleLineValue = value.ReplaceLineEndings(" ");
        return singleLineValue.Length <= MaxTableWidth
            ? singleLineValue
            : string.Concat(singleLineValue.AsSpan(0, MaxTableWidth - 3), "...");
    }

    private static string CreateTableSection(string title, IReadOnlyList<string> rows)
    {
        return
            $"**{title}**{Environment.NewLine}" +
            $"```{string.Join(Environment.NewLine, rows)}```";
    }

    private static IReadOnlyList<string> CombineBlocks(IReadOnlyList<string> blocks)
    {
        var messages = new List<string>();
        var currentBlocks = new List<string>();

        foreach (var block in blocks)
        {
            var candidate = string.Join(Environment.NewLine, currentBlocks.Append(block));
            if (currentBlocks.Count > 0 && candidate.Length > DiscordMessageMaxLength)
            {
                messages.Add(string.Join(Environment.NewLine, currentBlocks));
                currentBlocks.Clear();
            }

            currentBlocks.Add(block);
        }

        if (currentBlocks.Count > 0)
        {
            messages.Add(string.Join(Environment.NewLine, currentBlocks));
        }

        return messages;
    }
}
