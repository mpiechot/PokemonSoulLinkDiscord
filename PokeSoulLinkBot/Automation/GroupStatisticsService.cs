using System.Numerics;
using SoulLink;

namespace PokeSoulLinkBot.Automation;

internal sealed class GroupStatisticsService(AutomationStore store)
{
    public GroupStatistics ForPlayer(Registration registration)
    {
        var data = store.Execute(tx =>
        {
            var run = tx.Run(registration.RunId);
            if (!run.Players.Any(player => player.Id == registration.PlayerId && player.ClientId == registration.ClientId))
            {
                throw new UnauthorizedAccessException("Spielerregistrierung nicht gefunden.");
            }

            return ReadScope(tx, run.GuildId);
        });
        return Project(data);
    }

    public GroupStatistics ForGroup(Guid group) => Project(store.Execute(tx => ReadScope(tx, RunGroup.ScopeFor(group))));

    private static StatisticsData ReadScope(AutomationStore.StoreTransaction tx, string scope)
    {
        var group = RunGroup.IdForScope(scope);
        return new StatisticsData(
            group is Guid id ? tx.Group(id).Name : "Discord-Server",
            tx.Runs(scope),
            tx.CatchCounts(scope),
            group is Guid archiveGroup ? tx.ArchivedDiscordRuns(archiveGroup) : Array.Empty<ArchivedDiscordRun>());
    }

    private static GroupStatistics Project(StatisticsData data)
    {
        var native = data.Runs.Select(run => new RunStatistics(
            run.Id,
            run.Name,
            false,
            run.Phase,
            run.CreatedAt,
            run.History.LastOrDefault(item => item.Kind == "run-ended")?.At,
            run.EndReason,
            data.Catches.GetValueOrDefault(run.Id),
            run.Players.Sum(player => player.Pokemon.Count(pokemon => pokemon.Dead && !pokemon.State.IsEgg)),
            BitOperations.PopCount(run.ArenaProgress)));
        return new GroupStatistics(data.Name, native.Concat(data.Archive.Select(DiscordRunArchive.Statistics))
            .OrderByDescending(run => run.Date).ThenBy(run => run.Name, StringComparer.OrdinalIgnoreCase).ThenBy(run => run.Id).ToArray());
    }

    private sealed class StatisticsData(string name, List<AutomationRun> runs, Dictionary<Guid, int> catches, ArchivedDiscordRun[] archive)
    {
        public string Name { get; } = name;

        public List<AutomationRun> Runs { get; } = runs;

        public Dictionary<Guid, int> Catches { get; } = catches;

        public ArchivedDiscordRun[] Archive { get; } = archive;
    }
}
