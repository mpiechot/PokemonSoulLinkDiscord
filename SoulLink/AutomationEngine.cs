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
        return store.Execute(tx => CreateRunInScope(tx, guild, channel, name, deathRule));
    }

    public RunGroup[] Groups() => store.Execute(tx => tx.Groups());

    public RunGroup CreateGroup(string name)
    {
        name = name?.Trim() ?? "";
        ValidateName(name);
        return store.Execute(tx =>
        {
            if (tx.Groups().Any(group => string.Equals(group.Name, name, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("Eine Gruppe mit diesem Namen gibt es bereits.");
            var group = new RunGroup(Guid.NewGuid(), name);
            tx.AddGroup(group);
            return group;
        });
    }

    public HostRun[] GroupRuns(Guid groupId) => store.Execute(tx =>
    {
        _ = tx.Group(groupId);
        return tx.Runs(GroupScope(groupId)).Select(ToHostRun).ToArray();
    });

    public HostRun CreateGroupRun(Guid groupId, GroupRunRequest request)
    {
        string name = request.Name?.Trim() ?? "";
        ValidateName(name);
        if (!Enum.IsDefined(request.DeathRule)) throw new InvalidDataException("Ungültige Todesregel.");
        return store.Execute(tx =>
        {
            _ = tx.Group(groupId);
            return ToHostRun(CreateRunInScope(tx, GroupScope(groupId), 0, name, request.DeathRule));
        });
    }

    public HostRun ChangeGroupRun(Guid groupId, Guid runId, string action) =>
        ToHostRun(this.ChangeRun(GroupScope(groupId), runId, action));

    public HostPlayer[] GroupRunPlayers(Guid groupId, Guid runId) => store.Execute(tx =>
    {
        var run = RequireGroupRun(tx, groupId, runId);
        return run.Players.Select(p => new HostPlayer(p.Id, p.Name, this.Connection(p), p.Diagnostic)).ToArray();
    });

    public void RebaselineGroupRun(Guid groupId, Guid runId, Guid playerId) => this.Rebaseline(GroupScope(groupId), runId, playerId);

    public HostLinkEditor GroupLinkEditor(Guid groupId, Guid runId) => store.Execute(tx =>
        BuildLinkEditor(RequireGroupRun(tx, groupId, runId)));

    public void EditGroupLinks(Guid groupId, Guid runId, EditLinksRequest request) => store.Execute(tx =>
    {
        var run = RequireGroupRun(tx, groupId, runId);
        var editor = BuildLinkEditor(run);
        if (editor.Revision != request.Revision)
            throw new InvalidOperationException("Die Links haben sich geändert. Öffne das Menü erneut, bevor du speicherst.");

        var edits = request.Links ?? throw new InvalidDataException("Keine Link-Daten erhalten.");
        var locked = editor.Links.Where(row => row.Locked).ToArray();
        var lockedAreas = locked.Select(row => row.Area).ToHashSet();
        if (edits.Length > Encounter.StarterArea + 1 || edits.Any(row => row is null || row.Members is null ||
            row.Area is < 0 or > Encounter.StarterArea || lockedAreas.Contains(row.Area)) ||
            edits.Select(row => row.Area).Distinct().Count() != edits.Length)
            throw new InvalidDataException("Link-Daten enthalten einen ungültigen, geschützten oder doppelten Fangort.");

        var protectedPokemon = locked.SelectMany(row => row.Members.Values).ToHashSet();
        var eligible = editor.Players.ToDictionary(player => player.Id, player => player.Pokemon
            .Where(mon => !protectedPokemon.Contains(mon.Id)).Select(mon => mon.Id).ToHashSet());
        var assigned = new HashSet<Guid>();
        foreach (var pair in edits.SelectMany(row => row.Members))
        {
            if (!eligible.TryGetValue(pair.Key, out var available) || !available.Contains(pair.Value) || !assigned.Add(pair.Value))
                throw new InvalidDataException("Jedes Pokémon kann nur einem Link und seinem eigenen Spieler zugeordnet werden.");
        }

        var desired = edits.Where(row => row.Members.Count > 0).ToDictionary(row => row.Area);
        bool changed = run.Encounters.RemoveAll(link => !lockedAreas.Contains(link.Area) && !desired.ContainsKey(link.Area)) > 0;
        foreach (var row in desired.Values)
        {
            var encounter = run.Encounters.SingleOrDefault(link => link.Area == row.Area);
            if (encounter is not null && encounter.Members.Count == row.Members.Count && encounter.Members.All(pair =>
                row.Members.TryGetValue(pair.Key, out Guid id) && id == pair.Value)) continue;
            if (encounter is null)
            {
                encounter = row.Area == Encounter.StarterArea ? InsertStarterLink(run) : new Encounter { Area = row.Area };
                if (row.Area != Encounter.StarterArea) run.Encounters.Add(encounter);
            }

            encounter.Members = new(row.Members);
            if (encounter.Members.Count != run.Players.Count) encounter.GroupId = null;
            this.CompleteLink(tx, run, encounter);
            changed = true;
        }

        if (!changed) return false;
        this.Event(tx, run, "links-corrected", "Der Gastgeber hat die Link-Zuordnungen korrigiert.", notify: false);
        tx.Save(run);
        return true;
    });

    private static HostLinkEditor BuildLinkEditor(AutomationRun run)
    {
        if (run.Phase is not (RunPhase.Active or RunPhase.Paused))
            throw new InvalidOperationException("Link-Zuordnungen sind in aktiven und pausierten Runs bearbeitbar.");

        var editable = run.Players.SelectMany(player => player.Pokemon)
            .Where(pokemon => pokemon.Present && !pokemon.Dead && !pokemon.State.IsEgg).Select(pokemon => pokemon.Id).ToHashSet();
        var assigned = run.Encounters.SelectMany(link => link.Members.Values).ToHashSet();
        var players = run.Players.Select(player => new HostLinkPlayer(player.Id, player.Name, player.Pokemon
            .Where(mon => editable.Contains(mon.Id) || assigned.Contains(mon.Id))
            .Select(mon => new HostLinkPokemon(mon.Id, mon.State.Species, mon.State.Nickname, mon.State.Level, mon.State.MetArea, mon.State.Container))
            .ToArray())).ToArray();
        var links = run.Encounters.OrderBy(link => link.Area == Encounter.StarterArea ? -1 : link.Area)
            .Select(link => new HostLinkRow(link.Area, link.Dead || link.Failed || link.Members.Values.Any(id => !editable.Contains(id)),
                link.Members.OrderBy(pair => pair.Key).ToDictionary())).ToArray();
        string revision = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Protocol.Serialize(new { run.Phase, Links = links }))));
        return new HostLinkEditor(revision, players, links);
    }

    public HostStarterPlayer[] GroupStarterCandidates(Guid groupId, Guid runId) => store.Execute(tx =>
        StarterCandidates(RequireGroupRun(tx, groupId, runId)));

    public void LinkGroupStarters(Guid groupId, Guid runId, StarterLinkRequest request) => store.Execute(tx =>
    {
        var run = RequireGroupRun(tx, groupId, runId);
        var members = request.Members ?? throw new InvalidDataException("Wähle einen Starter pro Spieler aus.");
        var existing = run.Encounters.SingleOrDefault(link => link.Area == Encounter.StarterArea);
        if (existing is { GroupId: not null })
        {
            if (members.Count == existing.Members.Count && members.All(pair =>
                existing.Members.TryGetValue(pair.Key, out Guid pokemon) && pokemon == pair.Value)) return true;
            throw new InvalidOperationException("Für diesen Run gibt es bereits einen Starter-Link.");
        }

        var candidates = StarterCandidates(run);
        if (members.Count != candidates.Length || candidates.Any(p =>
            !members.TryGetValue(p.Id, out Guid selected) || !p.Pokemon.Any(m => m.Id == selected)))
            throw new InvalidDataException("Wähle für jeden Spieler ein unverlinktes Pokémon aus dem Startbestand.");

        var starter = existing ?? InsertStarterLink(run);
        starter.Members = new(members);
        this.CompleteLink(tx, run, starter);
        tx.Save(run);
        return true;
    });

    private static AutomationRun RequireGroupRun(AutomationStore.StoreTransaction tx, Guid groupId, Guid runId)
    {
        var run = tx.Run(runId);
        if (run.GuildId != GroupScope(groupId)) throw new UnauthorizedAccessException("Der Run gehört zu einer anderen Gruppe.");
        return run;
    }

    private static HostStarterPlayer[] StarterCandidates(AutomationRun run)
    {
        if (run.Phase is not (RunPhase.Active or RunPhase.Paused) || run.Players.Count < 2)
            throw new InvalidOperationException("Der Starter-Link ist nach dem Run-Start mit mindestens zwei Spielern verfügbar.");
        var starter = run.Encounters.SingleOrDefault(link => link.Area == Encounter.StarterArea);
        if (starter is { GroupId: not null })
            throw new InvalidOperationException("Für diesen Run gibt es bereits einen Starter-Link.");
        if (starter is { Dead: true })
            throw new InvalidOperationException("Für den Starter-Link wurde bereits ein Todesfall erfasst.");

        var linked = run.Encounters.Where(link => link.Area != Encounter.StarterArea).SelectMany(link => link.Members.Values).ToHashSet();
        return run.Players.Select(p => new HostStarterPlayer(p.Id, p.Name, p.Pokemon
            .Where(m => m.Imported && m.Present && !m.Dead && !m.State.IsEgg && !linked.Contains(m.Id) &&
                (starter is null || !starter.Members.TryGetValue(p.Id, out Guid identified) || m.Id == identified))
            .Select(m => new HostStarterPokemon(m.Id, m.State.Species, m.State.Nickname, m.State.Level)).ToArray())).ToArray();
    }

    private static string GroupScope(Guid groupId) => $"group:{groupId:N}";

    private static HostRun ToHostRun(AutomationRun run) => new(run.Id, run.Name, run.Phase, run.Selected, run.Invitation,
        run.Players.Count, run.Players.Count(p => p.LastSnapshot is not null && p.Connection != "OutOfSync"));

    private static AutomationRun CreateRunInScope(AutomationStore.StoreTransaction tx, string guild, ulong channel, string name, DeathRule deathRule)
    {
        foreach (var previous in tx.Runs(guild).Where(r => r.Selected))
            Deselect(tx, previous);
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
    }

    private static void Deselect(AutomationStore.StoreTransaction tx, AutomationRun previous)
    {
        if (previous.Phase == RunPhase.Lobby && !previous.GuildId.StartsWith("group:", StringComparison.Ordinal))
        {
            tx.Delete(previous.Id);
            return;
        }
        previous.Selected = false;
        if (previous.Phase == RunPhase.Active) previous.Phase = RunPhase.Paused;
        tx.Save(previous);
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
                    foreach (var participant in run.Players)
                    {
                        participant.RunStartEpoch = participant.LastSnapshot?.Epoch;
                        participant.RunStartFrame = participant.LastSnapshot?.Frame ?? 0;
                    }
                    if (run.Encounters.SingleOrDefault(link => link.Area == Encounter.StarterArea) is { } starter)
                        this.CompleteLink(tx, run, starter);
                    break;
                case "load":
                    if (run.Phase == RunPhase.Finished) throw new InvalidOperationException("Finished runs cannot be resumed.");
                    foreach (var previous in tx.Runs(guild).Where(r => r.Selected && r.Id != run.Id))
                        Deselect(tx, previous);
                    run.Selected = true;
                    if (run.Phase == RunPhase.Paused)
                    {
                        run.Phase = RunPhase.Active;
                        foreach (var participant in run.Players) participant.NeedsActiveBaseline = true;
                    }
                    foreach (var player in run.Players) this.sessions.TryRemove(player.Id, out _);
                    break;
                case "pause":
                    if (run.Phase != RunPhase.Active) throw new InvalidOperationException("Only an active run can be paused.");
                    run.Phase = RunPhase.Paused;
                    break;
                case "end":
                    if (run.Phase == RunPhase.Finished) return run;
                    Finish(run, reason ?? "Ended by administrator.");
                    break;
                default: throw new InvalidDataException("Unknown run action.");
            }

            tx.Save(run);
            return run;
        });
    }

    public void CompleteWipedRun(Registration registration, Guid incidentId, string? reason)
    {
        reason = reason?.Trim();
        if (reason is { Length: > 250 } || reason?.Any(char.IsControl) == true)
            throw new InvalidDataException("Der Grund darf bis zu 250 Zeichen enthalten.");

        store.Execute(tx =>
        {
            var run = tx.Run(registration.RunId);
            var player = run.Players.Single(p => p.Id == registration.PlayerId && p.ClientId == registration.ClientId);
            if (run.Phase != RunPhase.Active || run.PendingEnd != new PendingRunEnd(incidentId, player.Id) ||
                run.Encounters.Any(IsLiveLink))
                throw new InvalidOperationException("Für dich ist kein Run-Ende mehr offen.");

            string endReason = string.IsNullOrEmpty(reason) ? "Alle Links sind tot." : reason;
            Finish(run, endReason);
            this.Event(tx, run, "run-ended", $"Run abgeschlossen: {endReason}", player: player,
                details: new EventDetails(Reason: endReason));
            tx.Save(run);
            return true;
        });
    }

    private static void Finish(AutomationRun run, string reason)
    {
        run.Phase = RunPhase.Finished;
        run.Selected = false;
        run.Invitation = "";
        run.EndReason = reason;
        run.PendingEnd = null;
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
            if (player.Connection != "OutOfSync" || player.Diagnostic != DifferentTrainer) player.Connection = "Synchronizing";
            player.LastSeen = this.clock.GetUtcNow();
            tx.Save(run);
            return new SessionInfo(session.Id, run.Id, player.Id, player.Sequence + 1);
        });
    }

    public AutomationRun View(Registration registration) => store.Execute(tx => tx.Run(registration.RunId));

    public void AddRule(Registration registration, string text)
    {
        string rule = text?.Trim() ?? "";
        if (rule.Length is < 1 or > 240 || rule.Any(char.IsControl))
            throw new InvalidDataException("Eine Regel braucht 1 bis 240 Zeichen in einer Zeile.");
        store.Execute(tx =>
        {
            var run = tx.Run(registration.RunId);
            if (!run.Players.Any(p => p.Id == registration.PlayerId && p.ClientId == registration.ClientId))
                throw new UnauthorizedAccessException("Der Spieler gehört nicht zu diesem Run.");
            if (run.Phase == RunPhase.Finished)
                throw new InvalidOperationException("Der Run ist abgeschlossen.");
            if (run.AdditionalRules.Contains(rule, StringComparer.OrdinalIgnoreCase)) return false;
            if (run.AdditionalRules.Count >= 24) throw new InvalidOperationException("Dieser Run hat bereits 24 Zusatzregeln.");
            run.AdditionalRules.Add(rule);
            tx.Save(run);
            return true;
        });
    }

    public void Disconnect(Registration registration, Guid sessionId)
    {
        if (this.sessions.TryGetValue(registration.PlayerId, out var session) && session.Id == sessionId)
            this.sessions.TryRemove(new KeyValuePair<Guid, LiveSession>(registration.PlayerId, session));
    }

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
                return new ObservationResult(false, "Reconnecting", "Synchronisiere die Verbindung automatisch.", player.Sequence + 1);
            if (!session.Ready && !observation.Baseline)
                return new ObservationResult(false, "Synchronizing", "A reconnect baseline is required.", player.Sequence + 1);
            if (!run.Selected || run.Phase is RunPhase.Paused or RunPhase.Finished)
                return new ObservationResult(false, "Paused", "Run is paused, inactive or finished.", player.Sequence + 1);

            var snapshot = observation.Snapshot;
            var previous = player.LastSnapshot;
            bool baseline = !session.Ready || observation.Baseline;
            if (!player.RebaselineAllowed && snapshot.TrainerId is uint trainer && player.TrainerId is uint registeredTrainer && trainer != registeredTrainer)
                return player.Connection == "OutOfSync" && player.Diagnostic == DifferentTrainer
                    ? new ObservationResult(false, "OutOfSync", DifferentTrainer, player.Sequence + 1)
                    : this.OutOfSync(tx, run, player, DifferentTrainer);
            if (!baseline && previous is not null && snapshot.Epoch == previous.Epoch && snapshot.Frame == previous.Frame)
                return new ObservationResult(false, "Ready", "Waiting for a fresh emulator frame.", player.Sequence + 1);
            bool sameTimeline = previous is not null && !player.RebaselineAllowed && !player.NeedsActiveBaseline && previous.Epoch == snapshot.Epoch &&
                snapshot.Frame >= previous.Frame && snapshot.PlaySeconds + 1 >= previous.PlaySeconds &&
                !snapshot.Pokemon.Any(mon => previous.Pokemon.Any(old => old.Identity == mon.Identity && old.Experience > mon.Experience));
            bool import = !sameTimeline || run.Phase == RunPhase.Lobby;
            if (!import) this.RecordWildResults(tx, run, player, snapshot, previous!.Frame);
            if (snapshot.Battle is { } battle)
            {
                bool hidden = previous?.Battle is { HideOpponent: true } oldBattle && oldBattle.Key == battle.Key ||
                    battle is { CatchEncounter: true, Trainer: null } && snapshot.Area < 255 &&
                    !player.FirstWildAreas.Contains(snapshot.Area) && !run.Encounters.Any(link => link.Area == snapshot.Area &&
                        (link.Dead || link.Failed || link.Members.ContainsKey(player.Id)));
                snapshot = snapshot with { Battle = battle with { HideOpponent = hidden } };
            }
            foreach (var tracked in player.Pokemon) tracked.Present = false;
            foreach (var state in snapshot.Pokemon)
            {
                var tracked = player.Pokemon.SingleOrDefault(p => p.State.Identity == state.Identity);
                if (tracked is null)
                {
                    // The verified Ruby bridge reports the interval from choosing the starter to receiving the Pokédex.
                    bool starter = player.Pokemon.Count == 0 && snapshot.RubyStarterWindow && snapshot.Pokemon.Count == 1 &&
                        state is { Container: Placement.Party, Slot: 0, MetArea: 16, IsEgg: false };
                    tracked = new TrackedPokemon { State = state, Imported = import || starter };
                    player.Pokemon.Add(tracked);
                    if (starter) this.RecordStarter(tx, run, player, tracked);
                    else if (!import && !state.IsEgg) this.Catch(tx, run, player, tracked, snapshot, previous!);
                }
                else if (!import && tracked.State.Species != state.Species)
                {
                    this.Event(tx, run, "evolved", $"{player.Name}: {RubyNames.Pokemon(tracked.State.Species)} → {RubyNames.Pokemon(state.Species)}.");
                }

                tracked.State = state;
                tracked.Present = true;
                this.ObserveFaint(tx, run, player, tracked, snapshot, previous, import || baseline);
            }

            player.LastSnapshot = snapshot;
            if (run.Phase == RunPhase.Active && snapshot.Position is { Valid: true } position &&
                (previous is null || previous.Position != position || previous.MapGroup != snapshot.MapGroup || previous.MapNumber != snapshot.MapNumber))
                tx.RecordPosition(run.Id, player.Id, observation.Sequence, this.clock.GetUtcNow(), snapshot, position);
            if (!import && previous?.BadgeMask is byte oldBadges && snapshot.BadgeMask is byte newBadges)
            {
                int acquired = System.Numerics.BitOperations.PopCount((uint)(newBadges & ~oldBadges));
                if (acquired > 0) this.Event(tx, run, "badge", $"{player.Name}: +{acquired} Orden.", notify: false);
            }
            player.Sequence = observation.Sequence;
            player.TrainerId = snapshot.TrainerId ?? player.TrainerId;
            if (player.NeedsActiveBaseline)
            {
                player.RunStartEpoch = snapshot.Epoch;
                player.RunStartFrame = snapshot.Frame;
                player.NeedsActiveBaseline = false;
            }
            player.Connection = "Ready";
            player.Diagnostic = import ? "Spielstand abgeglichen. Bisherige Fänge, Links und Tode bleiben erhalten." : "Observation accepted.";
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
            this.sessions.TryRemove(new KeyValuePair<Guid, LiveSession>(registration.PlayerId, session));
            throw new UnauthorizedAccessException("Session went offline; reconnect with a baseline.");
        }
        session.LastSeen = this.clock.GetUtcNow();
        if (bridgeAvailable.HasValue) session.BridgeAvailable = bridgeAvailable.Value;
        return session.Ready ? "Online" : "Synchronizing";
    }

    public CatchCheck CheckCatch(Registration registration, string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 80) throw new InvalidDataException("Ungültiger Pokémon-Name.");
        return CatchCheckRules.Check(this.View(registration), name);
    }

    public void ChangeArena(Registration registration, int number, bool complete)
    {
        if (number is < 1 or > 8) throw new InvalidDataException("Ungültige Arena.");
        store.Execute(tx =>
        {
            var run = tx.Run(registration.RunId);
            var player = run.Players.Single(p => p.Id == registration.PlayerId);
            if (run.Phase != RunPhase.Active) throw new InvalidOperationException("Der Run muss aktiv sein.");
            var gym = RubyDashboardCatalog.Gyms.Single(g => g.Number == number);
            int flag = 1 << (number - 1);
            byte progress = run.ArenaProgress;
            if (((progress & flag) != 0) == complete) return false;
            int? expected = complete ? RubyDashboardCatalog.NextGym(progress)?.Number
                : RubyDashboardCatalog.Gyms.LastOrDefault(g => (progress & (1 << (g.Number - 1))) != 0)?.Number;
            if (expected != number) throw new InvalidOperationException("Der Arena-Fortschritt hat sich geändert.");
            foreach (var participant in run.Players)
            {
                participant.ArenaConfirmed = (byte)(complete ? participant.ArenaConfirmed | flag : participant.ArenaConfirmed & ~flag);
                participant.ArenaReverted = (byte)(complete ? participant.ArenaReverted & ~flag : participant.ArenaReverted | flag);
            }
            this.Event(tx, run, complete ? "arena-completed" : "arena-reverted",
                $"{player.Name}: Arena {gym.Number} · {gym.Name} {(complete ? "abgeschlossen" : "zurückgenommen")}.",
                player: player, notify: false);
            tx.Save(run);
            return true;
        });
    }

    public void ResolveFaint(Registration registration, Guid pokemonId, Guid incidentId, bool confirm, string? reason)
    {
        reason = reason?.Trim();
        if (reason is { Length: > 250 } || reason?.Any(char.IsControl) == true)
            throw new InvalidDataException("Der Grund darf bis zu 250 Zeichen enthalten.");
        store.Execute(tx =>
        {
            var run = tx.Run(registration.RunId);
            var player = run.Players.Single(p => p.Id == registration.PlayerId);
            var pokemon = player.Pokemon.SingleOrDefault(p => p.Id == pokemonId);
            if (run.Phase != RunPhase.Active || pokemon is null)
                throw new InvalidOperationException("Der Todesfall ist in diesem Run nicht verfügbar.");
            if (pokemon.PendingDeath is not { } pending || pending.IncidentId != incidentId) return false;
            pokemon.PendingDeath = null;
            if (confirm) this.MarkDeath(tx, run, player, pokemon, reason, pending.Battle, pending.At);
            else pokemon.FaintDismissed = true;
            tx.Save(run);
            return true;
        });
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
            if (encounter.Failed) return false;
            this.FailEncounter(tx, run, encounter, $"{RubyNames.Area(area)}: Erstfang als verpasst bestätigt.");
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
            if (IsLiveLink(encounter)) run.PendingEnd = null;
            foreach (var member in encounter.Members)
            {
                var pokemon = run.Players.Single(p => p.Id == member.Key).Pokemon.Single(p => p.Id == member.Value);
                pokemon.Dead = dead;
                pokemon.ZeroHpSamples = 0;
                pokemon.PendingDeath = null;
                pokemon.FaintBattle = null;
                pokemon.FaintDismissed = true;
            }
            this.Event(tx, run, dead ? "death-confirmed" : "death-undone",
                $"{run.Name}: {encounter.Name} — link death {(dead ? "confirmed" : "undone")} by administrator. Game memory is unchanged.");
            tx.Save(run);
            return true;
        });
    }

    private const string DifferentTrainer = "Ein anderer Trainer ist geladen. Wähle den Spielstand dieses Runs.";

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

    private ObservationResult OutOfSync(AutomationStore.StoreTransaction tx, AutomationRun run, AutomationPlayer player, string reason)
    {
        player.Connection = "OutOfSync";
        player.Diagnostic = reason;
        tx.Save(run);
        return new ObservationResult(false, "OutOfSync", reason, player.Sequence + 1);
    }

    private void RecordWildResults(AutomationStore.StoreTransaction tx, AutomationRun run, AutomationPlayer player, GameSnapshot snapshot, long previousFrame)
    {
        long floor = player.RunStartEpoch == snapshot.Epoch ? player.RunStartFrame : -1;
        foreach (var battle in (snapshot.WildBattles ?? []).OrderBy(result => result.Ended))
        {
            if (battle.Ended <= previousFrame || battle.Started <= floor) continue;
            // Older bridges only expose the species in the live battle snapshot.
            var live = player.LastSnapshot?.Battle;
            int? species = battle.Species ?? (live?.Key == $"{snapshot.Epoch}:{battle.Started}"
                ? live.Combatants.FirstOrDefault(mon => (mon.Position & 1) != 0)?.Species : null);
            if (species is int foe && EncounterCatchChecks.FamilyProblem(CatchCheckRules.Check(run, foe, Guid.Empty)) is not null) continue;
            if (!player.FirstWildAreas.Add(battle.Area) || battle.Outcome == 7) continue;

            var encounter = run.Encounters.SingleOrDefault(link => link.Area == battle.Area);
            if (encounter is { Failed: true } or { Dead: true } or { GroupId: not null } ||
                encounter?.Members.ContainsKey(player.Id) == true) continue;
            encounter ??= new Encounter { Area = battle.Area };
            this.FailEncounter(tx, run, encounter, $"{player.Name}: Erstfang in {RubyNames.Area(battle.Area)} verpasst.", player);
        }
    }

    private void FailEncounter(AutomationStore.StoreTransaction tx, AutomationRun run, Encounter encounter, string description, AutomationPlayer? player = null)
    {
        if (!run.Encounters.Contains(encounter)) run.Encounters.Add(encounter);
        encounter.Failed = encounter.Dead = true;
        foreach (var member in encounter.Members)
        {
            var pokemon = run.Players.Single(p => p.Id == member.Key).Pokemon.Single(p => p.Id == member.Value);
            pokemon.Dead = true;
            pokemon.PendingDeath = null;
            pokemon.FaintBattle = null;
        }
        this.Event(tx, run, "encounter-failed", description, player: player, area: encounter.Area,
            details: new EventDetails(Members: DescribeLink(run, encounter).ToArray()));
    }

    private void Catch(AutomationStore.StoreTransaction tx, AutomationRun run, AutomationPlayer player, TrackedPokemon pokemon,
        GameSnapshot snapshot, GameSnapshot previous)
    {
        int area = pokemon.State.MetArea;
        var encounter = run.Encounters.SingleOrDefault(e => e.Area == area);
        string place = RubyNames.Area(area);
        int speciesId = pokemon.State.Species;
        string species = RubyNames.Pokemon(speciesId);
        bool areaAvailable = encounter is null || !encounter.Failed && !encounter.Dead && !encounter.Members.ContainsKey(player.Id);
        var check = CatchCheckRules.Check(run, speciesId, pokemon.Id);
        var details = new EventDetails(new EventPokemon(player.Name, speciesId, pokemon.State.IsShiny),
            CatchCheck: check, AreaAvailable: areaAvailable);
        bool observedWildCatch = (snapshot.WildBattles ?? []).Any(battle => battle.Area == area && battle.Outcome == 7 &&
            battle.Ended > previous.Frame && battle.Species == speciesId) ||
            (snapshot.Area == area && IsWildOpponent(snapshot.Battle, speciesId)) ||
            (previous.Area == area && IsWildOpponent(previous.Battle, speciesId));
        string? rejection = !areaAvailable ? "Das Gebiet war bereits vergeben."
            : observedWildCatch ? EncounterCatchChecks.FamilyProblem(check) : null;
        if (rejection is not null)
        {
            this.Event(tx, run, "extra-catch", $"{player.Name}: {species} in {place} gefangen. {rejection}",
                player: player, area: area, snapshot: snapshot, details: details);
            return;
        }

        encounter ??= new Encounter { Area = area, MapGroup = snapshot.MapGroup, MapNumber = snapshot.MapNumber };
        if (!run.Encounters.Contains(encounter)) run.Encounters.Add(encounter);
        encounter.Members[player.Id] = pokemon.Id;
        this.Event(tx, run, "caught", $"{player.Name}: {species} in {place} gefangen ({encounter.Members.Count}/{run.Players.Count}).",
            player: player, area: area, snapshot: snapshot, details: details);
        this.CompleteLink(tx, run, encounter);
    }

    private static bool IsWildOpponent(BattleStatus? battle, int species) =>
        battle is { CatchEncounter: true } && battle.Combatants.Any(mon => (mon.Position & 1) != 0 && mon.Species == species);

    private static bool IsLiveLink(Encounter link) => link.GroupId.HasValue && !link.Dead && !link.Failed;

    private static Encounter InsertStarterLink(AutomationRun run)
    {
        var starter = new Encounter { Area = Encounter.StarterArea };
        run.Encounters.Insert(0, starter);
        return starter;
    }

    private void RecordStarter(AutomationStore.StoreTransaction tx, AutomationRun run, AutomationPlayer player, TrackedPokemon pokemon)
    {
        var starter = run.Encounters.SingleOrDefault(link => link.Area == Encounter.StarterArea) ?? InsertStarterLink(run);
        starter.Members.Add(player.Id, pokemon.Id);
        pokemon.Dead = starter.Dead;
        if (run.Phase == RunPhase.Active) this.CompleteLink(tx, run, starter);
    }

    private void CompleteLink(AutomationStore.StoreTransaction tx, AutomationRun run, Encounter encounter)
    {
        if (encounter.GroupId is not null || encounter.Members.Count != run.Players.Count) return;
        encounter.GroupId = Guid.NewGuid();
        if (IsLiveLink(encounter)) run.PendingEnd = null;
        var members = DescribeLink(run, encounter).ToArray();
        string memberList = string.Join(" · ", members.Select(m => $"{m.Player}: {RubyNames.Pokemon(m.Species)}"));
        this.Event(tx, run, "linked", $"{RubyNames.Area(encounter.Area)} · {memberList}.", area: encounter.Area,
            details: new EventDetails(Members: members));
    }

    private void ObserveFaint(AutomationStore.StoreTransaction tx, AutomationRun run, AutomationPlayer player,
        TrackedPokemon tracked, GameSnapshot snapshot, GameSnapshot? previous, bool import)
    {
        var state = tracked.State;
        var trace = run.DeathRule == DeathRule.ConfirmFaint && state.Container == Placement.Party
            ? snapshot.Faints?.FirstOrDefault(f => f.PartySlot == state.Slot) : null;
        if (trace is not null && (snapshot.Battle?.HideOpponent == true ||
            snapshot.Battle is null && previous?.Battle?.HideOpponent == true ||
            tracked.PendingDeath?.Battle?.OpponentHidden == true || tracked.FaintBattle?.OpponentHidden == true && state.Hp == 0))
            trace = trace with { OpponentHidden = true };
        if (state.Hp > 0 && trace is null) tracked.FaintDismissed = false;
        if (tracked.Dead) return;
        if (tracked.PendingDeath is { } pending)
        {
            if (trace is not null && (pending.Battle is null || (pending.Battle.Move is null && trace.Move is not null) ||
                (trace.FinishingBlow && !pending.Battle.FinishingBlow)))
                tracked.PendingDeath = pending with { Battle = trace };
            return;
        }
        if (import || run.DeathRule == DeathRule.Disabled ||
            (state.Hp != 0 && !(snapshot.InBattle && trace is not null)) || state.IsEgg || tracked.FaintDismissed)
        {
            tracked.ZeroHpSamples = 0;
            tracked.FaintBattle = null;
            return;
        }
        if (trace is not null) tracked.FaintBattle = trace;
        if (run.DeathRule == DeathRule.ConfirmFaint)
        {
            bool previouslyHealthy = previous?.Pokemon.Any(p => p.Identity == state.Identity && p.Hp > 0) == true;
            if (snapshot.InBattle && (trace is not null || previouslyHealthy))
            {
                tracked.PendingDeath = new PendingDeath(tracked.Id, state.Species, tracked.FaintBattle, Guid.NewGuid(), this.clock.GetUtcNow());
                return;
            }
            if (snapshot.InBattle) return;
            if (previouslyHealthy || previous?.InBattle == true || tracked.FaintBattle is not null || tracked.ZeroHpSamples > 0)
            {
                if (++tracked.ZeroHpSamples >= 2)
                    tracked.PendingDeath = new PendingDeath(tracked.Id, state.Species, tracked.FaintBattle, Guid.NewGuid(), this.clock.GetUtcNow());
            }
            return;
        }
        if (!snapshot.InBattle && ++tracked.ZeroHpSamples >= 2) this.MarkDeath(tx, run, player, tracked);
        else if (snapshot.InBattle) tracked.ZeroHpSamples = 0;
    }

    private void MarkDeath(AutomationStore.StoreTransaction tx, AutomationRun run, AutomationPlayer player,
        TrackedPokemon pokemon, string? reason = null, BattleTrace? battle = null, DateTimeOffset? at = null)
    {
        pokemon.Dead = true;
        var links = run.Encounters.Where(e => e.Members.GetValueOrDefault(player.Id) == pokemon.Id).ToArray();
        bool lostLink = links.Any(IsLiveLink);
        foreach (var encounter in links)
        {
            encounter.Dead = true;
            foreach (var member in encounter.Members)
            {
                var linked = run.Players.Single(p => p.Id == member.Key).Pokemon.Single(p => p.Id == member.Value);
                linked.Dead = true;
                linked.PendingDeath = null;
            }
        }
        if (lostLink && !run.Encounters.Any(IsLiveLink))
            run.PendingEnd = new PendingRunEnd(Guid.NewGuid(), player.Id);

        if (battle is { OpponentHidden: true })
            battle = battle with { OpponentSpecies = null, Move = null, FinishingBlow = false };
        string detail = string.Join(" · ", new[] { reason, battle?.Trainer,
            battle?.OpponentSpecies is int species ? RubyNames.Pokemon(species) : null, RubyNames.Move(battle?.Move) }
            .Where(v => !string.IsNullOrEmpty(v)));
        var affected = links.SelectMany(link => DescribeLink(run, link, player.Id)).Distinct().ToArray();
        this.Event(tx, run, "death", $"{player.Name}: {RubyNames.Pokemon(pokemon.State.Species)} ist gestorben." +
            (detail.Length > 0 ? " " + detail : ""), player: player, area: links.FirstOrDefault()?.Area ?? pokemon.State.MetArea,
            details: new EventDetails(new EventPokemon(player.Name, pokemon.State.Species, pokemon.State.IsShiny),
                Members: affected, Battle: battle, Reason: reason, HasLink: links.Length > 0), at: at);
    }

    private static IEnumerable<EventPokemon> DescribeLink(AutomationRun run, Encounter link, Guid? except = null)
    {
        foreach (var player in run.Players.Where(p => p.Id != except && link.Members.ContainsKey(p.Id)))
        {
            var state = player.Pokemon.Single(m => m.Id == link.Members[player.Id]).State;
            yield return new EventPokemon(player.Name, state.Species, state.IsShiny);
        }
    }

    private void Event(AutomationStore.StoreTransaction tx, AutomationRun run, string kind, string message, bool notify = true,
        AutomationPlayer? player = null, int? area = null, GameSnapshot? snapshot = null, EventDetails? details = null,
        DateTimeOffset? at = null)
    {
        var where = snapshot?.Area == area ? snapshot : null;
        var item = new AutomationEvent(Guid.NewGuid(), at ?? this.clock.GetUtcNow(), kind, message, player?.Id, area,
            where?.MapGroup, where?.MapNumber, where?.Position, details);
        run.History.Add(item);
        if (kind is "caught" or "extra-catch" or "death") tx.RecordReplayEvent(run.Id, item);
        if (run.History.Count > 200) run.History.RemoveAt(0);
        if (notify && run.ChannelId != 0) tx.Notify(run, item);
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
