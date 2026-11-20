using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace SoulLink;

/// <summary>The server owns encounter rules; clients supply read-only observations.</summary>
public sealed partial class AutomationEngine(AutomationStore store, TimeProvider? timeProvider = null)
{
    private readonly TimeProvider clock = timeProvider ?? TimeProvider.System;
    private readonly ConcurrentDictionary<Guid, LiveSession> sessions = new();

    public List<AutomationRun> Runs(string guild) => store.Execute(tx => tx.Runs(guild));

    public AutomationRun Initialize(string guild, ulong channel, string name, DeathRule deathRule = DeathRule.Disabled)
    {
        ValidateName(name);
        if (string.IsNullOrWhiteSpace(guild)) throw new InvalidDataException("A guild is required.");
        return store.Execute(tx =>
        {
            foreach (var previous in tx.Runs(guild).Where(r => r.Selected))
            {
                if (previous.Phase == RunPhase.Lobby) tx.Delete(previous.Id);
                else
                {
                    previous.Selected = false;
                    if (previous.Phase == RunPhase.Active) previous.Phase = RunPhase.Paused;
                    tx.Save(previous);
                }
            }

            var run = new AutomationRun
            {
                GuildId = guild,
                ChannelId = channel,
                Name = name,
                DeathRule = deathRule,
                Invitation = Convert.ToHexString(RandomNumberGenerator.GetBytes(12)),
            };
            tx.Save(run);
            return run;
        });
    }

    public AutomationRun ChangeRun(string guild, Guid runId, string action, string? reason = null)
    {
        return store.Execute(tx =>
        {
            var run = tx.Run(runId);
            if (run.GuildId != guild) throw new UnauthorizedAccessException("Run belongs to another guild.");
            switch (action)
            {
                case "start":
                    if (!run.Selected || run.Phase != RunPhase.Lobby || run.Players.Count == 0)
                        throw new InvalidOperationException("Select a lobby with registered players before starting.");
                    if (run.Players.Any(p => p.LastSnapshot is null || p.Connection == "OutOfSync"))
                        throw new InvalidOperationException("Every player must send a valid baseline before starting.");
                    run.Phase = RunPhase.Active;
                    run.Invitation = "";
                    break;
                case "load":
                    if (run.Phase == RunPhase.Finished) throw new InvalidOperationException("Finished runs cannot be resumed.");
                    foreach (var previous in tx.Runs(guild).Where(r => r.Selected && r.Id != run.Id))
                    {
                        if (previous.Phase == RunPhase.Lobby) tx.Delete(previous.Id);
                        else
                        {
                            previous.Selected = false;
                            if (previous.Phase == RunPhase.Active) previous.Phase = RunPhase.Paused;
                            tx.Save(previous);
                        }
                    }
                    run.Selected = true;
                    if (run.Phase == RunPhase.Paused) run.Phase = RunPhase.Active;
                    foreach (var player in run.Players) this.sessions.TryRemove(player.Id, out _);
                    break;
                case "pause":
                    if (run.Phase != RunPhase.Active) throw new InvalidOperationException("Only an active run can be paused.");
                    run.Phase = RunPhase.Paused;
                    break;
                case "end":
                    run.Phase = RunPhase.Finished;
                    run.Selected = false;
                    run.Invitation = "";
                    run.EndReason = reason ?? "Ended by administrator.";
                    break;
                default: throw new InvalidDataException("Unknown run action.");
            }

            tx.Save(run);
            return run;
        });
    }

