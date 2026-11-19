namespace SoulLink;

public sealed record RawPokemon(string Hex, Placement Container, int Slot, int? Box = null);

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
    public int MapGroup { get; init; }
    public int MapNumber { get; init; }
    public int Area { get; init; } = 255;
    public bool InBattle { get; init; }
    public bool CompleteInventory { get; init; }
    public List<RawPokemon> Pokemon { get; init; } = [];

    public GameSnapshot Parse()
    {
        if (this.Version != Protocol.Version || this.Type != "snapshot" || !this.CompleteInventory || this.GameCode != "AXVD" || this.Pokemon is null || this.Pokemon.Count > 426)
            throw new InvalidDataException(this.Error ?? "Incomplete inventory, unsupported ROM or bridge protocol.");
        var pokemon = new List<PokemonState>();
        foreach (var raw in this.Pokemon)
        {
            if (raw is null || raw.Hex is null || !Enum.IsDefined(raw.Container)) throw new InvalidDataException("Invalid raw Pokémon record.");
            int expected = raw.Container == Placement.Party ? 200 : 160;
            if (raw.Hex.Length != expected) throw new InvalidDataException("Invalid raw Pokémon length.");
            var parsed = Gen3Parser.Parse(Convert.FromHexString(raw.Hex), raw.Container, raw.Slot, raw.Box);
            if (parsed is not null) pokemon.Add(parsed);
        }
        if (!pokemon.Any(p => p.Container == Placement.Party))
            throw new InvalidDataException("No valid party; wait for a loaded save or verify the memory profile.");
        return new GameSnapshot
        {
            GameCode = this.GameCode, LayoutId = this.LayoutId, Epoch = this.Epoch, Frame = this.Frame,
            PlaySeconds = this.PlaySeconds, MapGroup = this.MapGroup, MapNumber = this.MapNumber,
            Area = MapCatalog.Section(this.MapGroup, this.MapNumber), InBattle = this.InBattle, Pokemon = pokemon,
        };
    }
}
