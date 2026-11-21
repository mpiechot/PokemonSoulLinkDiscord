namespace SoulLink;

public sealed record EncounterCatchVerdict(int Species, bool Allowed, string? Reason);

/// <summary>Evaluates the current wild opponent against the shared run and the player's encounter slot.</summary>
public static class EncounterCatchChecks
{
    public static EncounterCatchVerdict[] ForPlayer(AutomationRun run, AutomationPlayer player)
    {
        if (run.Phase != RunPhase.Active || player.LastSnapshot is not
            { InBattle: true, Area: >= 0 and < 255, Battle: { CatchEncounter: true, Trainer: null } battle } snapshot)
            return [];

        int area = snapshot.Area;
        var encounter = run.Encounters.SingleOrDefault(link => link.Area == area);
        string? locationProblem = encounter switch
        {
            { Failed: true } or { Dead: true } => $"Fangort gesperrt: {RubyNames.Area(area)}",
            _ when encounter?.Members.ContainsKey(player.Id) == true => "Du hast hier schon gefangen.",
            _ when player.FirstWildAreas.Contains(area) => "Dein Erstencounter ist bereits vorbei.",
            _ => null,
        };

        return battle.Combatants.Where(mon => (mon.Position & 1) != 0 && mon.Hp > 0)
            .Select(mon => mon.Species).Distinct().Select(species =>
            {
                if (locationProblem is not null) return new EncounterCatchVerdict(species, false, locationProblem);
                string? reason = FamilyProblem(CatchCheckRules.Check(run, species, Guid.Empty));
                return new EncounterCatchVerdict(species, reason is null, reason);
            }).ToArray();
    }

    internal static string? FamilyProblem(CatchCheck family) => family.PreviouslyCaught is { } match
        ? $"Evolutionsfamilie schon gefangen: {match} ({family.Player})"
        : family.Pokemon is null ? "Pokémon konnte nicht geprüft werden." : null;
}
