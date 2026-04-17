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

        var input = userInput?.Trim() ?? string.Empty;
        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(value => input.Length == 0 || value.Contains(input, StringComparison.OrdinalIgnoreCase))
            .OrderBy(value => value)
            .Take(MaxResults)
            .Select(value => Truncate(value.Trim()))
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
}
