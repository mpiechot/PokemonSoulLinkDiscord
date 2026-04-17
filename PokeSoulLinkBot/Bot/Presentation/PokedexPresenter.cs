using Discord;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Bot.Presentation;

/// <summary>
/// Creates Discord output for Pokédex responses.
/// </summary>
public sealed class PokedexPresenter
{
    /// <summary>
    /// Creates the Pokédex embed.
    /// </summary>
    /// <param name="entry">The Pokédex entry.</param>
    /// <param name="requestedPokemonName">The requested Pokémon name.</param>
    /// <returns>The created embed.</returns>
    public Embed CreateEmbed(PokedexEntry entry, string requestedPokemonName)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestedPokemonName);

        var builder = new EmbedBuilder()
            .WithTitle($"Pokédex: {entry.PokemonName}")
            .WithDescription($"Angefragt: **{requestedPokemonName}**");

        if (!string.IsNullOrWhiteSpace(entry.ImageUrl))
        {
            builder.WithThumbnailUrl(entry.ImageUrl);
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates the Pokédex evolution table message.
    /// </summary>
    /// <param name="entry">The Pokédex entry.</param>
    /// <returns>The formatted table message.</returns>
    public string CreateTableMessage(PokedexEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        const int pokemonWidth = 16;
        const int requirementWidth = 28;
        const int typeWidth = 20;

        var lines = new List<string>
        {
            $"{Pad("Pokemon", pokemonWidth)}{Pad("Requirement", requirementWidth)}{Pad("Type(s)", typeWidth)}",
            new string('-', pokemonWidth + requirementWidth + typeWidth),
        };

        foreach (var row in entry.Rows)
        {
            var typeText = row.Types.Count == 0
                ? "-"
                : string.Join(", ", row.Types);

            lines.Add(
                $"{Pad(row.PokemonName, pokemonWidth)}" +
                $"{Pad(row.RequirementText, requirementWidth)}" +
                $"{Pad(typeText, typeWidth)}");
        }

        return $"```{string.Join(Environment.NewLine, lines)}```";
    }

    private static string Pad(string value, int width)
    {
        var normalizedValue = string.IsNullOrWhiteSpace(value) ? "-" : value;

        return normalizedValue.Length >= width
            ? normalizedValue[.. (width - 1)] + " "
            : normalizedValue.PadRight(width);
    }
}
