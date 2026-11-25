namespace SoulLink;

public sealed record RawPokemon(string Hex, Placement Container, int Slot, int? Box = null, int? LiveHp = null);
public sealed record RawBattleTrace(int PartySlot, int? OpponentSpecies, int? Move, string? TrainerHex, bool FinishingBlow = false);
public sealed record RawBattleStatus(long Started, string? TrainerHex, BattleCombatant[] Combatants, bool CatchEncounter = false);
public sealed record RawWildBattle(int MapGroup, int MapNumber, long Started, long Ended, int Outcome, int? Species = null);

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
    public int? Money { get; init; }
    public string? ItemSession { get; init; }
    public Guid? ItemRequest { get; init; }
    public string? ItemResult { get; init; }
    public int? ItemQuantity { get; init; }
    public RawPokemon? TradePokemon { get; init; }
    public OverworldPose? Overworld { get; init; }
    public int MapGroup { get; init; }
    public int MapNumber { get; init; }
    public int Area { get; init; } = 255;
    public bool InBattle { get; init; }
    public MapPosition? Position { get; init; }
    public RawBattleTrace[]? Faints { get; init; }
    public RawBattleStatus? Battle { get; init; }
    public RawWildBattle[]? WildBattles { get; init; }
    public byte? BadgeMask { get; init; }
    public string? PokeNavFlagsHex { get; init; }
    public bool RubyStarterWindow { get; init; }
    public int[]? GymLevels { get; init; }
    public int[]?[]? GymLineups { get; init; }
    public string? RubyTypesHex { get; init; }
    public bool CompleteInventory { get; init; }
    public List<RawPokemon> Pokemon { get; init; } = [];

    public GameSnapshot Parse()
    {
        if (this.Version != Protocol.Version || this.Type != "snapshot" || !this.CompleteInventory ||
            GameProfiles.ForGameCode(this.GameCode) is not { } profile)
            throw new InvalidDataException(this.Error ?? "Incomplete inventory, unsupported ROM or bridge protocol.");
        return profile.Decode(this);
    }
}
