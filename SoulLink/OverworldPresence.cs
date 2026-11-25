namespace SoulLink;

internal sealed class OverworldPresence(TimeProvider clock)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(1.2);
    private readonly Dictionary<Guid, Dictionary<Guid, Entry>> runs = new();
    private readonly object gate = new();

    public OverworldPlayer[] Exchange(Registration player, Guid session, OverworldPose? pose, Func<Guid, Guid, Guid, bool> isCurrent)
    {
        lock (this.gate)
        {
            if (!isCurrent(player.RunId, player.PlayerId, session)) throw new UnauthorizedAccessException("Overworld session expired.");
            if (pose is null)
            {
                this.Remove(player.RunId, player.PlayerId, session);
                return [];
            }

            long now = clock.GetTimestamp();
            if (!this.runs.TryGetValue(player.RunId, out var participants))
            {
                participants = new();
                this.runs.Add(player.RunId, participants);
            }

            participants[player.PlayerId] = new Entry(session, now, pose);
            var nearby = new List<OverworldPlayer>();
            foreach (var (playerId, other) in participants)
            {
                if (clock.GetElapsedTime(other.Received, now) > Lifetime || !isCurrent(player.RunId, playerId, other.Session))
                    participants.Remove(playerId);
                else if (playerId != player.PlayerId && other.Pose.MapGroup == pose.MapGroup && other.Pose.MapNumber == pose.MapNumber)
                    nearby.Add(new OverworldPlayer(playerId, other.Pose));
            }

            return nearby.ToArray();
        }
    }

    public void Remove(Guid runId, Guid playerId, Guid? session = null)
    {
        lock (this.gate)
        {
            if (this.runs.TryGetValue(runId, out var players) && players.TryGetValue(playerId, out var entry) &&
                (session is null || entry.Session == session))
            {
                players.Remove(playerId);
                if (players.Count == 0) this.runs.Remove(runId);
            }
        }
    }

    private sealed record Entry(Guid Session, long Received, OverworldPose Pose);
}
