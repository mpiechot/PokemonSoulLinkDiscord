using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoulLink;

public static class Protocol
{
    public const int Version = 1;
    public const int MaxMessageBytes = 160_000;
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Json);

    public static T Deserialize<T>(string value) => JsonSerializer.Deserialize<T>(value, Json)
        ?? throw new InvalidDataException("Empty message.");
}

public enum RunPhase { Lobby, Active, Paused, Finished }
public enum DeathRule { Disabled, ZeroHpOutsideBattle, ConfirmFaint }
public enum Placement { Party, Box }
public enum PlayerMarker { Circle, Diamond, Star, Triangle, Square, Heart, Moon, Sun }

public sealed record MapPosition(int X, int Y, int Width, int Height, bool Outdoor)
{
    public bool Valid => Width is > 0 and <= 1024 && Height is > 0 and <= 1024 && X >= 0 && Y >= 0 && X < Width && Y < Height;
}

public sealed record BattleTrace(int PartySlot, int? OpponentSpecies, int? Move, string? Trainer, bool FinishingBlow = false, bool OpponentHidden = false);
public sealed record BattleCombatant(int Position, int Species, int Hp, int MaxHp, uint Personality)
{
    public bool IsValid() => this.Position is >= 0 and <= 3 && SpeciesCatalog.IsValid(this.Species) &&
        this.MaxHp is > 0 and <= 999 && this.Hp >= 0 && this.Hp <= this.MaxHp;
}

public sealed record BattleStatus(string Key, string? Trainer, BattleCombatant[] Combatants, bool CatchEncounter = false, bool HideOpponent = false)
{
    public BattleStatus ForSpectator() => !this.HideOpponent ? this : this with
    {
        Combatants = this.Combatants.Select(mon => (mon.Position & 1) == 0 ? mon : mon with { Species = 0, Personality = 0 }).ToArray(),
    };

    public bool IsValid() => this.Key is { Length: > 0 and <= 100 } &&
        (this.Trainer is null || this.Trainer.Length <= 12 && !this.Trainer.Any(char.IsControl)) &&
        this.Combatants is { Length: > 0 and <= 4 } &&
        this.Combatants.All(mon => mon is not null && mon.IsValid()) &&
        this.Combatants.DistinctBy(mon => mon.Position).Count() == this.Combatants.Length;

    public IEnumerable<(int Position, int Damage)> DamageSince(BattleStatus? previous)
    {
        if (previous is null || previous.Key != this.Key) yield break;
        foreach (var mon in this.Combatants)
        {
            var before = previous.Combatants.FirstOrDefault(old => old.Position == mon.Position);
            if (before is not null && before.Personality == mon.Personality && before.Species == mon.Species &&
                before.MaxHp == mon.MaxHp && before.Hp > mon.Hp)
                yield return (mon.Position, before.Hp - mon.Hp);
        }
    }
}

public sealed record WildBattleResult(int Area, long Started, long Ended, int Outcome);
public sealed record PendingDeath(Guid PokemonId, int Species, BattleTrace? Battle, Guid IncidentId = default, DateTimeOffset? At = null);
public sealed record PokemonStats(int Hp, int Attack, int Defense, int Speed, int SpecialAttack, int SpecialDefense);

public sealed record PokemonState
{
    public string Identity { get; init; } = "";
    public int Species { get; init; }
    public string Nickname { get; init; } = "";
    public uint Experience { get; init; }
    public int? Level { get; init; }
    public int? Hp { get; init; }
    public int? MaxHp { get; init; }
    public PokemonStats? Stats { get; init; }
    public uint Status { get; init; }
    public bool IsEgg { get; init; }
    public bool IsShiny { get; init; }
    public byte? RubyType1 { get; init; }
    public byte? RubyType2 { get; init; }
    public int MetArea { get; init; }
    public Placement Container { get; init; }
    public int Slot { get; init; }
    public int? Box { get; init; }
    public string DisplayName => SpeciesCatalog.Name(this.Species);
}

public sealed record GameSnapshot
{
    public string GameCode { get; init; } = "AXVD";
    public string LayoutId { get; init; } = "";
    public string Epoch { get; init; } = "";
    public long Frame { get; init; }
    public long PlaySeconds { get; init; }
    public uint? TrainerId { get; init; }
    public int? Money { get; init; }
    public int MapGroup { get; init; }
    public int MapNumber { get; init; }
    public int Area { get; init; }
    public MapPosition? Position { get; init; }
    public string AreaName => AreaCatalog.Name(this.Area);
    public bool InBattle { get; init; }
    public BattleStatus? Battle { get; init; }
    public BattleTrace[]? Faints { get; init; }
    public WildBattleResult[]? WildBattles { get; init; }
    public byte? BadgeMask { get; init; }
    public bool RubyStarterWindow { get; init; }
    public int[]? GymLevels { get; init; }
    public int[]?[]? GymLineups { get; init; }
    public List<PokemonState> Pokemon { get; init; } = [];
}

public sealed record JoinRequest(string Invitation, string Name, Guid ClientId, string Token, string GameCode,
    int Version = Protocol.Version, PlayerMarker Marker = PlayerMarker.Circle);
public sealed record Registration(Guid RunId, Guid PlayerId, Guid ClientId);
public sealed record Hello(Guid ClientId, int Version = Protocol.Version);
public sealed record SessionInfo(Guid SessionId, Guid RunId, Guid PlayerId, long NextSequence);
public sealed record Observation(Guid EventId, Guid SessionId, long Sequence, bool Baseline, GameSnapshot Snapshot);
public sealed record ObservationResult(bool Accepted, string State, string Message, long NextSequence);

