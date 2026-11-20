using SoulLink;

namespace SoulLink.Client;

public sealed record ClientView(
    string Player, string Server, bool Registered, string State, int BridgePort, string Bridge,
    DateTimeOffset? LastBridge, GameSnapshot? Snapshot, ClientRunView? Run, string[] Log, bool IsDemo,
    bool ReadOnly = true, Guid? LocalPlayerId = null, PlayerMarker Marker = PlayerMarker.Circle, bool IsLocalRun = false);

public sealed record ClientRunView(string Name, RunPhase Phase, Guid Id, ClientPlayerView[] Players,
    string[] Lines, AutomationEvent[] History, DashboardLink[]? Links = null, int[]? CaughtAreas = null, string[]? AdditionalRules = null);

public sealed record ClientPlayerView(string Name, Guid Id, string Diagnostic, string Connection,
    PlayerMarker Marker = PlayerMarker.Circle, int? Area = null, DashboardPokemon[]? Pokemon = null,
    MapPosition? Position = null, byte ArenaProgress = 0, PendingDeath[]? PendingDeaths = null, BattleStatus? Battle = null);

public sealed record DashboardPokemon(PokemonState State, bool Dead, bool Present);
public sealed record DashboardLinkMember(string Player, DashboardPokemon Pokemon, Guid PlayerId = default);
public sealed record DashboardLink(string Area, string State, DashboardLinkMember[] Members);
