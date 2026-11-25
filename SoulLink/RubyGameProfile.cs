using System.Text.RegularExpressions;

namespace SoulLink;

internal sealed partial class RubyGameProfile : GameProfile
{
    public const string Code = "AXVD";
    public const string EditionId = "ruby";
    internal const string ReferenceLayout = "ruby-axvd-reference-v2";

    public override string GameCode => Code;

    public override string Edition => EditionId;

    public override string RomExtension => ".gba";

    internal override bool MatchesEdition(string? edition) => edition is EditionId or "rubin" or Code;

    internal override bool MatchesHeader(ReadOnlySpan<byte> header) =>
        header.Length >= 0xBD && header[0xA0..0xB0].SequenceEqual("POKEMON RUBYAXVD"u8) && header[0xBC] <= 1;

    internal override bool IsStarter(GameSnapshot snapshot, PokemonState pokemon) =>
        snapshot.RubyStarterWindow && snapshot.Pokemon.Count == 1 &&
        pokemon is { Container: Placement.Party, Slot: 0, MetArea: 16, IsEgg: false };

    public override string AreaName(int area) => RubyNames.Area(area);

    public override bool SupportsLocalActions(string layoutId) => layoutId == ReferenceLayout;

    public override bool SupportsTrade => true;

    internal override PokemonState DecodeTrade(RawPokemon pokemon) => Gen3Parser.ParseTrade(pokemon);

    internal override PokemonRomDetails ReadPokemonDetails(Stream rom, int species) => RubyRomDetails.Read(rom, species);

