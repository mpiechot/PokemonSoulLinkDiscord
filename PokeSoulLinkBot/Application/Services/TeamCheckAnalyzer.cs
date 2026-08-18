using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Services;

public sealed class TeamCheckAnalyzer
{
    public TeamCheckAnalysis Analyze(SoulLinkRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        var currentGroups = run.ActiveLinks
            .Where(group => group is not null)
            .Cast<LinkGroup>()
            .Where(group => group.IsAlive)
            .ToList();
        var candidateGroups = run.LinkGroups.Where(group => group.IsAlive).ToList();
        var optimalGroups = SelectOptimalGroups(run.Players, candidateGroups);

        return new TeamCheckAnalysis
        {
            CurrentCoverage = CreateCoverage(run.Players, currentGroups),
            OptimalCoverage = CreateCoverage(run.Players, optimalGroups),
            OptimalLinkGroups = optimalGroups,
        };
    }

    private static IReadOnlyList<LinkGroup> SelectOptimalGroups(
        IReadOnlyList<RunPlayer> players,
        IReadOnlyList<LinkGroup> candidates)
    {
        var selected = new List<LinkGroup>();
        var remaining = candidates.ToList();
        var coveredTypes = players.ToDictionary(
            player => player.UserId,
            _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase));

        while (selected.Count < 6 && remaining.Count > 0)
        {
            var best = remaining
                .Select(group => new
                {
                    Group = group,
                    Score = CalculateScore(group, players, coveredTypes),
                })
                .OrderByDescending(candidate => candidate.Score.NewTypes)
                .ThenByDescending(candidate => candidate.Score.MinimumCoverage)
                .ThenByDescending(candidate => candidate.Score.TotalCoverage)
                .ThenBy(candidate => candidate.Group.Route, StringComparer.OrdinalIgnoreCase)
                .First();

            selected.Add(best.Group);
            remaining.Remove(best.Group);
            AddTypes(best.Group, coveredTypes);
        }

        return selected;
    }

    private static (int NewTypes, int MinimumCoverage, int TotalCoverage) CalculateScore(
        LinkGroup group,
        IReadOnlyList<RunPlayer> players,
        IReadOnlyDictionary<ulong, HashSet<string>> coveredTypes)
    {
        var newTypes = 0;
        var coverageByPlayer = new List<int>();
        foreach (var player in players)
        {
            var types = GetTypes(group, player.UserId);
            newTypes += types.Count(type => !coveredTypes[player.UserId].Contains(type));
            coverageByPlayer.Add(coveredTypes[player.UserId].Count + types.Count(type => !coveredTypes[player.UserId].Contains(type)));
        }

        return (newTypes, coverageByPlayer.Count == 0 ? 0 : coverageByPlayer.Min(), coverageByPlayer.Sum());
    }

    private static void AddTypes(LinkGroup group, IDictionary<ulong, HashSet<string>> coveredTypes)
    {
        foreach (var entry in group.Entries.Where(entry => entry.IsAlive))
        {
            foreach (var type in entry.Types.Where(type => !string.IsNullOrWhiteSpace(type)))
            {
                coveredTypes[entry.PlayerUserId].Add(type.Trim());
            }
        }
    }

    private static IReadOnlyList<TeamTypeCoverage> CreateCoverage(
        IReadOnlyList<RunPlayer> players,
        IReadOnlyList<LinkGroup> groups)
    {
        return players.Select(player => new TeamTypeCoverage
        {
            PlayerId = player.UserId,
            PlayerName = player.UserName,
            Types = groups
                .SelectMany(group => GetTypes(group, player.UserId))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(type => type, StringComparer.OrdinalIgnoreCase)
                .ToList(),
        }).ToList();
    }

    private static IReadOnlyList<string> GetTypes(LinkGroup group, ulong playerId)
    {
        return group.Entries
            .Where(entry => entry.IsAlive && entry.PlayerUserId == playerId)
            .SelectMany(entry => entry.Types)
            .Where(type => !string.IsNullOrWhiteSpace(type))
            .Select(type => type.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
