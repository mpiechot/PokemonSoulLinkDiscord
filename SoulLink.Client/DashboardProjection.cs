using SoulLink;

namespace SoulLink.Client;

internal static class DashboardProjection
{
    public static ClientRunView FromRun(AutomationRun run)
    {
        var playerById = run.Players.ToDictionary(p => p.Id);
        var players = run.Players.Select(p => new ClientPlayerView(p.Name, p.Id, p.Diagnostic, p.Connection,
            p.Marker, p.DisplayArea ?? p.LastSnapshot?.Area, p.Pokemon.Select(ProjectPokemon).ToArray(),
            p.DisplayPosition ?? p.LastSnapshot?.Position, p.ArenaProgress,
            p.Pokemon.Select(m => m.PendingDeath).OfType<PendingDeath>().ToArray())).ToArray();
        var links = run.Encounters.Select(encounter =>
        {
            var members = encounter.Members.Select(pair =>
            {
                var player = playerById[pair.Key];
                return (player.Name, Pokemon: player.Pokemon.Single(p => p.Id == pair.Value));
            }).ToArray();
            return new DashboardLink(RubyNames.Area(encounter.Area), StatusPresenter.LinkState(encounter, members.Select(m => m.Pokemon)),
                members.Select(m => new DashboardLinkMember(m.Name, ProjectPokemon(m.Pokemon))).ToArray());
        }).ToArray();
        return new ClientRunView(run.Name, run.Phase, run.Id, players, StatusPresenter.Lines(run, p => p.Connection).ToArray(),
            run.History.TakeLast(24).ToArray(), links);
    }

    private static DashboardPokemon ProjectPokemon(TrackedPokemon pokemon) => new(pokemon.State, pokemon.Dead, pokemon.Present);
}
