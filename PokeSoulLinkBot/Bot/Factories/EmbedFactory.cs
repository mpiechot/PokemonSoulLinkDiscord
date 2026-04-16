using Discord;
using PokeSoulLinkBot.Bot.Helpers;
using PokeSoulLinkBot.Core.Models;
using System.Text;

namespace PokeSoulLinkBot.Bot.Factories;

/// <summary>
/// Creates Discord embeds for Soul Link bot responses.
/// </summary>
public sealed class EmbedFactory
{
    /// <summary>
    /// Creates an embed for a newly started run.
    /// </summary>
    /// <param name="run">The started run.</param>
    /// <param name="imageUrl">The attachment URL of the image shown in the embed.</param>
    /// <returns>The created embed.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="run"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="imageUrl"/> is null, empty, or whitespace.
    /// </exception>
    public Embed CreateRunStartedEmbed(SoulLinkRun run, string imageUrl)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);

        return new EmbedBuilder()
            .WithTitle("Soul Link Run Started")
            .WithDescription($"Run **{run.Name}** for **{run.Game}** has been started.")
            .AddField("Players", string.Join(", ", run.Players.Select(player => player.UserName)))
            .AddField("Started At (UTC)", run.StartedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"))
            .WithImageUrl(imageUrl)
            .Build();
    }

    /// <summary>
    /// Creates an embed for an ended run.
    /// </summary>
    /// <param name="run">The ended run.</param>
    /// <param name="imageUrl">The attachment URL of the image shown in the embed.</param>
    /// <returns>The created embed.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="run"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="imageUrl"/> is null, empty, or whitespace.
    /// </exception>
    public Embed CreateRunEndedEmbed(SoulLinkRun run, string imageUrl)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);

        return new EmbedBuilder()
            .WithTitle("🚀 Soul Link Run Ended")
            .WithDescription($"Run **{run.Name}** has ended.")
            .AddField("Game", run.Game)
            .AddField("Reason", run.EndReason ?? "No reason given.")
            .AddField(
                "Ended At (UTC)",
                run.EndedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown")
            .WithImageUrl(imageUrl)
            .Build();
    }

    /// <summary>
    /// Creates an embed for a registered catch.
    /// </summary>
    /// <param name="route">The route or area.</param>
    /// <param name="playerName">The player name.</param>
    /// <param name="pokemon">The Pokémon name.</param>
    /// <param name="currentEntries">The current number of linked entries.</param>
    /// <param name="requiredEntries">The required number of linked entries.</param>
    /// <param name="imageUrl">The Pokémon image URL.</param>
    /// <returns>The created embed.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when one of the string parameters is null, empty, or whitespace.
    /// </exception>
    public Embed CreateCatchRegisteredEmbed(
    string route,
    string playerName,
    string pokemon,
    int currentEntries,
    int requiredEntries,
    PokemonInfo? pokemonInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemon);

        var statusText = currentEntries >= requiredEntries
            ? "Link group is complete."
            : $"Waiting for {requiredEntries - currentEntries} more catch(es).";

        var builder = new EmbedBuilder()
            .WithTitle("Catch Registered")
            .WithDescription($"**{playerName}** registered **{pokemon}** on **{route}**.")
            .AddField("Progress", $"{currentEntries}/{requiredEntries}", true)
            .AddField("Status", statusText, true);

        if (pokemonInfo != null)
        {
            if (!string.IsNullOrWhiteSpace(pokemonInfo.ImageUrl))
            {
                builder.WithThumbnailUrl(pokemonInfo.ImageUrl);
            }

            if (pokemonInfo.Types.Count > 0)
            {
                var typeText = string.Join(", ", pokemonInfo.Types.Select(PokemonTypeVisualizer.FormatType));
                builder.AddField("Type", typeText, true);
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates an embed for a linked group death.
    /// </summary>
    /// <param name="linkGroup">The affected link group.</param>
    /// <param name="imageUrl">The attachment URL of the image shown in the embed.</param>
    /// <returns>The created embed.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="linkGroup"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="imageUrl"/> is null, empty, or whitespace.
    /// </exception>
    public Embed CreateDeathRegisteredEmbed(LinkGroup linkGroup, string imageUrl)
    {
        ArgumentNullException.ThrowIfNull(linkGroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);

        var entries = string.Join(
            Environment.NewLine,
            linkGroup.Entries.Select(entry => $"{entry.PlayerName}: {entry.PokemonName}"));

        return new EmbedBuilder()
            .WithTitle("💀 Soul Link Death")
            .WithColor(new Color(128, 0, 128))
            .WithDescription($"The linked group on **{linkGroup.Route}** has been marked as dead.")
            .AddField("Affected Pokémon", entries)
            .WithImageUrl(imageUrl)
            .Build();
    }

    /// <summary>
    /// Creates the status message for the active run.
    /// </summary>
    /// <param name="run">The active run.</param>
    /// <returns>The formatted status message.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="run"/> is <see langword="null"/>.
    /// </exception>
    public string CreateStatusMessage(SoulLinkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var aliveTable = this.BuildStringTable(run.LinkGroups.Where(group => group.IsAlive), run.Players.Select(player => player.UserName).ToList());
        var deadTable = this.BuildStringTable(run.LinkGroups.Where(group => !group.IsAlive), run.Players.Select(player => player.UserName).ToList());

        return
            $"**Run Status: {run.Name} ({run.Game})**{Environment.NewLine}{Environment.NewLine}" +
            $"**💚 Alive**{Environment.NewLine}" +
            $"```{aliveTable}```{Environment.NewLine}{Environment.NewLine}" +
            $"**💀 Dead**{Environment.NewLine}" +
            $"```{deadTable}```";
    }

    public string CreateUseMessage(SoulLinkRun run)
    {
        var activeTeam = BuildStringTable(run.activeLinks, run.Players.Select(player => player.UserName).ToList());

        return
            $"**New Active Teams: {run.Name} ({run.Game})**{Environment.NewLine}{Environment.NewLine}" +
            $"**📜 Team**{Environment.NewLine}" +
            $"```{activeTeam}```{Environment.NewLine}{Environment.NewLine}";
    }

    public string CreateTeamMessage(SoulLinkRun run)
    {
        var activeTeam = BuildStringTable(run.activeLinks, run.Players.Select(player => player.UserName).ToList());

        return
            $"**Active Teams: {run.Name} ({run.Game})**{Environment.NewLine}{Environment.NewLine}" +
            $"**📜 Team**{Environment.NewLine}" +
            $"```{activeTeam}```{Environment.NewLine}{Environment.NewLine}";
    }

    /// <summary>
    /// Creates an embed for historical run statistics.
    /// </summary>
    /// <param name="runs">The stored runs.</param>
    /// <param name="imageUrl">The attachment URL of the image shown in the embed.</param>
    /// <returns>The created embed.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="runs"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="imageUrl"/> is null, empty, or whitespace.
    /// </exception>
    public Embed CreateStatsEmbed(IReadOnlyList<SoulLinkRun> runs, string imageUrl)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);

        var completedRuns = runs.Count(run => run.EndedAtUtc is not null);
        var totalDeaths = runs
            .SelectMany(run => run.LinkGroups)
            .SelectMany(group => group.Entries)
            .Count(entry => !entry.IsAlive);

        var topDeaths = string.Join(
            ", ",
            runs.SelectMany(run => run.LinkGroups)
                .SelectMany(group => group.Entries)
                .Where(entry => !entry.IsAlive)
                .GroupBy(entry => entry.PlayerName)
                .OrderByDescending(group => group.Count())
                .Take(3)
                .Select(group => $"{group.Key}: {group.Count()}"));

        if (string.IsNullOrWhiteSpace(topDeaths))
        {
            topDeaths = "None";
        }

        return new EmbedBuilder()
            .WithTitle("📊 Run Statistics")
            .WithColor(Color.Blue)
            .AddField("Stored Runs", runs.Count)
            .AddField("Completed Runs", completedRuns)
            .AddField("Total Recorded Deaths", totalDeaths)
            .AddField("Top Deaths by Player", topDeaths)
            .WithImageUrl(imageUrl)
            .Build();
    }

    /// <summary>
    /// Creates an error embed.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <returns>The created embed.</returns>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="message"/> is null, empty, or whitespace.
    /// </exception>
    public Embed CreateErrorEmbed(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new EmbedBuilder()
            .WithTitle("Error")
            .WithColor(Color.Red)
            .WithDescription(message)
            .Build();
    }

    private string BuildStringTable(IEnumerable<LinkGroup> linkedGroups, IReadOnlyList<string> playerNames)
    {
        const int routeWidth = 14;
        const int playerColumnWidth = 14;

        var header = $"{PadRight("Route", routeWidth)}" +
                     string.Concat(playerNames.Select(player => PadRight(player, playerColumnWidth)));

        var separator = new string('-', routeWidth + (playerNames.Count * playerColumnWidth));

        var lines = new List<string>
        {
            header,
            separator,
        };

        foreach (var group in linkedGroups.Where(group => group != null).OrderBy(group => group.Route))
        {
            var row = PadRight(group.Route, routeWidth);

            foreach (var player in playerNames)
            {
                var entry = group.Entries.FirstOrDefault(linkedPokemon =>
                    linkedPokemon.PlayerName == player);

                var value = entry?.PokemonName ?? "-";
                row += PadRight(value, playerColumnWidth);
            }

            lines.Add(row);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string PadRight(string value, int totalWidth)
    {
        return value.Length >= totalWidth
            ? value[..(totalWidth - 1)] + " "
            : value.PadRight(totalWidth);
    }
}