namespace SoulLink;

public sealed record RawPokemon(string Hex, Placement Container, int Slot, int? Box = null, int? LiveHp = null);
public sealed record RawBattleTrace(int PartySlot, int? OpponentSpecies, int? Move, string? TrainerHex, bool FinishingBlow = false);
public sealed record RawWildBattle(int MapGroup, int MapNumber, long Started, long Ended, int Outcome);

public sealed record BridgeFrame
{
    public int Version { get; init; } = Protocol.Version;
    public string Type { get; init; } = "snapshot";
    public string? Error { get; init; }
    public string GameCode { get; init; } = "";
    public string LayoutId { get; init; } = "";
    public string Epoch { get; init; } = "";
    public long Frame { get; init; }
    public long PlaySeconds { get; init; }
    public uint? TrainerId { get; init; }
    public string? ItemSession { get; init; }
    public Guid? ItemRequest { get; init; }
    public string? ItemResult { get; init; }
    public int? ItemQuantity { get; init; }
    public int MapGroup { get; init; }
    public int MapNumber { get; init; }
    public int Area { get; init; } = 255;
    public bool InBattle { get; init; }
    public MapPosition? Position { get; init; }
    public RawBattleTrace[]? Faints { get; init; }
    public RawWildBattle[]? WildBattles { get; init; }
    public byte? BadgeMask { get; init; }
    public bool RubyStarterWindow { get; init; }
    public int[]? GymLevels { get; init; }
    public int[]?[]? GymLineups { get; init; }
    public string? RubyTypesHex { get; init; }
    public bool CompleteInventory { get; init; }
    public List<RawPokemon> Pokemon { get; init; } = [];

    public GameSnapshot Parse()
    {
        if (this.Version != Protocol.Version || this.Type != "snapshot" || !this.CompleteInventory || this.GameCode != "AXVD" || this.Pokemon is null || this.Pokemon.Count > 426)
            throw new InvalidDataException(this.Error ?? "Incomplete inventory, unsupported ROM or bridge protocol.");
        var pokemon = new List<PokemonState>();
        byte[]? types = this.RubyTypesHex?.Length == 412 * 4 ? Convert.FromHexString(this.RubyTypesHex) : null;
        foreach (var raw in this.Pokemon)
        {
            if (raw is null || raw.Hex is null || !Enum.IsDefined(raw.Container)) throw new InvalidDataException("Invalid raw Pokémon record.");
            int expected = raw.Container == Placement.Party ? 200 : 160;
            if (raw.Hex.Length != expected) throw new InvalidDataException("Invalid raw Pokémon length.");
            PokemonState? parsed;
            try
            {
                parsed = Gen3Parser.Parse(Convert.FromHexString(raw.Hex), raw.Container, raw.Slot, raw.Box);
            }
            catch (Exception error) when (error is InvalidDataException or FormatException)
            {
                string location = raw.Container == Placement.Party ? $"Teamplatz {raw.Slot + 1}" : $"Box {raw.Box + 1}, Platz {raw.Slot + 1}";
                throw new InvalidDataException($"{location}: {error.Message}", error);
            }
            if (parsed is not null)
            {
                if (this.InBattle && parsed.Container == Placement.Party && raw.LiveHp is >= 0 && raw.LiveHp <= parsed.MaxHp)
                    parsed = parsed with { Hp = raw.LiveHp };
                if (types is not null && types[parsed.Species * 2] is < 18 and not 9 && types[parsed.Species * 2 + 1] is < 18 and not 9)
                {
                    parsed = parsed with { RubyType1 = types[parsed.Species * 2], RubyType2 = types[parsed.Species * 2 + 1] };
                }
                pokemon.Add(parsed);
            }
        }
        if (!pokemon.Any(p => p.Container == Placement.Party) &&
            (this.InBattle || this.Position is not { Valid: true } || this.Pokemon.Any(p => p.Container == Placement.Party)))
            throw new InvalidDataException("No valid party or live field position; wait for a loaded save or verify the memory profile.");
        return new GameSnapshot
        {
            GameCode = this.GameCode,
            LayoutId = this.LayoutId,
            Epoch = this.Epoch,
            Frame = this.Frame,
            PlaySeconds = this.PlaySeconds,
            TrainerId = this.TrainerId,
            MapGroup = this.MapGroup,
            MapNumber = this.MapNumber,
            Area = MapCatalog.Section(this.MapGroup, this.MapNumber),
            Position = this.Position is { Valid: true } ? this.Position : null,
            InBattle = this.InBattle,
            Faints = this.Faints is { Length: > 0 and <= 6 }
                ? this.Faints.Where(f => f is not null && f.PartySlot is >= 0 and <= 5)
                    .Select(ParseTrace)
                    .DistinctBy(f => f.PartySlot).ToArray()
                : null,
            WildBattles = this.WildBattles is { Length: > 0 and <= 16 } ? this.WildBattles
                .Where(battle => battle is { MapGroup: >= 0 and <= 255, MapNumber: >= 0 and <= 255, Started: >= 0, Outcome: >= 1 and <= 10 } &&
                    battle.Started < battle.Ended && battle.Ended <= this.Frame)
                .Select(battle => new WildBattleResult(MapCatalog.Section(battle.MapGroup, battle.MapNumber), battle.Started, battle.Ended, battle.Outcome))
                .Where(battle => battle.Area != 255).ToArray() : null,
            BadgeMask = this.BadgeMask,
            RubyStarterWindow = this.RubyStarterWindow,
            GymLevels = ValidateLevels(this.GymLevels),
            GymLineups = this.GymLineups is { Length: 8 } ? this.GymLineups.Select(ValidateLevels).ToArray() : null,
            Pokemon = pokemon,
        };
    }

    private static BattleTrace ParseTrace(RawBattleTrace trace)
    {
        int? opponent = trace.OpponentSpecies is int foe && SpeciesCatalog.IsValid(foe) ? foe : null;
        int? move = trace.Move is > 0 and < 355 ? trace.Move : null;
        string? trainer = trace.TrainerHex is { Length: 24 } hex && hex.All(Uri.IsHexDigit)
            ? Gen3Parser.DecodeName(Convert.FromHexString(hex)) : null;
        return new BattleTrace(trace.PartySlot, opponent, move, trainer,
            trace.FinishingBlow && opponent.HasValue && move.HasValue);
    }

    private static int[]? ValidateLevels(int[]? levels) =>
        levels is { Length: > 0 and <= 6 } && levels.All(level => level is >= 1 and <= 100) ? levels : null;
}