    internal override GameSnapshot Decode(BridgeFrame frame)
    {
        if (frame.Pokemon is null || frame.Pokemon.Count > 426)
            throw new InvalidDataException(frame.Error ?? "Incomplete inventory, unsupported ROM or bridge protocol.");
        var pokemon = new List<PokemonState>();
        byte[]? types = frame.RubyTypesHex?.Length == 412 * 4 ? Convert.FromHexString(frame.RubyTypesHex) : null;
        foreach (var raw in frame.Pokemon)
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
                if (frame.InBattle && parsed.Container == Placement.Party && raw.LiveHp is >= 0 && raw.LiveHp <= parsed.MaxHp)
                    parsed = parsed with { Hp = raw.LiveHp };
                if (types is not null && types[parsed.Species * 2] is < 18 and not 9 && types[parsed.Species * 2 + 1] is < 18 and not 9)
                {
                    parsed = parsed with { RubyType1 = types[parsed.Species * 2], RubyType2 = types[parsed.Species * 2 + 1] };
                }
                pokemon.Add(parsed);
            }
        }
        if (!pokemon.Any(p => p.Container == Placement.Party) &&
            (frame.InBattle || frame.Position is not { Valid: true } || frame.Pokemon.Any(p => p.Container == Placement.Party)))
            throw new InvalidDataException("No valid party or live field position; wait for a loaded save or verify the memory profile.");
        return new GameSnapshot
        {
            GameCode = frame.GameCode,
            LayoutId = frame.LayoutId,
            Epoch = frame.Epoch,
            Frame = frame.Frame,
            PlaySeconds = frame.PlaySeconds,
            TrainerId = frame.TrainerId,
            Money = frame.Money is >= 0 and <= 999_999 ? frame.Money : null,
            MapGroup = frame.MapGroup,
            MapNumber = frame.MapNumber,
            Area = MapCatalog.Section(frame.MapGroup, frame.MapNumber),
            Position = frame.Position is { Valid: true } ? frame.Position : null,
            InBattle = frame.InBattle,
            Battle = ParseBattle(frame),
            Faints = frame.Faints is { Length: > 0 and <= 6 }
                ? frame.Faints.Where(f => f is not null && f.PartySlot is >= 0 and <= 5)
                    .Select(ParseTrace)
                    .DistinctBy(f => f.PartySlot).ToArray()
                : null,
            WildBattles = frame.WildBattles is { Length: > 0 and <= 16 } ? frame.WildBattles
                .Where(battle => battle is { MapGroup: >= 0 and <= 255, MapNumber: >= 0 and <= 255, Started: >= 0, Outcome: >= 1 and <= 10 } &&
                    battle.Started < battle.Ended && battle.Ended <= frame.Frame)
                .Select(battle => new WildBattleResult(MapCatalog.Section(battle.MapGroup, battle.MapNumber), battle.Started, battle.Ended, battle.Outcome,
                    battle.Species is int species && SpeciesCatalog.IsValid(species) ? species : null))
                .Where(battle => battle.Area != 255).ToArray() : null,
            BadgeMask = frame.BadgeMask,
            PokeNavFlags = frame.PokeNavFlagsHex is { Length: 26 } navFlags ? Convert.FromHexString(navFlags) : null,
            RubyStarterWindow = frame.RubyStarterWindow,
            GymLevels = ValidateLevels(frame.GymLevels),
            GymLineups = frame.GymLineups is { Length: 8 } ? frame.GymLineups.Select(ValidateLevels).ToArray() : null,
            Pokemon = pokemon,
        };
    }

    private static BattleTrace ParseTrace(RawBattleTrace trace)
    {
        int? opponent = trace.OpponentSpecies is int foe && SpeciesCatalog.IsValid(foe) ? foe : null;
        int? move = trace.Move is > 0 and < 355 ? trace.Move : null;
        return new BattleTrace(trace.PartySlot, opponent, move, DecodeTrainer(trace.TrainerHex),
            trace.FinishingBlow && opponent.HasValue && move.HasValue);
    }

    private static BattleStatus? ParseBattle(BridgeFrame frame)
    {
        if (!frame.InBattle || frame.Battle is not { Started: >= 0 } battle || battle.Started > frame.Frame) return null;
        string? trainer = DecodeTrainer(battle.TrainerHex);
        var status = new BattleStatus($"{frame.Epoch}:{battle.Started}", trainer, battle.Combatants, battle.CatchEncounter && trainer is null);
        return status.IsValid() ? status : null;
    }

    private static string? DecodeTrainer(string? encoded) =>
        encoded is { Length: 24 } hex && hex.All(Uri.IsHexDigit) ? Gen3Parser.DecodeName(Convert.FromHexString(hex)) : null;

    private static int[]? ValidateLevels(int[]? levels) =>
        levels is { Length: > 0 and <= 6 } && levels.All(level => level is >= 1 and <= 100) ? levels : null;

    internal override void ValidateObservation(GameSnapshot snapshot)
    {
        if (snapshot.Epoch.Length is < 1 or > 80 || snapshot.LayoutId.Length is < 1 or > 80 ||
            snapshot.Frame < 0 || snapshot.PlaySeconds < 0 || snapshot.Area is < 0 or > 255 ||
            snapshot.Money is < 0 or > 999_999 ||
            snapshot.MapGroup is < 0 or > 255 || snapshot.MapNumber is < 0 or > 255 || snapshot.Pokemon.Count > 426 ||
            snapshot.WildBattles is { Length: > 16 } || snapshot.WildBattles?.Any(battle =>
                battle is null || battle.Area is < 0 or >= 255 || battle.Started < 0 || battle.Ended <= battle.Started ||
                battle.Ended > snapshot.Frame || battle.Outcome is < 1 or > 10 ||
                battle.Species is int foe && !SpeciesCatalog.IsValid(foe)) == true ||
            snapshot.Battle is { } combat && (!snapshot.InBattle || !combat.IsValid()) ||
            snapshot.Position is { Valid: false } || snapshot.Faints is { Length: > 6 } ||
            snapshot.Faints?.Any(f => f is null || f.PartySlot is < 0 or > 5 || f.Move is < 1 or > 354 ||
                f.OpponentSpecies.HasValue && !SpeciesCatalog.IsValid(f.OpponentSpecies.Value) ||
                f.Trainer is { Length: > 12 } || f.Trainer?.Any(char.IsControl) == true) == true)
            throw new InvalidDataException("Invalid game snapshot.");
        if (snapshot.Pokemon.Select(p => p.Identity).Distinct().Count() != snapshot.Pokemon.Count ||
            snapshot.Pokemon.Where(p => p.Container == Placement.Party).Count() > 6 ||
            snapshot.Pokemon.Select(p => (p.Container, p.Box, p.Slot)).Distinct().Count() != snapshot.Pokemon.Count)
            throw new InvalidDataException("Duplicate Pokémon identities or invalid inventory slots.");
        foreach (var pokemon in snapshot.Pokemon)
        {
            if (!IdentityFormat().IsMatch(pokemon.Identity) || !SpeciesCatalog.IsValid(pokemon.Species) || pokemon.Nickname.Length > 20 ||
                !Enum.IsDefined(pokemon.Container) || pokemon.Nickname.Any(char.IsControl) ||
                pokemon.MetArea is < 0 or > 255 || pokemon.Experience > 2_000_000 ||
                (pokemon.Container == Placement.Party && (pokemon.Box is not null || pokemon.Slot is < 0 or > 5 || pokemon.Level is null or < 1 or > 100 || pokemon.Hp is null or < 0 || pokemon.MaxHp is null or < 1 || pokemon.Hp > pokemon.MaxHp)) ||
                (pokemon.Container == Placement.Box && (pokemon.Box is null or < 0 or > 13 || pokemon.Slot is < 0 or > 29)))
                throw new InvalidDataException("Invalid Pokémon state.");
        }
    }

    [GeneratedRegex("^[0-9A-F]{8}:[0-9A-F]{8}$")]
    private static partial Regex IdentityFormat();
}
