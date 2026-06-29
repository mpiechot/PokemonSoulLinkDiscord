using System.Globalization;
using System.Text;
using Discord;

namespace PokeSoulLinkBot.Bot.Helpers;

/// <summary>
/// Creates Discord autocomplete results from simple string collections.
/// </summary>
public static class AutocompleteHelper
{
    private const int MaxResults = 25;
    private const int MaxTextLength = 100;

    /// <summary>
    /// Filters and maps values to Discord autocomplete results.
    /// </summary>
    /// <param name="values">The values to suggest.</param>
    /// <param name="userInput">The current user input.</param>
    /// <returns>The autocomplete results.</returns>
    public static IReadOnlyCollection<AutocompleteResult> CreateResults(
        IEnumerable<string> values,
        string? userInput)
    {
        ArgumentNullException.ThrowIfNull(values);

        var input = NormalizeForSearch(userInput?.Trim() ?? string.Empty);
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(value => new ScoredAutocompleteValue(value, GetMatchScore(value, input)))
            .Where(value => value.MatchScore.HasValue)
            .OrderBy(value => value.MatchScore!.Value)
            .ThenBy(value => NormalizeForSearch(value.Value), StringComparer.Ordinal)
            .ThenBy(value => value.Value, StringComparer.OrdinalIgnoreCase)
            .Take(MaxResults)
            .Select(value => Truncate(value.Value))
            .Select(value => new AutocompleteResult(value, value))
            .ToList();
    }

    /// <summary>
    /// Reads the option that is currently being autocompleted.
    /// </summary>
    /// <param name="interaction">The autocomplete interaction.</param>
    /// <returns>The current user input as text.</returns>
    public static string GetCurrentValue(IAutocompleteInteraction interaction)
    {
        ArgumentNullException.ThrowIfNull(interaction);

        return interaction.Data.Current.Value?.ToString() ?? string.Empty;
    }

    private static string Truncate(string value)
    {
        return value.Length <= MaxTextLength ? value : value[..MaxTextLength];
    }

    private static int? GetMatchScore(string value, string normalizedInput)
    {
        if (normalizedInput.Length == 0)
        {
            return 0;
        }

        var normalizedValue = NormalizeForSearch(value);
        if (normalizedValue.Equals(normalizedInput, StringComparison.Ordinal))
        {
            return 0;
        }

        if (normalizedValue.StartsWith(normalizedInput, StringComparison.Ordinal))
        {
            return 10;
        }

        var tokens = normalizedValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Any(token => token.StartsWith(normalizedInput, StringComparison.Ordinal)))
        {
            return 20;
        }

        if (normalizedValue.Contains(normalizedInput, StringComparison.Ordinal))
        {
            return 30;
        }

        if (normalizedInput.Length < 3)
        {
            return null;
        }

        var fuzzyDistance = GetBestFuzzyDistance(normalizedValue, tokens, normalizedInput);
        return fuzzyDistance <= 2
            ? 40 + fuzzyDistance
            : null;
    }

    private static int GetBestFuzzyDistance(string normalizedValue, IReadOnlyCollection<string> tokens, string normalizedInput)
    {
        var bestDistance = GetBoundedLevenshteinDistance(normalizedValue, normalizedInput, 2);
        foreach (var token in tokens)
        {
            bestDistance = Math.Min(bestDistance, GetBoundedLevenshteinDistance(token, normalizedInput, 2));
        }

        return bestDistance;
    }

    private static int GetBoundedLevenshteinDistance(string left, string right, int maxDistance)
    {
        if (Math.Abs(left.Length - right.Length) > maxDistance)
        {
            return maxDistance + 1;
        }

        var previousRow = Enumerable.Range(0, right.Length + 1).ToArray();
        var currentRow = new int[right.Length + 1];

        for (var leftIndex = 1; leftIndex <= left.Length; leftIndex++)
        {
            currentRow[0] = leftIndex;
            var rowMinimum = currentRow[0];

            for (var rightIndex = 1; rightIndex <= right.Length; rightIndex++)
            {
                var substitutionCost = left[leftIndex - 1] == right[rightIndex - 1] ? 0 : 1;
                currentRow[rightIndex] = Math.Min(
                    Math.Min(currentRow[rightIndex - 1] + 1, previousRow[rightIndex] + 1),
                    previousRow[rightIndex - 1] + substitutionCost);
                rowMinimum = Math.Min(rowMinimum, currentRow[rightIndex]);
            }

            if (rowMinimum > maxDistance)
            {
                return maxDistance + 1;
            }

            (previousRow, currentRow) = (currentRow, previousRow);
        }

        return previousRow[right.Length];
    }

    private static string NormalizeForSearch(string value)
    {
        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(normalized.Length);

        foreach (var character in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsWhiteSpace(character) || character == '-' || character == '_' ? ' ' : char.ToLowerInvariant(character));
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private sealed class ScoredAutocompleteValue
    {
        public ScoredAutocompleteValue(string value, int? matchScore)
        {
            this.Value = value;
            this.MatchScore = matchScore;
        }

        public string Value { get; }

        public int? MatchScore { get; }
    }
}
