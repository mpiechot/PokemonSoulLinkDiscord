using System.Text;
using Discord;
using PokeSoulLinkBot.Bot.Helpers;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Bot.Factories;

/// <summary>
/// Creates Discord embeds for Soul Link bot responses.
/// </summary>
public sealed class EmbedFactory
{
    private const int DiscordFieldValueMaxLength = 1024;

    private const int CodeBlockOverheadLength = 6;

    private const string TruncatedTableSuffix = "...";

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
            .WithTitle("Run Started")
            .WithDescription($"Run **{run.Name}** has been started.")
            .AddField("Run", run.Name, true)
            .AddField("Edition", run.Game, true)
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
            .WithTitle("Run Ended")
            .WithDescription($"Run **{run.Name}** has ended.")
            .AddField("Run", run.Name, true)
            .AddField("Edition", run.Game, true)
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
    /// <param name="pokemonInfo">The Pokémon metadata used for visual details.</param>
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
            .AddField("Route", route, true)
            .AddField("Player", playerName, true)
            .AddField("Pokemon", pokemon, true)
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
            .WithTitle("Death Registered")
            .WithColor(new Color(128, 0, 128))
            .WithDescription($"The linked group on **{linkGroup.Route}** has been marked as dead.")
            .AddField("Route", linkGroup.Route, true)
            .AddField("Status", "Dead", true)
            .AddField("Affected Pokemon", entries)
            .WithImageUrl(imageUrl)
            .Build();
    }

    /// <summary>
    /// Creates an embed for a route lost before any catch was registered.
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
    public Embed CreateRouteLostEmbed(LinkGroup linkGroup, string imageUrl)
    {
        ArgumentNullException.ThrowIfNull(linkGroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);

        var builder = new EmbedBuilder()
            .WithTitle("Route Lost")
            .WithColor(new Color(128, 0, 128))
            .WithDescription($"Route **{linkGroup.Route}** has been marked as lost.")
            .AddField("Route", linkGroup.Route, true)
            .AddField("Status", "Lost", true)
            .AddField("Reason", linkGroup.LossReason ?? "First encounter was not caught.")
            .WithImageUrl(imageUrl);

        if (!string.IsNullOrWhiteSpace(linkGroup.FailedEncounterPlayerName))
        {
            builder.AddField("Player", linkGroup.FailedEncounterPlayerName, true);
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates the status message for the active run.
    /// </summary>
    /// <param name="run">The active run.</param>
    /// <returns>The formatted status message.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="run"/> is <see langword="null"/>.
    /// </exception>
    public Embed CreateStatusEmbed(SoulLinkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var currentTeam = run.ActiveLinks.Where(group => group != null && group.IsAlive).ToList();
        var currentTeamIds = currentTeam.Select(group => group!.Id).ToHashSet();
        var box = run.LinkGroups.Where(group => group.IsAlive && !currentTeamIds.Contains(group.Id));
        var playerNames = run.Players.Select(player => player.UserName).ToList();
        var builder = this.CreateRunOutputBuilder("Run Status", run)
            .WithColor(Color.Blue);

        this.AddTableField(builder, "Current Team", currentTeam, playerNames);
        this.AddTableField(builder, "Box", box, playerNames);
        this.AddTableField(builder, "Dead", run.LinkGroups.Where(group => !group.IsAlive), playerNames);

        return builder.Build();
    }

    /// <summary>
    /// Creates the status message for the active run.
    /// </summary>
    /// <param name="run">The active run.</param>
    /// <returns>The formatted status message.</returns>
    public string CreateStatusMessage(SoulLinkRun run)
    {
        return this.CreateRunOverviewMessage(this.CreateStatusEmbed(run));
    }

    /// <summary>
    /// Creates an embed for a newly selected active team.
    /// </summary>
    /// <param name="run">The active run.</param>
    /// <returns>The created embed.</returns>
    public Embed CreateUseEmbed(SoulLinkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return this.CreateTeamEmbed("Active Team Updated", run);
    }

    /// <summary>
    /// Creates the message for a newly selected active team.
    /// </summary>
    /// <param name="run">The active run.</param>
    /// <returns>The formatted team message.</returns>
    public string CreateUseMessage(SoulLinkRun run)
    {
        return this.CreateRunOverviewMessage(this.CreateUseEmbed(run));
    }

    /// <summary>
    /// Creates an embed for the active team.
    /// </summary>
    /// <param name="run">The active run.</param>
    /// <returns>The created embed.</returns>
    public Embed CreateTeamEmbed(SoulLinkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return this.CreateTeamEmbed("Active Team", run);
    }

    /// <summary>
    /// Creates the message for the active team.
    /// </summary>
    /// <param name="run">The active run.</param>
    /// <returns>The formatted team message.</returns>
    public string CreateTeamMessage(SoulLinkRun run)
    {
        return this.CreateRunOverviewMessage(this.CreateTeamEmbed(run));
    }

    /// <summary>
    /// Creates an embed for arena information.
    /// </summary>
    /// <param name="edition">The edition name.</param>
    /// <param name="arenaNumber">The arena number.</param>
    /// <param name="leaderName">The arena leader name.</param>
    /// <param name="location">The arena location.</param>
    /// <param name="levels">The Pokémon levels in the arena.</param>
    /// <returns>The created embed.</returns>
    public Embed CreateArenaInfoEmbed(
        string edition,
        long arenaNumber,
        string leaderName,
        string location,
        IReadOnlyCollection<int> levels)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(edition);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(levels);

        return new EmbedBuilder()
            .WithTitle("Arena Information")
            .WithColor(Color.Blue)
            .AddField("Edition", edition, true)
            .AddField("Arena", arenaNumber, true)
            .AddField("Leader", leaderName, true)
            .AddField("Location", location, true)
            .AddField("Pokemon Levels", string.Join(", ", levels), true)
            .Build();
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
            .WithTitle("Run Statistics")
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

    private static string CreateCodeBlock(string value)
    {
        var maxContentLength = DiscordFieldValueMaxLength - CodeBlockOverheadLength;
        var table = TruncateTable(value, maxContentLength);
        return $"```{table}```";
    }

    private static string TruncateTable(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        var lines = value.Split(Environment.NewLine);
        var builder = new StringBuilder();

        foreach (var line in lines)
        {
            var separatorLength = builder.Length == 0 ? 0 : Environment.NewLine.Length;
            var projectedLength = builder.Length + separatorLength + line.Length + Environment.NewLine.Length + TruncatedTableSuffix.Length;
            if (projectedLength > maxLength)
            {
                break;
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
            }

            builder.Append(line);
        }

        if (builder.Length > 0)
        {
            builder.AppendLine();
        }

        builder.Append(TruncatedTableSuffix);
        return builder.ToString();
    }

    private Embed CreateTeamEmbed(string title, SoulLinkRun run)
    {
        var playerNames = run.Players.Select(player => player.UserName).ToList();
        var builder = this.CreateRunOutputBuilder(title, run)
            .WithColor(Color.Blue);

        this.AddTableField(builder, "Team", run.ActiveLinks, playerNames);

        return builder.Build();
    }

    private EmbedBuilder CreateRunOutputBuilder(string title, SoulLinkRun run)
    {
        return new EmbedBuilder()
            .WithTitle(title)
            .AddField("Run", run.Name, true)
            .AddField("Edition", run.Game, true);
    }

    private void AddTableField(
        EmbedBuilder builder,
        string title,
        IEnumerable<LinkGroup?> linkedGroups,
        IReadOnlyList<string> playerNames)
    {
        var table = this.BuildStringTable(linkedGroups, playerNames);
        builder.AddField(title, CreateCodeBlock(table));
    }

    private string CreateRunOverviewMessage(Embed embed)
    {
        var lines = new List<string>
        {
            $"**{embed.Title}**",
        };

        foreach (var field in embed.Fields)
        {
            lines.Add(string.Empty);
            lines.Add($"**{field.Name}**");
            lines.Add(field.Value);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string BuildStringTable(IEnumerable<LinkGroup?> linkedGroups, IReadOnlyList<string> playerNames)
    {
        const int routeWidth = 14;
        const int playerColumnWidth = 24;

        var header = $"{this.PadRight("Route", routeWidth)}" +
                     string.Concat(playerNames.Select(player => this.PadRight(player, playerColumnWidth)));

        var separator = new string('-', routeWidth + (playerNames.Count * playerColumnWidth));

        var lines = new List<string>
        {
            header,
            separator,
        };

        foreach (var group in linkedGroups.Where(group => group != null).OrderBy(group => group!.Route))
        {
            var row = this.PadRight(group!.Route, routeWidth);

            foreach (var player in playerNames)
            {
                var entry = group.Entries.FirstOrDefault(linkedPokemon =>
                    linkedPokemon.PlayerName == player);

                var value = entry is null ? "-" : this.FormatPokemonWithTypes(entry);
                row += this.PadRight(value, playerColumnWidth);
            }

            lines.Add(row);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private string PadRight(string value, int totalWidth)
    {
        return value.Length >= totalWidth
            ? value[.. (totalWidth - 1)] + " "
            : value.PadRight(totalWidth);
    }

    private string FormatPokemonWithTypes(LinkedPokemon pokemon)
    {
        if (pokemon.Types.Count == 0)
        {
            return pokemon.PokemonName;
        }

        var typeIcons = string.Join(string.Empty, pokemon.Types.Select(PokemonTypeVisualizer.FormatType));
        return $"{pokemon.PokemonName} ({typeIcons})";
    }
}
