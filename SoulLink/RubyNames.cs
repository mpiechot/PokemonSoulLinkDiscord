namespace SoulLink;

public static partial class RubyNames
{
    private static readonly string[] Places =
    [
        "Wurzelheim", "Rosaltstadt", "Faustauhaven", "Bad Lavastadt", "Laubwechselfeld",
        "Wiesenflur", "Floßbrunn", "Blütenburg City", "Graphitport City", "Malvenfroh City",
        "Metarost City", "Baumhausen City", "Seegrasulb City", "Moosbach City", "Xeneroville", "Prachtpolis City",
    ];

    private static readonly IReadOnlyDictionary<int, string> Landmarks = new Dictionary<int, string>
    {
        [55] = "Granithöhle",
        [56] = "Schlotberg",
        [57] = "Safari-Zone",
        [58] = "Duellturm",
        [59] = "Blütenburgwald",
        [60] = "Metaflurtunnel",
        [61] = "Schiffswrack",
        [62] = "Neu Malvenfroh",
        [63] = "Meteorfälle",
        [64] = "Meteorfälle",
        [65] = "Pyroberg",
        [66] = "Versteck",
        [67] = "Küstenhöhle",
        [68] = "Tiefseehöhle",
        [70] = "Siegesstraße",
        [71] = "Wundereiland",
        [72] = "Urzeithöhle",
        [73] = "Insel im Süden",
        [74] = "Feuriger Pfad",
        [75] = "Feuriger Pfad",
        [76] = "Steilpass",
        [77] = "Steilpass",
        [78] = "Siegelkammer",
        [79] = "Siegelkammer (unter Wasser)",
        [80] = "Sonnengrotte",
        [81] = "Inselhöhle",
        [82] = "Wüstenruine",
        [83] = "Grabmal",
        [84] = "Umzugswagen",
        [85] = "Himmelturm",
        [86] = "Geheimbasis",
        [87] = "Wechselnder Ort",
    };

    private static readonly Lazy<Dictionary<string, SpeciesName>> ByAlias = new(BuildAliases);

    private static Dictionary<string, SpeciesName> BuildAliases() => SpeciesByDex.Skip(1)
        .SelectMany(species => new[] { (Alias: CatchCheckRules.Normalize(species.German), species), (Alias: CatchCheckRules.Normalize(species.English), species) })
        .DistinctBy(pair => pair.Alias).ToDictionary(pair => pair.Alias, pair => pair.species);

    public static SpeciesName? Find(string query) => ByAlias.Value.GetValueOrDefault(CatchCheckRules.Normalize(query.Replace(".", "", StringComparison.Ordinal)));

    public static SpeciesName? ForSpecies(int id) =>
        RubyDashboardCatalog.Appearance(id) is { } form ? SpeciesByDex[form.NationalDex] : null;

    public static string Pokemon(int id) => ForSpecies(id)?.German ?? SpeciesCatalog.Name(id);

    public static string? Move(int? id) => id is > 0 and < 355 ? MoveById[id.Value] : null;

    public static string Area(int id) => id >= 0 && id < Places.Length ? Places[id]
        : id == 255 ? "Unbekannter Ort" : Landmarks.GetValueOrDefault(id, AreaCatalog.Name(id));

    public sealed record SpeciesName(int Family, string German, string English);
}

public static class CatchCheckRules
{
    public static string Normalize(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return string.Concat(name.Trim().ToLowerInvariant().Where(c => c is not ' ' and not '-' and not '_'));
    }

    public static CatchCheck Check(AutomationRun run, string name)
    {
        if (RubyNames.Find(name) is not { } wanted) return new CatchCheck(null, null, null, null);
        foreach (var player in run.Players)
        {
            foreach (var found in player.Pokemon.Where(p => !p.State.IsEgg))
            {
                if (RubyNames.ForSpecies(found.State.Species) is { } actual && actual.Family == wanted.Family)
                    return new CatchCheck(wanted.German, actual.German, player.Name, RubyNames.Area(found.State.MetArea));
            }
        }
        return new CatchCheck(wanted.German, null, null, null);
    }
}

public sealed record CatchCheck(string? Pokemon, string? PreviouslyCaught, string? Player, string? Area);
