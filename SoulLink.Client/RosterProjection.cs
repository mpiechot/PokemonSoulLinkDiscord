using SoulLink;

namespace SoulLink.Client;

public enum RosterView { Team, Box, Dead, Shiny }

public sealed record RosterColumn(Guid Id, string Name);
public sealed record RosterRow(bool Linked, DashboardPokemon?[] Slots);
public sealed record RosterGridData(RosterColumn[] Columns, RosterRow[] Rows)
{
    public static RosterGridData From(ClientView view, RosterView filter)
    {
        var players = view.Run?.Players ?? [];
        if (players.Length == 0)
            players = [new ClientPlayerView("Du", Guid.Empty, "", "")];
        var columns = players.Select(p => new RosterColumn(p.Id, p.Name)).ToArray();
        var eligible = players.Select(p =>
        {
            var pokemon = p.Pokemon ?? [];
            if ((p.Id == Guid.Empty || (view.IsLocalRun && p.Id == view.LocalPlayerId)) && view.Snapshot is { } snapshot)
            {
                var byIdentity = pokemon.ToDictionary(m => m.State.Identity);
                var current = snapshot.Pokemon.Select(m => new DashboardPokemon(m, byIdentity.GetValueOrDefault(m.Identity)?.Dead == true, true));
                pokemon = current.Concat(pokemon.Where(m => m.Dead && !snapshot.Pokemon.Any(s => s.Identity == m.State.Identity))).ToArray();
            }
            return pokemon.Where(m => filter switch
            {
                RosterView.Team => m.Present && !m.Dead && m.State.Container == Placement.Party,
                RosterView.Box => m.Present && !m.Dead && m.State.Container == Placement.Box,
                RosterView.Dead => m.Dead,
                _ => m.State.IsShiny,
            }).OrderBy(m => m.State.Box).ThenBy(m => m.State.Slot).ToDictionary(m => m.State.Identity);
        }).ToArray();

        var rows = new List<RosterRow>();
        foreach (var link in view.Run?.Links ?? [])
        {
            var slots = new DashboardPokemon?[columns.Length];
            foreach (var member in link.Members)
            {
                int index = Array.FindIndex(columns, c => member.PlayerId == Guid.Empty
                    ? c.Name == member.Player : c.Id == member.PlayerId);
                if (index >= 0 && eligible[index].Remove(member.Pokemon.State.Identity, out var pokemon))
                    slots[index] = pokemon;
            }
            if (slots.Any(m => m is not null))
                rows.Add(new RosterRow(link.Members.Length > 1 && link.State != "PARTIAL", slots));
        }
        for (int i = 0; i < eligible.Length; i++)
        {
            foreach (var pokemon in eligible[i].Values)
            {
                var slots = new DashboardPokemon?[columns.Length];
                slots[i] = pokemon;
                rows.Add(new RosterRow(false, slots));
            }
        }
        return new RosterGridData(columns, rows.ToArray());
    }
}
