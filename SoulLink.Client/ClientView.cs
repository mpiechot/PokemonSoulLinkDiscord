using SoulLink;

namespace SoulLink.Client;

public sealed record ClientView(
    string Player, string Server, bool Registered, string State, int BridgePort, string Bridge,
    DateTimeOffset? LastBridge, GameSnapshot? Snapshot, ClientRunView? Run, string[] Log, bool IsDemo,
    bool ReadOnly = true);

public sealed record ClientRunView(string Name, RunPhase Phase, Guid Id, ClientPlayerView[] Players,
    string[] Lines, AutomationEvent[] History);

public sealed record ClientPlayerView(string Name, Guid Id, string Diagnostic, string Connection);