public sealed class AutomationRun
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string GuildId { get; set; } = "";
    public ulong ChannelId { get; set; }
    public string Name { get; set; } = "";
    public string Edition { get; set; } = "ruby";
    public RunPhase Phase { get; set; } = RunPhase.Lobby;
    public bool Selected { get; set; } = true;
    public string Invitation { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string? EndReason { get; set; }
    public DeathRule DeathRule { get; set; }
    public List<AutomationPlayer> Players { get; set; } = [];
    public byte ArenaProgress => this.Players.Count == 0 ? (byte)0 :
        this.Players.Aggregate(byte.MaxValue, (shared, player) => (byte)(shared & player.ArenaProgress));
    public List<Encounter> Encounters { get; set; } = [];
    public List<AutomationEvent> History { get; set; } = [];
    public List<string> AdditionalRules { get; set; } = [];
}

public sealed class AutomationPlayer
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ClientId { get; set; }
    public string Name { get; set; } = "";
    public long Sequence { get; set; }
    public PlayerMarker Marker { get; set; }
    public string Connection { get; set; } = "Offline";
    public string Diagnostic { get; set; } = "Waiting for the first snapshot.";
    public DateTimeOffset? LastSeen { get; set; }
    public GameSnapshot? LastSnapshot { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DisplayArea { get; set; }
    public MapPosition? DisplayPosition { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BattleStatus? DisplayBattle { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public EncounterCatchVerdict[]? DisplayCatchChecks { get; set; }
    public byte ArenaConfirmed { get; set; }
    public byte ArenaReverted { get; set; }
    public byte ArenaProgress => (byte)(((this.LastSnapshot?.BadgeMask ?? this.DisplayBadges ?? 0) | this.ArenaConfirmed) & ~this.ArenaReverted);
    public byte? DisplayBadges { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? DisplayMoney { get; set; }
    public bool RebaselineAllowed { get; set; }
    public uint? TrainerId { get; set; }
    public string? RunStartEpoch { get; set; }
    public long RunStartFrame { get; set; }
    public bool NeedsActiveBaseline { get; set; }
    public HashSet<int> FirstWildAreas { get; set; } = [];
    public List<TrackedPokemon> Pokemon { get; set; } = [];
}

public sealed class TrackedPokemon
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public PokemonState State { get; set; } = new();
    public bool Dead { get; set; }
    public bool Present { get; set; } = true;
    public bool Imported { get; set; }
    public int ZeroHpSamples { get; set; }
    public PendingDeath? PendingDeath { get; set; }
    public BattleTrace? FaintBattle { get; set; }
    public bool FaintDismissed { get; set; }
}

public sealed class Encounter
{
    // Game snapshots only contain byte-sized area IDs.
    public const int StarterArea = 256;

    public Guid Id { get; set; } = Guid.NewGuid();
    public int Area { get; set; }
    public int MapGroup { get; set; }
    public int MapNumber { get; set; }
    public string Name => this.Area == StarterArea ? "Starter" : AreaCatalog.Name(this.Area);
    public Guid? GroupId { get; set; }
    public bool Dead { get; set; }
    public bool Failed { get; set; }
    public Dictionary<Guid, Guid> Members { get; set; } = [];
}

public sealed record AutomationEvent(Guid Id, DateTimeOffset At, string Kind, string Message,
    Guid? PlayerId = null, int? Area = null, int? MapGroup = null, int? MapNumber = null, MapPosition? Position = null,
    EventDetails? Details = null);
public sealed record EventPokemon(string Player, int Species, bool Shiny = false);
public sealed record EventDetails(EventPokemon? Pokemon = null, EventPokemon[]? Members = null, CatchCheck? CatchCheck = null,
    bool? AreaAvailable = null, BattleTrace? Battle = null, string? Reason = null, bool? HasLink = null);
public sealed record Notification(Guid Id, Guid RunId, ulong ChannelId, string Message);

public sealed record CreateRun(string Guild, string Name, ulong Channel = 0, DeathRule DeathRule = DeathRule.ConfirmFaint);
public sealed record RunAction(string Guild, string? Reason = null);
public sealed record RunGroup(Guid Id, string Name);
public sealed record CreateGroupRequest(string Name);
public sealed record GroupRunRequest(string Name, DeathRule DeathRule = DeathRule.ConfirmFaint);
public sealed record HostRun(Guid Id, string Name, RunPhase Phase, bool Selected, string Invitation, int Players, int Ready);
public sealed record HostPlayer(Guid Id, string Name, string Connection, string Diagnostic);
public sealed record HostStarterPlayer(Guid Id, string Name, HostStarterPokemon[] Pokemon);
public sealed record HostStarterPokemon(Guid Id, int Species, string Nickname, int? Level);
public sealed record StarterLinkRequest(Dictionary<Guid, Guid>? Members);
public sealed record HostLinkEditor(string Revision, HostLinkPlayer[] Players, HostLinkRow[] Links);
public sealed record HostLinkPlayer(Guid Id, string Name, HostLinkPokemon[] Pokemon);
public sealed record HostLinkPokemon(Guid Id, int Species, string Nickname, int? Level, int MetArea, Placement Container);
public sealed record HostLinkRow(int Area, bool Locked, Dictionary<Guid, Guid> Members);
public sealed record HostLinkEdit(int Area, Dictionary<Guid, Guid> Members);
public sealed record EditLinksRequest(string Revision, HostLinkEdit[]? Links);