    public Registration Join(JoinRequest request)
    {
        if (request.Version != Protocol.Version) throw new InvalidDataException("Protocol version unsupported; update the client.");
        ValidateName(request.Name);
        if (!Enum.IsDefined(request.Marker)) throw new InvalidDataException("Unknown player marker.");
        if (request.ClientId == Guid.Empty || request.Token.Length != 64 || !HexToken().IsMatch(request.Token))
            throw new InvalidDataException("Invalid client registration.");
        if (request.GameCode != "AXVD") throw new InvalidDataException("Only the German Ruby AXVD read-only adapter is currently enabled.");
        return store.Execute(tx =>
        {
            string hash = HashToken(request.Token);
            var existing = tx.Registration(hash);
            if (existing is not null)
            {
                if (existing.ClientId != request.ClientId) throw new UnauthorizedAccessException("Client mismatch.");
                return existing;
            }

            var run = tx.Runs().SingleOrDefault(r => r.Invitation == request.Invitation && r.Selected && r.Phase == RunPhase.Lobby)
                ?? throw new UnauthorizedAccessException("Invitation invalid, expired, or lobby already started.");
            if (run.Players.Count >= 12 || run.Players.Any(p => string.Equals(p.Name, request.Name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Player name already used or lobby full.");
            var player = new AutomationPlayer { ClientId = request.ClientId, Name = request.Name, Marker = request.Marker };
            run.Players.Add(player);
            var registration = new Registration(run.Id, player.Id, request.ClientId);
            tx.Save(run);
            tx.Register(hash, registration);
            return registration;
        });
    }

    public Registration Authenticate(string token) => store.Execute(tx =>
        tx.Registration(HashToken(token)) ?? throw new UnauthorizedAccessException("Invalid client token."));

    public SessionInfo Connect(Registration registration, Hello hello)
    {
        if (hello.Version != Protocol.Version || hello.ClientId != registration.ClientId)
            throw new UnauthorizedAccessException("Incompatible client or protocol version.");
        return store.Execute(tx =>
        {
            var run = tx.Run(registration.RunId);
            var player = run.Players.Single(p => p.Id == registration.PlayerId);
            var session = new LiveSession(Guid.NewGuid(), this.clock.GetUtcNow());
            this.sessions[player.Id] = session;
            if (player.Connection != "OutOfSync") player.Connection = "Synchronizing";
            player.LastSeen = this.clock.GetUtcNow();
            tx.Save(run);
            return new SessionInfo(session.Id, run.Id, player.Id, player.Sequence + 1);
        });
    }

    public AutomationRun View(Registration registration) => store.Execute(tx => tx.Run(registration.RunId));

    public ObservationResult Observe(Registration registration, Observation observation)
    {
        ValidateSnapshot(observation.Snapshot);
        if (observation.EventId == Guid.Empty) throw new InvalidDataException("Event ID required.");
        return store.Execute(tx =>
        {
            var run = tx.Run(registration.RunId);
            var player = run.Players.Single(p => p.Id == registration.PlayerId);
            if (!this.sessions.TryGetValue(player.Id, out var session) || session.Id != observation.SessionId)
                throw new UnauthorizedAccessException("Session expired; reconnect before continuing.");
            if (this.clock.GetUtcNow() - session.LastSeen > TimeSpan.FromSeconds(45))
                throw new UnauthorizedAccessException("Stale session; reconnect with a baseline.");
            if (tx.HasReceipt(player.Id, observation.EventId, observation.Sequence))
                return new ObservationResult(true, player.Connection, "Already acknowledged.", player.Sequence + 1);
            if (observation.Sequence != player.Sequence + 1)
                return this.OutOfSync(tx, run, player, "Sequence gap or out-of-order observation; reconnect and reconcile.");
            if (player.Connection == "OutOfSync" && !player.RebaselineAllowed)
                return new ObservationResult(false, "OutOfSync", player.Diagnostic, player.Sequence + 1);
            if (!session.Ready && !observation.Baseline)
                return new ObservationResult(false, "Synchronizing", "A reconnect baseline is required.", player.Sequence + 1);
            if (!run.Selected || run.Phase is RunPhase.Paused or RunPhase.Finished)
                return new ObservationResult(false, "Paused", "Run is paused, inactive or finished.", player.Sequence + 1);

            var snapshot = observation.Snapshot;
            var previous = player.LastSnapshot;
            bool baseline = !session.Ready || observation.Baseline;
            if (!baseline && previous is not null && snapshot.Epoch == previous.Epoch && snapshot.Frame == previous.Frame)
                return new ObservationResult(false, "Ready", "Waiting for a fresh emulator frame.", player.Sequence + 1);
            if (previous is not null && !player.RebaselineAllowed)
            {
                if (snapshot.PlaySeconds + 1 < previous.PlaySeconds ||
                    (snapshot.Epoch == previous.Epoch && snapshot.Frame < previous.Frame))
                    return this.OutOfSync(tx, run, player, "Game time moved backwards; possible save-state load.");
                if (session.Ready && snapshot.Epoch != previous.Epoch)
                    return this.OutOfSync(tx, run, player, "The emulator reset or loaded a state. Explicit reconciliation required.");
                if (baseline && !SameInventory(previous, snapshot))
                    return this.OutOfSync(tx, run, player, "Inventory changed while disconnected; offline events are not replayed.");
                if (previous.Pokemon.Any(old => !snapshot.Pokemon.Any(p => p.Identity == old.Identity)))
                    return this.OutOfSync(tx, run, player, "A known Pokémon disappeared. Check release, daycare, trade or rollback before rebaselining.");
                if (snapshot.Pokemon.Any(p => player.Pokemon.Any(old => old.State.Identity == p.Identity && old.State.Experience > p.Experience)))
                    return this.OutOfSync(tx, run, player, "Pokémon experience moved backwards.");
            }

            bool import = baseline || run.Phase == RunPhase.Lobby;
            foreach (var tracked in player.Pokemon) tracked.Present = false;
            foreach (var state in snapshot.Pokemon)
            {
                var tracked = player.Pokemon.SingleOrDefault(p => p.State.Identity == state.Identity);
                if (tracked is null)
                {
                    tracked = new TrackedPokemon { State = state, Imported = import };
                    player.Pokemon.Add(tracked);
                    if (!import && !state.IsEgg) this.Catch(tx, run, player, tracked, snapshot);
                }
                else if (!import && tracked.State.Species != state.Species)
                {
                    this.Event(tx, run, "evolved", $"{run.Name}: {player.Name} — {tracked.State.DisplayName} → {state.DisplayName}.");
                }

                tracked.State = state;
                tracked.Present = true;
                if (!import && run.DeathRule == DeathRule.ZeroHpOutsideBattle && !snapshot.InBattle && state.Hp == 0 && !state.IsEgg)
                    tracked.ZeroHpSamples++;
                else tracked.ZeroHpSamples = 0;
                if (tracked.ZeroHpSamples >= 2 && !tracked.Dead) this.MarkDeath(tx, run, player, tracked);
            }

            player.LastSnapshot = snapshot;
            if (!import && previous?.BadgeMask is byte oldBadges && snapshot.BadgeMask is byte newBadges)
            {
                int acquired = System.Numerics.BitOperations.PopCount((uint)(newBadges & ~oldBadges));
                if (acquired > 0) this.Event(tx, run, "badge", $"{player.Name}: +{acquired} Orden.", notify: false);
            }
            player.Sequence = observation.Sequence;
            player.Connection = "Ready";
            player.Diagnostic = import ? "Baseline accepted; existing Pokémon imported without catch events." : "Observation accepted.";
            player.RebaselineAllowed = false;
            player.LastSeen = this.clock.GetUtcNow();
            tx.Receipt(player.Id, observation.EventId, observation.Sequence);
            tx.Save(run);
            session.Ready = true;
            session.LastSeen = this.clock.GetUtcNow();
            return new ObservationResult(true, player.Connection, player.Diagnostic, player.Sequence + 1);
        });
    }

    public string Heartbeat(Registration registration, Guid sessionId, bool? bridgeAvailable = null)
    {
        if (!this.sessions.TryGetValue(registration.PlayerId, out var session) || session.Id != sessionId)
            throw new UnauthorizedAccessException("Session expired.");
        if (this.clock.GetUtcNow() - session.LastSeen > TimeSpan.FromSeconds(45))
        {
            this.sessions.TryRemove(registration.PlayerId, out _);
            throw new UnauthorizedAccessException("Session went offline; reconnect with a baseline.");
        }
        session.LastSeen = this.clock.GetUtcNow();
        if (bridgeAvailable.HasValue) session.BridgeAvailable = bridgeAvailable.Value;
        return session.Ready ? "Online" : "Synchronizing";
    }

    public string Connection(AutomationPlayer player)
    {
        if (!this.sessions.TryGetValue(player.Id, out var session)) return "Offline";
        var age = this.clock.GetUtcNow() - session.LastSeen;
        if (age > TimeSpan.FromSeconds(45)) return "Offline";
        if (age > TimeSpan.FromSeconds(15)) return "Stale";
        if (session.BridgeAvailable == false && player.Connection == "Ready") return "Stale";
        return player.Connection;
    }

    public void Rebaseline(string guild, Guid runId, Guid playerId)
    {
        store.Execute(tx =>
        {
            var run = tx.Run(runId);
            if (run.GuildId != guild) throw new UnauthorizedAccessException("Wrong guild.");
            var player = run.Players.Single(p => p.Id == playerId);
            player.RebaselineAllowed = true;
            player.Connection = "Synchronizing";
            this.sessions.TryRemove(player.Id, out _);
            tx.Save(run);
            return true;
        });
    }

    public void SetEncounterFailed(string guild, Guid runId, int area)
    {
        if (area is < 0 or > 255) throw new InvalidDataException("Invalid region section ID.");
        store.Execute(tx =>
        {
            var run = tx.Run(runId);
            if (run.GuildId != guild) throw new UnauthorizedAccessException("Wrong guild.");
            var encounter = run.Encounters.SingleOrDefault(e => e.Area == area);
            if (encounter?.GroupId is not null) throw new InvalidOperationException("A completed link cannot be marked as a missed encounter.");
            encounter ??= new Encounter { Area = area };
            if (!run.Encounters.Contains(encounter)) run.Encounters.Add(encounter);
            encounter.Failed = true;
            this.Event(tx, run, "encounter-failed", $"{run.Name}: {encounter.Name} — encounter failed (manual confirmation).");
            tx.Save(run);
            return true;
        });
    }

    public void SetDeath(string guild, Guid runId, int area, bool dead)
    {
        store.Execute(tx =>
        {
            var run = tx.Run(runId);
            if (run.GuildId != guild) throw new UnauthorizedAccessException("Wrong guild.");
            var encounter = run.Encounters.SingleOrDefault(e => e.Area == area)
                ?? throw new InvalidOperationException("No encounter found for this area.");
            if (encounter.Dead == dead) return true;
            encounter.Dead = dead;
            foreach (var member in encounter.Members)
            {
                var pokemon = run.Players.Single(p => p.Id == member.Key).Pokemon.Single(p => p.Id == member.Value);
                pokemon.Dead = dead;
                pokemon.ZeroHpSamples = 0;
            }
            this.Event(tx, run, dead ? "death-confirmed" : "death-undone",
                $"{run.Name}: {encounter.Name} — link death {(dead ? "confirmed" : "undone")} by administrator. Game memory is unchanged.");
            tx.Save(run);
            return true;
        });
    }

    private static bool SameInventory(GameSnapshot left, GameSnapshot right) =>
        left.Pokemon.Count == right.Pokemon.Count && left.Pokemon.All(a => right.Pokemon.Any(b =>
            a.Identity == b.Identity && a.Species == b.Species && a.Experience == b.Experience && a.IsEgg == b.IsEgg));

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 64 || name.Any(c => char.IsControl(c) || c is '@' or '`' or '<' or '>'))
            throw new InvalidDataException("Use a name of 1–64 characters without mentions, markup or control characters.");
    }

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static void ValidateSnapshot(GameSnapshot snapshot)
    {
        if (snapshot.GameCode != "AXVD" || snapshot.Epoch.Length is < 1 or > 80 || snapshot.LayoutId.Length is < 1 or > 80 ||
            snapshot.Frame < 0 || snapshot.PlaySeconds < 0 || snapshot.Area is < 0 or > 255 ||
            snapshot.MapGroup is < 0 or > 255 || snapshot.MapNumber is < 0 or > 255 || snapshot.Pokemon.Count > 426)
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

    private ObservationResult OutOfSync(AutomationStore.StoreTransaction tx, AutomationRun run, AutomationPlayer player, string reason)
    {
        player.Connection = "OutOfSync";
        player.Diagnostic = reason;
        tx.Save(run);
        return new ObservationResult(false, "OutOfSync", reason, player.Sequence + 1);
    }

    private void Catch(AutomationStore.StoreTransaction tx, AutomationRun run, AutomationPlayer player, TrackedPokemon pokemon, GameSnapshot snapshot)
    {
        int area = pokemon.State.MetArea;
        var encounter = run.Encounters.SingleOrDefault(e => e.Area == area);
        encounter ??= new Encounter { Area = area, MapGroup = snapshot.MapGroup, MapNumber = snapshot.MapNumber };
        if (!run.Encounters.Contains(encounter)) run.Encounters.Add(encounter);
        if (encounter.Failed || encounter.Dead || encounter.Members.ContainsKey(player.Id))
        {
            this.Event(tx, run, "extra-catch", $"{run.Name}: {player.Name} — extra {pokemon.State.DisplayName} at {encounter.Name}; area already used, no new link.");
            return;
        }

        encounter.Members[player.Id] = pokemon.Id;
        this.Event(tx, run, "caught", $"{run.Name}: {player.Name} caught {pokemon.State.DisplayName} at {encounter.Name} ({encounter.Members.Count}/{run.Players.Count}).");
        if (encounter.Members.Count == run.Players.Count && encounter.GroupId is null)
        {
            encounter.GroupId = Guid.NewGuid();
            string members = string.Join(" · ", run.Players.Select(p => $"{p.Name}: {p.Pokemon.Single(m => m.Id == encounter.Members[p.Id]).State.DisplayName}"));
            this.Event(tx, run, "linked", $"{run.Name}: Soul-Link complete at {encounter.Name} — {members}.");
        }
    }

    private void MarkDeath(AutomationStore.StoreTransaction tx, AutomationRun run, AutomationPlayer player, TrackedPokemon pokemon)
    {
        pokemon.Dead = true;
        foreach (var encounter in run.Encounters.Where(e => e.Members.GetValueOrDefault(player.Id) == pokemon.Id))
        {
            encounter.Dead = true;
            foreach (var member in encounter.Members)
                run.Players.Single(p => p.Id == member.Key).Pokemon.Single(p => p.Id == member.Value).Dead = true;
        }
        this.Event(tx, run, "death", $"{run.Name}: {player.Name}'s {pokemon.State.DisplayName} fainted; linked partners are marked dead. Game memory is unchanged.");
    }

    private void Event(AutomationStore.StoreTransaction tx, AutomationRun run, string kind, string message, bool notify = true)
    {
        var item = new AutomationEvent(Guid.NewGuid(), this.clock.GetUtcNow(), kind, message);
        run.History.Add(item);
        if (run.History.Count > 200) run.History.RemoveAt(0);
        if (notify) tx.Notify(run, item);
    }

    [GeneratedRegex("^[0-9A-Fa-f]{64}$")]
    private static partial Regex HexToken();

    [GeneratedRegex("^[0-9A-F]{8}:[0-9A-F]{8}$")]
    private static partial Regex IdentityFormat();

    private sealed class LiveSession(Guid id, DateTimeOffset lastSeen)
    {
        public Guid Id { get; } = id;
        public bool Ready { get; set; }
        public bool? BridgeAvailable { get; set; }
        public DateTimeOffset LastSeen { get; set; } = lastSeen;
    }
}
