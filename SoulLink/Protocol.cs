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
public enum DeathRule { Disabled, ZeroHpOutsideBattle }
public enum Placement { Party, Box }
public enum PlayerMarker { Circle, Diamond, Star, Triangle, Square, Heart, Moon, Sun }

public sealed record PokemonState
{
    public string Identity { get; init; } = "";
    public int Species { get; init; }
    public string Nickname { get; init; } = "";
    public uint Experience { get; init; }
    public int? Level { get; init; }
    public int? Hp { get; init; }
    public int? MaxHp { get; init; }
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
    public int MapGroup { get; init; }
    public int MapNumber { get; init; }
    public int Area { get; init; }
    public string AreaName => AreaCatalog.Name(this.Area);
    public bool InBattle { get; init; }
    public byte? BadgeMask { get; init; }
    public int[]? GymLevels { get; init; }
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
    public List<Encounter> Encounters { get; set; } = [];
    public List<AutomationEvent> History { get; set; } = [];
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
    public bool RebaselineAllowed { get; set; }
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
}

public sealed class Encounter
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int Area { get; set; }
    public int MapGroup { get; set; }
    public int MapNumber { get; set; }
    public string Name => AreaCatalog.Name(this.Area);
    public Guid? GroupId { get; set; }
    public bool Dead { get; set; }
    public bool Failed { get; set; }
    public Dictionary<Guid, Guid> Members { get; set; } = [];
}

public sealed record AutomationEvent(Guid Id, DateTimeOffset At, string Kind, string Message);
public sealed record Notification(Guid Id, Guid RunId, ulong ChannelId, string Message);

public sealed record CreateRun(string Guild, string Name, ulong Channel = 0, DeathRule DeathRule = DeathRule.Disabled);
public sealed record RunAction(string Guild, string? Reason = null);
