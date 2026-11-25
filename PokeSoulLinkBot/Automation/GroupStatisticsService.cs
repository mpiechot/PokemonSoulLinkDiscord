using System.Numerics;
using SoulLink;

namespace PokeSoulLinkBot.Automation;

internal sealed class GroupStatisticsService(AutomationStore store)
{
    public GroupStatistics ForPlayer(Registration registration) => Project(this.ReadForPlayer(registration, ReadStatistics));

    public GroupStatistics ForGroup(Guid group) => Project(store.Execute(tx => ReadStatistics(tx, RunGroup.ScopeFor(group))));

    public GroupTimeline TimelineForPlayer(Registration registration) => BuildTimeline(this.ReadForPlayer(registration, ReadTimeline));

    public GroupTimeline TimelineForGroup(Guid group) => BuildTimeline(store.Execute(tx => ReadTimeline(tx, RunGroup.ScopeFor(group))));

    private static GroupData ReadRuns(AutomationStore.StoreTransaction tx, string scope)
    {
        var group = RunGroup.IdForScope(scope);
        return new GroupData(
            group is Guid id ? tx.Group(id).Name : "Discord-Server",
            tx.Runs(scope),
            group is Guid archiveGroup ? tx.ArchivedDiscordRuns(archiveGroup) : Array.Empty<ArchivedDiscordRun>());
    }

    private static (GroupData Group, Dictionary<Guid, int> Catches) ReadStatistics(AutomationStore.StoreTransaction tx, string scope) =>
        (ReadRuns(tx, scope), tx.CatchCounts(scope));

    private static (GroupData Group, Dictionary<Guid, List<AutomationEvent>> Events) ReadTimeline(AutomationStore.StoreTransaction tx, string scope) =>
        (ReadRuns(tx, scope), tx.TimelineEvents(scope));

    private static GroupStatistics Project((GroupData Group, Dictionary<Guid, int> Catches) data)
    {
        var native = data.Group.Runs.Select(run => new RunStatistics(
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
        return new GroupStatistics(data.Group.Name, native.Concat(data.Group.Archive.Select(DiscordRunArchive.Statistics))
            .OrderByDescending(run => run.Date).ThenBy(run => run.Name, StringComparer.OrdinalIgnoreCase).ThenBy(run => run.Id).ToArray());
    }

    private static GroupTimeline BuildTimeline((GroupData Group, Dictionary<Guid, List<AutomationEvent>> Events) data)
    {
        var native = data.Group.Runs.Select(run =>
        {
            var history = data.Events.GetValueOrDefault(run.Id)?.Concat(run.History) ?? run.History;
            var events = new List<RunTimelineEvent> { new RunTimelineEvent(TimelineEventKind.Start, run.CreatedAt, "Run angelegt.") };
            foreach (var item in history.DistinctBy(item => item.Id).OrderBy(item => item.At))
            {
                if (RunTimelineEvent.Classify(item.Kind) is { } kind)
                {
                    events.Add(new RunTimelineEvent(kind, item.At, item.Message));
                }
            }

            if (run.Phase == RunPhase.Finished && !events.Any(item => item.Kind == TimelineEventKind.End))
            {
                string text = string.IsNullOrWhiteSpace(run.EndReason) ? "Run abgeschlossen." : $"Run abgeschlossen. {run.EndReason}";
                events.Add(new RunTimelineEvent(TimelineEventKind.End, null, text));
            }

            return new RunTimeline(run.Name, false, run.CreatedAt, events.ToArray());
        });
        return new GroupTimeline(data.Group.Name, native.Concat(data.Group.Archive.Select(DiscordRunArchive.Timeline))
            .OrderBy(run => run.Date).ThenBy(run => run.Name, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private T ReadForPlayer<T>(Registration registration, Func<AutomationStore.StoreTransaction, string, T> read) =>
        store.Execute(tx =>
        {
            var run = tx.Run(registration.RunId);
            if (!run.Players.Any(player => player.Id == registration.PlayerId && player.ClientId == registration.ClientId))
            {
                throw new UnauthorizedAccessException("Spielerregistrierung nicht gefunden.");
            }

            return read(tx, run.GuildId);
        });

    private sealed class GroupData(string name, List<AutomationRun> runs, ArchivedDiscordRun[] archive)
    {
        public string Name { get; } = name;

        public List<AutomationRun> Runs { get; } = runs;

        public ArchivedDiscordRun[] Archive { get; } = archive;
    }
}
