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
    private const int DiscordMessageMaxLength = 2000;

    /// <summary>
    /// Creates an embed for a newly started run.
    /// </summary>
    /// <param name="run">The started run.</param>
    /// <param name="thumbnailUrl">The attachment URL of the thumbnail shown in the embed.</param>
    /// <returns>The created embed.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="run"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="thumbnailUrl"/> is null, empty, or whitespace.
    /// </exception>
    public Embed CreateRunStartedEmbed(SoulLinkRun run, string thumbnailUrl)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailUrl);

        return new EmbedBuilder()
            .WithTitle("Run Started")
            .WithDescription($"Run **{run.Name}** has been started.")
            .AddField("Run", run.Name, true)
            .AddField("Edition", run.Game, true)
            .AddField("Players", string.Join(", ", run.Players.Select(player => player.UserName)))
            .AddField("Started At (UTC)", run.StartedAtUtc.ToString("yyyy-MM-dd HH:mm:ss"))
            .WithThumbnailUrl(thumbnailUrl)
            .Build();
    }

    /// <summary>
    /// Creates an embed for an ended run.
    /// </summary>
    /// <param name="run">The ended run.</param>
    /// <param name="thumbnailUrl">The attachment URL of the thumbnail shown in the embed.</param>
    /// <returns>The created embed.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="run"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="thumbnailUrl"/> is null, empty, or whitespace.
    /// </exception>
    public Embed CreateRunEndedEmbed(SoulLinkRun run, string thumbnailUrl)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailUrl);

        return new EmbedBuilder()
            .WithTitle("Run Ended")
            .WithDescription($"Run **{run.Name}** has ended.")
            .AddField("Run", run.Name, true)
            .AddField("Edition", run.Game, true)
            .AddField("Reason", run.EndReason ?? "No reason given.")
            .AddField(
                "Ended At (UTC)",
                run.EndedAtUtc?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Unknown")
            .WithThumbnailUrl(thumbnailUrl)
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
    /// <param name="thumbnailUrl">The attachment URL of the thumbnail shown in the embed.</param>
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
        string thumbnailUrl,
        PokemonInfo? pokemonInfo)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemon);
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailUrl);

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
            .AddField("Status", statusText, true)
            .WithThumbnailUrl(thumbnailUrl);

        if (pokemonInfo != null)
        {
            if (pokemonInfo.Types.Count > 0)
            {
                var typeText = string.Join(", ", pokemonInfo.Types.Select(PokemonTypeVisualizer.FormatType));
                builder.AddField("Type", typeText, true);
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates an embed for a catch eligibility check.
    /// </summary>
    /// <param name="result">The catch check result.</param>
    /// <returns>The created embed.</returns>
    public Embed CreateCatchCheckEmbed(CatchCheckResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(result.RequestedPokemonName);

        if (result.IsAllowed)
        {
            return new EmbedBuilder()
                .WithTitle("Fang-Check")
                .WithColor(Color.Green)
                .WithDescription($"✅ **{result.RequestedPokemonName}** darf gefangen werden.")
                .Build();
        }

        var match = result.Match
            ?? throw new InvalidOperationException("Blocked catch checks must include the matching catch.");

        return new EmbedBuilder()
            .WithTitle("Fang-Check")
            .WithColor(Color.Red)
            .WithDescription($"⛔ **{result.RequestedPokemonName}** ist gesperrt.")
            .AddField("Fund", $"{match.PokemonName} · {match.Route} · {match.PlayerName} · {FormatCatchCheckStatus(match.Status)}")
            .Build();
    }

    /// <summary>
    /// Creates an embed for a linked group death.
    /// </summary>
    /// <param name="linkGroup">The affected link group.</param>
    /// <param name="thumbnailUrl">The attachment URL of the thumbnail shown in the embed.</param>
    /// <returns>The created embed.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="linkGroup"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="thumbnailUrl"/> is null, empty, or whitespace.
    /// </exception>
    public Embed CreateDeathRegisteredEmbed(LinkGroup linkGroup, string thumbnailUrl)
    {
        ArgumentNullException.ThrowIfNull(linkGroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailUrl);

        var entries = string.Join(
            Environment.NewLine,
            linkGroup.Entries.Select(entry => $"{entry.PlayerName}: {entry.PokemonName}"));
        var deathReason = linkGroup.Entries
            .Select(entry => entry.DeathReason)
            .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason));
        var causedByPlayer = linkGroup.Entries
            .Select(entry => entry.DeathCausedByPlayerName)
            .FirstOrDefault(playerName => !string.IsNullOrWhiteSpace(playerName));

        var builder = new EmbedBuilder()
            .WithTitle("Death Registered")
            .WithColor(new Color(128, 0, 128))
            .WithDescription($"The linked group on **{linkGroup.Route}** has been marked as dead.")
            .AddField("Route", linkGroup.Route, true)
            .AddField("Status", "Dead", true)
            .AddField("Reason", deathReason ?? "No reason given.")
            .AddField("Affected Pokemon", entries)
            .WithThumbnailUrl(thumbnailUrl);

        if (!string.IsNullOrWhiteSpace(causedByPlayer))
        {
            builder.AddField("Player", causedByPlayer, true);
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates an embed for a route lost before any catch was registered.
    /// </summary>
    /// <param name="linkGroup">The affected link group.</param>
    /// <param name="thumbnailUrl">The attachment URL of the thumbnail shown in the embed.</param>
    /// <returns>The created embed.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="linkGroup"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="thumbnailUrl"/> is null, empty, or whitespace.
    /// </exception>
    public Embed CreateRouteLostEmbed(LinkGroup linkGroup, string thumbnailUrl)
    {
        ArgumentNullException.ThrowIfNull(linkGroup);
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailUrl);

        var builder = new EmbedBuilder()
            .WithTitle("Route Lost")
            .WithColor(new Color(128, 0, 128))
            .WithDescription($"Route **{linkGroup.Route}** has been marked as lost.")
            .AddField("Route", linkGroup.Route, true)
            .AddField("Status", "Lost", true)
            .AddField("Reason", linkGroup.LossReason ?? "First encounter was not caught.")
            .WithThumbnailUrl(thumbnailUrl);

        if (!string.IsNullOrWhiteSpace(linkGroup.FailedEncounterPlayerName))
        {
            builder.AddField("Player", linkGroup.FailedEncounterPlayerName, true);
        }

        return builder.Build();
    }

    /// <summary>
    /// Creates a compact summary embed for run table messages.
    /// </summary>
    /// <param name="title">The embed title.</param>
    /// <param name="run">The active run.</param>
    /// <param name="thumbnailUrl">The attachment URL of the thumbnail shown in the embed.</param>
    /// <returns>The created embed.</returns>
    public Embed CreateRunSummaryEmbed(string title, SoulLinkRun run, string thumbnailUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailUrl);

        return new EmbedBuilder()
            .WithTitle(title)
            .WithColor(Color.Blue)
            .AddField("Run", run.Name, true)
            .AddField("Edition", run.Game, true)
            .WithThumbnailUrl(thumbnailUrl)
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
        return JoinBlocks(this.CreateStatusBlocks(run));
    }

    /// <summary>
    /// Creates Discord-compatible status messages for the active run.
    /// </summary>
    /// <param name="run">The active run.</param>
    /// <returns>The paged status messages.</returns>
    public IReadOnlyList<string> CreateStatusMessages(SoulLinkRun run)
    {
        return CombineBlocks(this.CreateStatusBlocks(run), DiscordMessageMaxLength);
    }

    /// <summary>
    /// Creates the message for a newly selected active team.
    /// </summary>
    /// <param name="run">The active run.</param>
    /// <returns>The formatted team message.</returns>
    public string CreateUseMessage(SoulLinkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return JoinBlocks(this.CreateTeamBlocks("Active Team Updated", run));
    }

    /// <summary>
    /// Creates Discord-compatible messages for a newly selected active team.
    /// </summary>
    /// <param name="run">The active run.</param>
    /// <returns>The paged team messages.</returns>
    public IReadOnlyList<string> CreateUseMessages(SoulLinkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return CombineBlocks(this.CreateTeamBlocks("Active Team Updated", run), DiscordMessageMaxLength);
    }

    /// <summary>
    /// Creates the message for the active team.
    /// </summary>
    /// <param name="run">The active run.</param>
    /// <returns>The formatted team message.</returns>
    public string CreateTeamMessage(SoulLinkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return JoinBlocks(this.CreateTeamBlocks("Active Team", run));
    }

    /// <summary>
    /// Creates Discord-compatible messages for the active team.
    /// </summary>
    /// <param name="run">The active run.</param>
    /// <returns>The paged team messages.</returns>
    public IReadOnlyList<string> CreateTeamMessages(SoulLinkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return CombineBlocks(this.CreateTeamBlocks("Active Team", run), DiscordMessageMaxLength);
    }

    /// <summary>
    /// Creates an embed for arena information.
    /// </summary>
    /// <param name="edition">The edition name.</param>
    /// <param name="arenaNumber">The arena number.</param>
    /// <param name="leaderName">The arena leader name.</param>
    /// <param name="location">The arena location.</param>
    /// <param name="levels">The Pokémon levels in the arena.</param>
    /// <param name="thumbnailUrl">The attachment URL of the thumbnail shown in the embed.</param>
    /// <returns>The created embed.</returns>
    public Embed CreateArenaInfoEmbed(
        string edition,
        long arenaNumber,
        string leaderName,
        string location,
        IReadOnlyCollection<int> levels,
        string thumbnailUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(edition);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        ArgumentNullException.ThrowIfNull(levels);
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailUrl);

        return new EmbedBuilder()
            .WithTitle("Arena Information")
            .WithColor(Color.Blue)
            .AddField("Edition", edition, true)
            .AddField("Arena", arenaNumber, true)
            .AddField("Leader", leaderName, true)
            .AddField("Location", location, true)
            .AddField("Pokemon Levels", string.Join(", ", levels), true)
            .WithThumbnailUrl(thumbnailUrl)
            .Build();
    }

    /// <summary>
    /// Creates an embed for historical run statistics.
    /// </summary>
    /// <param name="runs">The stored runs.</param>
    /// <param name="thumbnailUrl">The attachment URL of the thumbnail shown in the embed.</param>
    /// <returns>The created embed.</returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="runs"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="thumbnailUrl"/> is null, empty, or whitespace.
    /// </exception>
    public Embed CreateStatsEmbed(IReadOnlyList<SoulLinkRun> runs, string thumbnailUrl)
    {
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentException.ThrowIfNullOrWhiteSpace(thumbnailUrl);

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
            .WithThumbnailUrl(thumbnailUrl)
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

    private static IReadOnlyList<string> CombineBlocks(IReadOnlyList<string> blocks, int maxLength)
    {
        var messages = new List<string>();
        var builder = new StringBuilder();

        foreach (var block in blocks)
        {
            var separatorLength = builder.Length == 0 ? 0 : Environment.NewLine.Length * 2;
            var projectedLength = builder.Length + separatorLength + block.Length;
            if (builder.Length > 0 && projectedLength > maxLength)
            {
                messages.Add(builder.ToString());
                builder.Clear();
            }

            if (builder.Length > 0)
            {
                builder.AppendLine();
                builder.AppendLine();
            }

            builder.Append(block);
        }

        if (builder.Length > 0)
        {
            messages.Add(builder.ToString());
        }

        return messages;
    }

    private static string JoinBlocks(IReadOnlyList<string> blocks)
    {
        return string.Join(
            Environment.NewLine + Environment.NewLine,
            blocks);
    }

    private static string FormatCatchCheckStatus(string status)
    {
        return status switch
        {
            "Dead" => "Tot",
            "Team" => "Team",
            "Box" => "Box",
            _ => status,
        };
    }

    private IReadOnlyList<string> CreateStatusBlocks(SoulLinkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var currentTeam = run.ActiveLinks.Where(group => group != null && group.IsAlive).ToList();
        var currentTeamIds = currentTeam.Select(group => group!.Id).ToHashSet();
        var box = run.LinkGroups.Where(group => group.IsAlive && !currentTeamIds.Contains(group.Id));
        var playerNames = run.Players.Select(player => player.UserName).ToList();
        var blocks = new List<string>();

        blocks.Add(this.CreateTableSection("Current Team", currentTeam, playerNames));
        blocks.Add(this.CreateTableSection("Box", box, playerNames));
        blocks.Add(this.CreateTableSection("Dead", run.LinkGroups.Where(group => !group.IsAlive), playerNames));

        return blocks;
    }

    private IReadOnlyList<string> CreateTeamBlocks(string title, SoulLinkRun run)
    {
        var blocks = new List<string>
        {
            this.CreateRunHeader(title, run),
        };

        blocks.Add(this.CreateTeamTableSection(run));
        return blocks;
    }

    private string CreateTeamTableSection(SoulLinkRun run)
    {
        var playerNames = run.Players.Select(player => player.UserName).ToList();

        return this.CreateTableSection("Team", run.ActiveLinks, playerNames);
    }

    private string CreateRunHeader(string title, SoulLinkRun run)
    {
        return
            $"**{title}**{Environment.NewLine}" +
            $"Run: **{run.Name}**{Environment.NewLine}" +
            $"Edition: **{run.Game}**";
    }

    private string CreateTableSection(
        string title,
        IEnumerable<LinkGroup?> linkedGroups,
        IReadOnlyList<string> playerNames)
    {
        var table = this.BuildStringTable(linkedGroups, playerNames);

        return $"**{title}**{Environment.NewLine}```{table}```";
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
            ? value + " "
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
