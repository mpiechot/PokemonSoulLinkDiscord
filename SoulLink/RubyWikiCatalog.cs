using System.Buffers.Binary;
using System.Globalization;

namespace SoulLink;

public sealed record RubyMachineMove(int Number, int Move, bool Hidden);

public sealed record RubyWikiPokemon(int Species, int NationalDex, string Type1, string Type2, PokemonRomDetails Details,
    IReadOnlyList<RubyMachineMove> Machines, IReadOnlyList<int> EggMoves)
{
    public string Name => RubyNames.Pokemon(this.Species);
    public string EnglishName => RubyNames.ForSpecies(this.Species)!.English;
}

public sealed record RubyWikiEvolution(int From, int To, string Requirement);

public sealed record RubyWikiMove(int Id, int TypeId, int Power, int Accuracy, int Pp, RubyMoveEffect Effect, int Chance, int Priority)
{
    public string Name => RubyNames.Move(this.Id)!;
    public string TypeName => this.VariableType ? "Variabel" : RubyWikiCatalog.TypeName(RubyWikiCatalog.TypeKey(this.TypeId));
    public string Category => this.Power == 0 ? "Status" : this.VariableType ? "Variabel" : this.TypeId < 9 ? "Physisch" : "Speziell";
    public string PowerText => this.Power switch
    {
        0 => "–",
        1 when this.Effect == RubyMoveEffect.DragonRage => "40 KP",
        1 when this.Effect == RubyMoveEffect.Sonicboom => "20 KP",
        1 when this.Effect == RubyMoveEffect.Ohko => "K. o.",
        1 => "Variabel",
        _ => this.Power.ToString(CultureInfo.InvariantCulture),
    };
    public string Description => RubyWikiMoveEffects.Describe(this);

    private bool VariableType => this.Effect is RubyMoveEffect.HiddenPower or RubyMoveEffect.WeatherBall || this.TypeId == 9;
}

/// <summary>Ruby reference data and the same tables as read from a supported local cartridge.</summary>
public sealed partial class RubyWikiCatalog
{
    private const int MoveTable = 0x2080FC;
    private const int MoveSize = 12;
    private const int MachineTable = 0x381CEC;
    private const int MachineCompatibility = 0x20A0C0;
    private const int EggMoveTable = 0x2161AC;
    private const int EggSpeciesMarker = 20_000;
    private const int TechnicalMachineCount = 50;
    private static readonly string[] TypeKeys =
    [
        "normal", "fighting", "flying", "poison", "ground", "rock", "bug", "ghost", "steel", "mystery",
        "fire", "water", "grass", "electric", "psychic", "ice", "dragon", "dark",
    ];
    private static readonly string[] GermanTypes =
    [
        "Normal", "Kampf", "Flug", "Gift", "Boden", "Gestein", "Käfer", "Geist", "Stahl", "???",
        "Feuer", "Wasser", "Pflanze", "Elektro", "Psycho", "Eis", "Drache", "Unlicht",
    ];
    private static readonly CompareInfo SearchComparer = CultureInfo.GetCultureInfo("de-DE").CompareInfo;
    private static readonly Lazy<RubyWikiCatalog> StandardCatalog = new(CreateStandard);
    private readonly Dictionary<int, RubyWikiPokemon> bySpecies;
    private readonly RubyWikiMove?[] moves;
    private readonly RubyWikiEvolution[] evolutions;

    private RubyWikiCatalog(RubyWikiPokemon[] entries, RubyWikiMove?[] moves)
    {
        this.Pokemon = Array.AsReadOnly(entries);
        this.bySpecies = entries.ToDictionary(entry => entry.Species);
        this.moves = moves;
        this.evolutions = entries.SelectMany(entry => entry.Details.Evolutions.Select(evolution =>
            new RubyWikiEvolution(entry.Species, evolution.Species, evolution.Requirement))).ToArray();
    }

    public static RubyWikiCatalog Standard => StandardCatalog.Value;
    public IReadOnlyList<RubyWikiPokemon> Pokemon { get; }

    public static RubyWikiCatalog ReadCartridge(Stream rom)
    {
        if (GameProfiles.Identify(rom) is not RubyGameProfile)
            throw new InvalidDataException("Für diese ROM sind keine Rubin-Tabellen hinterlegt.");

        var moves = ReadCartridgeMoves(rom);
        var machines = ReadCartridgeMachines(rom);
        var eggs = ReadCartridgeEggMoves(rom);
        var entries = new RubyWikiPokemon[ReferenceEntries.Length];
        for (int i = 0; i < entries.Length; i++)
        {
            var reference = ReferenceEntries[i];
            var details = RubyRomDetails.ReadTables(rom, reference.Species, out int type1, out int type2);
            entries[i] = reference with
            {
                Type1 = TypeKey(type1),
                Type2 = TypeKey(type2),
                Details = details,
                Machines = ReadMachineCompatibility(rom, reference.Species, machines),
                EggMoves = eggs.TryGetValue(reference.Species, out var eggMoves) ? eggMoves.ToArray() : [],
            };
        }

        return new RubyWikiCatalog(entries, moves);
    }

    public RubyWikiPokemon? Find(int species) => this.bySpecies.GetValueOrDefault(species);

