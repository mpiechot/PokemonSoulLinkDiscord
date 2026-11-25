namespace SoulLink;

public static class StatusPresenter
{
    public static IEnumerable<string> Lines(AutomationRun run, Func<AutomationPlayer, string> connection)
    {
        yield return $"{run.Name} | {run.Phase} | {run.Id}";
        yield return $"Edition: {run.Edition} · Death rule: {run.DeathRule} · Game: read-only";
        foreach (var player in run.Players)
        {
            yield return $"{player.Name}: {connection(player)} — {player.Diagnostic}";
            foreach (var pokemon in player.Pokemon.Where(p => p.Present && p.State.Container == Placement.Party).OrderBy(p => p.State.Slot))
                yield return $"  {pokemon.State.Slot + 1}. {pokemon.State.DisplayName} [{pokemon.State.Nickname}] Lv {pokemon.State.Level} HP {pokemon.State.Hp}/{pokemon.State.MaxHp}{(pokemon.Dead ? " DEAD" : "")}";
            yield return $"  Box: {player.Pokemon.Count(p => p.Present && p.State.Container == Placement.Box)} Pokémon";
        }

        foreach (var encounter in run.Encounters)
        {
            var members = encounter.Members.Select(member =>
            {
                var player = run.Players.Single(p => p.Id == member.Key);
                var pokemon = player.Pokemon.Single(p => p.Id == member.Value);
                return (Player: player, Pokemon: pokemon);
            }).ToList();
            string state = LinkState(encounter, members.Select(m => m.Pokemon));
            yield return $"{encounter.Name}: {state} · {string.Join(" / ", members.Select(m => $"{m.Player.Name}: {m.Pokemon.State.DisplayName}"))}";
        }
    }

    public static string LinkState(Encounter encounter, IEnumerable<TrackedPokemon> members)
    {
        if (encounter.Failed) return "FAILED";
        if (encounter.Dead) return "DEAD";
        if (encounter.GroupId is null) return "PARTIAL";
        var team = members.ToArray();
        if (team.Length > 0 && team.All(p => p.Present && p.State.Container == Placement.Party)) return "ACTIVE";
        if (team.Length > 0 && team.All(p => p.Present && p.State.Container == Placement.Box)) return "BOX";
        return "TEAM CONFLICT";
    }

    public static IEnumerable<string> Pages(IEnumerable<string> lines, int limit = 1800)
    {
        var page = new System.Text.StringBuilder();
        foreach (string line in lines)
        {
            string safeLine = line.Length > limit ? line[..limit] : line;
            if (page.Length + safeLine.Length + 1 > limit)
            {
                yield return page.ToString();
                page.Clear();
            }
            page.AppendLine(safeLine);
        }
        if (page.Length > 0) yield return page.ToString();
    }
}
