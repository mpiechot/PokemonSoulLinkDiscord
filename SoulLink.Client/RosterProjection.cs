using SoulLink;

namespace SoulLink.Client;

public enum RosterView { Team, Box, Dead, Shiny, Trade }

public sealed record RosterColumn(Guid Id, string Name);
public sealed record RosterRow(bool Linked, DashboardPokemon?[] Slots, string? LinkName = null, bool Failed = false, bool[]? MissingMembers = null, Guid LinkId = default);
public sealed record RosterGridData(RosterColumn[] Columns, RosterRow[] Rows, bool ShowRouteColumn = false)
{
    public static RosterGridData From(ClientView view, RosterView filter)
    {
        var players = view.Run?.Players ?? [];
        if (players.Length == 0)
            players = [new ClientPlayerView("Du", Guid.Empty, "", "")];
        else if (view.LocalPlayerId is Guid local)
            players = players.OrderByDescending(player => player.Id == local).ToArray();
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
                RosterView.Trade => m.Present && !m.Dead,
                _ => m.State.IsShiny,
            }).OrderBy(m => m.State.Box).ThenBy(m => m.State.Slot).ToDictionary(m => m.State.Identity);
        }).ToArray();

        bool showRoutes = filter is RosterView.Box or RosterView.Dead or RosterView.Trade;
        var rows = new List<RosterRow>();
        foreach (var link in (view.Run?.Links ?? []).OrderBy(link => link.AreaId == Encounter.StarterArea ? -1 : link.AreaId))
        {
            var slots = new DashboardPokemon?[columns.Length];
            var missing = Enumerable.Repeat(true, columns.Length).ToArray();
            foreach (var member in link.Members)
            {
                int index = Array.FindIndex(columns, c => member.PlayerId == Guid.Empty
                    ? c.Name == member.Player : c.Id == member.PlayerId);
                if (index < 0) continue;
                missing[index] = false;
                if (eligible[index].Remove(member.Pokemon.State.Identity, out var pokemon))
                    slots[index] = pokemon;
            }
            bool failed = link.State == "FAILED";
            if (slots.Any(m => m is not null) || (filter == RosterView.Dead && (failed || link.State == "DEAD")))
                rows.Add(new RosterRow(link.Members.Length > 1 && link.State != "PARTIAL" && !failed,
                    slots, link.Area, failed, missing, link.Id));
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
        return new RosterGridData(columns, rows.ToArray(), showRoutes);
    }
}