    public RubyWikiMove? Move(int id) => id > 0 && id < this.moves.Length ? this.moves[id] : null;

    public IEnumerable<RubyWikiEvolution> Evolutions(int species) =>
        this.evolutions.Where(edge => edge.To == species)
            .Concat(this.evolutions.Where(edge => edge.From == species && edge.To != species));

    public IEnumerable<RubyWikiPokemon> Search(string query)
    {
        query = query.Trim();
        if (query.Length == 0) return this.Pokemon;
        string number = query.TrimStart('#');
        if (int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int dex))
            return this.Pokemon.Where(entry => entry.NationalDex == dex);

        if (!query.Any(char.IsLetterOrDigit)) return [];
        return this.Pokemon.Where(entry => Matches(entry, query));
    }

    public static string TypeName(string type)
    {
        int index = Array.IndexOf(TypeKeys, type);
        return index >= 0 ? GermanTypes[index] : type;
    }

    internal static string TypeKey(int id) => id >= 0 && id < TypeKeys.Length
        ? TypeKeys[id] : throw new InvalidDataException("Die ROM enthält einen unbekannten Pokémon-Typ.");

    private static RubyWikiCatalog CreateStandard() => new(ReferenceEntries, ReferenceMoves);

    private static bool Matches(RubyWikiPokemon entry, string query)
    {
        const CompareOptions options = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace | CompareOptions.IgnoreSymbols;
        return SearchComparer.IndexOf(entry.Name, query, options) >= 0 ||
            SearchComparer.IndexOf(entry.EnglishName, query, options) >= 0;
    }

    private static RubyWikiPokemon ReferencePokemon(int species, int dex, PokemonStats stats, RubyLevelMove[] moves,
        RubyEvolution[] evolutions, RubyMachineMove[] machines, int[] eggs)
    {
        var appearance = RubyDashboardCatalog.Appearance(species)!;
        return new RubyWikiPokemon(species, dex, appearance.Type1, appearance.Type2,
            new PokemonRomDetails(stats, moves, evolutions), machines, eggs);
    }

    private static RubyWikiMove?[] ReadCartridgeMoves(Stream rom)
    {
        var moves = new RubyWikiMove?[ReferenceMoves.Length];
        var table = new byte[moves.Length * MoveSize];
        RubyRomDetails.ReadAt(rom, MoveTable, table);
        for (int id = 1; id < moves.Length; id++)
        {
            var row = table.AsSpan(id * MoveSize, MoveSize);
            _ = TypeKey(row[2]);
            if (row[3] > 100 || row[4] is 0 or > 64 || row[5] > 100)
                throw new InvalidDataException("Die Attackenwerte der ROM haben ein unbekanntes Format.");
            moves[id] = new RubyWikiMove(id, row[2], row[1], row[3], row[4], (RubyMoveEffect)row[0], row[5], (sbyte)row[7]);
        }
        return moves;
    }

    private static RubyMachineMove[] ReadCartridgeMachines(Stream rom)
    {
        Span<byte> table = stackalloc byte[TechnicalMachineCount * 2];
        RubyRomDetails.ReadAt(rom, MachineTable, table);
        var moves = new RubyMachineMove[TechnicalMachineCount + HiddenMachines.Length];
        for (int i = 0; i < TechnicalMachineCount; i++)
        {
            int move = BinaryPrimitives.ReadUInt16LittleEndian(table[(i * 2)..]);
            if (RubyNames.Move(move) is null) throw new InvalidDataException("Die TM-Liste der ROM enthält eine unbekannte Attacke.");
            moves[i] = new RubyMachineMove(i + 1, move, false);
        }
        HiddenMachines.CopyTo(moves, TechnicalMachineCount);
        return moves;
    }

    private static RubyMachineMove[] ReadMachineCompatibility(Stream rom, int species, RubyMachineMove[] moves)
    {
        Span<byte> bytes = stackalloc byte[8];
        RubyRomDetails.ReadAt(rom, MachineCompatibility + bytes.Length * species, bytes);
        ulong flags = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        return moves.Where((_, index) => (flags & (1UL << index)) != 0).ToArray();
    }

    private static Dictionary<int, List<int>> ReadCartridgeEggMoves(Stream rom)
    {
        var entries = new Dictionary<int, List<int>>();
        List<int>? moves = null;
        Span<byte> word = stackalloc byte[2];
        for (int index = 0; index < 8_192; index++)
        {
            RubyRomDetails.ReadAt(rom, EggMoveTable + index * 2, word);
            int value = BinaryPrimitives.ReadUInt16LittleEndian(word);
            if (value == ushort.MaxValue) return entries;
            if (value > EggSpeciesMarker && SpeciesCatalog.IsValid(value - EggSpeciesMarker))
            {
                moves = [];
                if (entries.TryAdd(value - EggSpeciesMarker, moves)) continue;
            }
            else if (moves is not null && RubyNames.Move(value) is not null)
            {
                moves.Add(value);
                continue;
            }
            throw new InvalidDataException("Die Zuchtliste der ROM hat ein unbekanntes Format.");
        }
        throw new InvalidDataException("Die Zuchtliste der ROM hat keinen gültigen Abschluss.");
    }
}
