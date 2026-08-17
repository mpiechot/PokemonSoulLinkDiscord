using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Services;

/// <summary>
/// Provides the default implementation for Soul Link run management.
/// </summary>
public sealed class RunService : IRunService
{
    private const string DefaultRouteLossReason = "First encounter was not caught.";

    private readonly IRunStore runStore;
    private readonly object operationLock = new object();

    /// <summary>
    /// Initializes a new instance of the <see cref="RunService"/> class.
    /// </summary>
    /// <param name="runStore">The persistence store for Soul Link runs.</param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="runStore"/> is <see langword="null"/>.
    /// </exception>
    public RunService(IRunStore runStore)
    {
        this.runStore = runStore ?? throw new ArgumentNullException(nameof(runStore));
    }

    /// <inheritdoc />
    public SoulLinkRun StartRun(string guildId, string name, string edition, IReadOnlyList<RunPlayer> players)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(edition);
        ArgumentNullException.ThrowIfNull(players);

        if (players.Count == 0)
        {
            throw new ArgumentException("At least one player must be provided.", nameof(players));
        }

        lock (this.operationLock)
        {
            if (this.runStore.GetActiveRun(guildId) is not null)
            {
                throw new InvalidOperationException("An active run already exists for this guild.");
            }

            var run = new SoulLinkRun
            {
                Id = Guid.NewGuid(),
                GuildId = guildId,
                Name = name,
                Game = edition,
                StartedAtUtc = DateTime.UtcNow,
                Players = players.ToList(),
            };

            this.runStore.AddRun(run);

            return run;
        }
    }

    /// <inheritdoc />
    public SoulLinkRun EndRun(string guildId, string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);

        lock (this.operationLock)
        {
            SoulLinkRun activeRun = this.GetActiveRun(guildId);

            activeRun.EndedAtUtc = DateTime.UtcNow;
            activeRun.EndReason = string.IsNullOrWhiteSpace(reason)
                ? "No reason given."
                : reason;

            this.runStore.Save();

            return activeRun;
        }
    }

    /// <inheritdoc />
    public LinkGroup RegisterCatch(
        string guildId,
        string route,
        ulong playerId,
        string playerName,
        string pokemon,
        IReadOnlyList<string> pokemonTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemon);
        ArgumentNullException.ThrowIfNull(pokemonTypes);

        lock (this.operationLock)
        {
            SoulLinkRun activeRun = this.GetActiveRun(guildId);
            var normalizedRoute = this.NormalizeRoute(route);

            RunPlayer? runPlayer = activeRun.Players.FirstOrDefault(player => player.UserId == playerId);
            if (runPlayer is null)
            {
                throw new InvalidOperationException("The specified player is not part of the active run.");
            }

            LinkGroup? existingGroup = activeRun.LinkGroups.FirstOrDefault(group =>
                string.Equals(group.Route, normalizedRoute, StringComparison.OrdinalIgnoreCase));

            LinkGroup linkGroup = existingGroup ?? this.CreateLinkGroup(activeRun, normalizedRoute);

            bool playerAlreadyRegistered = linkGroup.Entries.Any(entry => entry.PlayerUserId == playerId);
            if (playerAlreadyRegistered)
            {
                throw new InvalidOperationException("The player already has a registered catch for this route.");
            }

            linkGroup.Entries.Add(new LinkedPokemon
            {
                PlayerUserId = playerId,
                PlayerName = playerName,
                PokemonName = pokemon,
                Types = pokemonTypes.ToList(),
                IsAlive = true,
                CaughtAtUtc = DateTime.UtcNow,
            });

            activeRun.TryAddToActive(linkGroup);
            this.runStore.Save();

            return linkGroup;
        }
    }

    /// <inheritdoc />
    public LinkGroup MarkRouteLost(
        string guildId,
        string route,
        string? reason,
        ulong? playerId,
        string? playerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        lock (this.operationLock)
        {
            SoulLinkRun activeRun = this.GetActiveRun(guildId);
            var normalizedRoute = this.NormalizeRoute(route);

            if (playerId.HasValue && activeRun.Players.All(player => player.UserId != playerId.Value))
            {
                throw new InvalidOperationException("The specified player is not part of the active run.");
            }

            LinkGroup? existingGroup = activeRun.LinkGroups.FirstOrDefault(group =>
                string.Equals(group.Route, normalizedRoute, StringComparison.OrdinalIgnoreCase));

            if (existingGroup?.Entries.Count > 0)
            {
                throw new InvalidOperationException("The route already has registered catches and must be marked dead with /death.");
            }

            if (existingGroup?.IsLostWithoutEncounter == true)
            {
                throw new InvalidOperationException("The route has already been marked as lost.");
            }

            LinkGroup linkGroup = existingGroup ?? this.CreateLinkGroup(activeRun, normalizedRoute);
            linkGroup.IsLostWithoutEncounter = true;
            linkGroup.LossReason = string.IsNullOrWhiteSpace(reason)
                ? DefaultRouteLossReason
                : reason.Trim();
            linkGroup.FailedEncounterPlayerUserId = playerId;
            linkGroup.FailedEncounterPlayerName = string.IsNullOrWhiteSpace(playerName)
                ? null
                : playerName.Trim();
            linkGroup.LostAtUtc = DateTime.UtcNow;

            this.RemoveFromActiveLinks(activeRun, linkGroup);
            this.runStore.Save();

            return linkGroup;
        }
    }

    /// <inheritdoc />
    public SoulLinkRun UseRoute(string guildId, string route, int position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        if (position < 1 || position > 6)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "Position must be between 1 and 6.");
        }

        lock (this.operationLock)
        {
            SoulLinkRun activeRun = this.GetActiveRun(guildId);
            LinkGroup linkGroup = this.GetAliveLinkGroup(activeRun, route);
            var targetIndex = position - 1;

            for (var index = 0; index < activeRun.ActiveLinks.Length; index++)
            {
                if (index != targetIndex && IsSameLinkGroup(activeRun.ActiveLinks[index], linkGroup))
                {
                    activeRun.ActiveLinks[index] = null;
                }
            }

            activeRun.ActiveLinks[targetIndex] = linkGroup;
            this.runStore.Save();

            return activeRun;
        }
    }

    /// <inheritdoc />
    public SoulLinkRun SwapRoute(string guildId, string teamRoute, string boxRoute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(teamRoute);
        ArgumentException.ThrowIfNullOrWhiteSpace(boxRoute);

        lock (this.operationLock)
        {
            SoulLinkRun activeRun = this.GetActiveRun(guildId);
            var normalizedTeamRoute = this.NormalizeRoute(teamRoute);
            LinkGroup boxLinkGroup = this.GetAliveLinkGroup(activeRun, boxRoute);

            var activeIndex = Array.FindIndex(
                activeRun.ActiveLinks,
                activeLink => activeLink != null &&
                    string.Equals(activeLink.Route, normalizedTeamRoute, StringComparison.OrdinalIgnoreCase));

            if (activeIndex < 0)
            {
                throw new InvalidOperationException($"Route '{normalizedTeamRoute}' is not in the current team.");
            }

            if (activeRun.ActiveLinks.Any(activeLink =>
                activeLink != null &&
                string.Equals(activeLink.Route, boxLinkGroup.Route, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Route '{boxLinkGroup.Route}' is already in the current team.");
            }

            activeRun.ActiveLinks[activeIndex] = boxLinkGroup;
            this.runStore.Save();

            return activeRun;
        }
    }

    /// <inheritdoc />
    public LinkGroup RegisterDeath(
        string guildId,
        string route,
        string reason,
        ulong? playerId,
        string? playerName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        lock (this.operationLock)
        {
            SoulLinkRun activeRun = this.GetActiveRun(guildId);
            var normalizedRoute = this.NormalizeRoute(route);

            if (playerId.HasValue && activeRun.Players.All(player => player.UserId != playerId.Value))
            {
                throw new InvalidOperationException("The specified player is not part of the active run.");
            }

            LinkGroup? linkGroup = activeRun.LinkGroups.FirstOrDefault(group =>
                group.Route.Equals(normalizedRoute, StringComparison.OrdinalIgnoreCase));

            if (linkGroup is null)
            {
                throw new InvalidOperationException("The specified Pokémon was not found in the active run.");
            }

            if (linkGroup.Entries.Count == 0 || linkGroup.Entries.All(entry => !entry.IsAlive))
            {
                throw new InvalidOperationException("The specified route has no living Pokémon to mark as dead.");
            }

            foreach (LinkedPokemon entry in linkGroup.Entries)
            {
                entry.IsAlive = false;
                entry.DiedAtUtc = DateTime.UtcNow;
                entry.DeathReason = reason.Trim();
                entry.DeathCausedByPlayerUserId = playerId;
                entry.DeathCausedByPlayerName = string.IsNullOrWhiteSpace(playerName)
                    ? null
                    : playerName.Trim();
            }

            this.RemoveFromActiveLinks(activeRun, linkGroup);
            this.runStore.Save();

            return linkGroup;
        }
    }

    /// <inheritdoc />
    public LinkGroup EditCatch(
        string guildId,
        string route,
        ulong playerId,
        string pokemon,
        IReadOnlyList<string> pokemonTypes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemon);
        ArgumentNullException.ThrowIfNull(pokemonTypes);

        lock (this.operationLock)
        {
            var activeRun = this.GetActiveRun(guildId);
            var linkGroup = this.GetLinkGroup(activeRun, route);
            var entry = linkGroup.Entries.FirstOrDefault(entry => entry.PlayerUserId == playerId)
                ?? throw new InvalidOperationException("The specified player has no catch on this route.");

            entry.PokemonName = pokemon.Trim();
            entry.Types = pokemonTypes.ToList();
            this.runStore.Save();
            return linkGroup;
        }
    }

    /// <inheritdoc />
    public LinkGroup RemoveCatch(string guildId, string route, ulong playerId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        lock (this.operationLock)
        {
            var activeRun = this.GetActiveRun(guildId);
            var linkGroup = this.GetLinkGroup(activeRun, route);
            var entry = linkGroup.Entries.FirstOrDefault(entry => entry.PlayerUserId == playerId)
                ?? throw new InvalidOperationException("The specified player has no catch on this route.");

            linkGroup.Entries.Remove(entry);
            if (linkGroup.Entries.Count == 0)
            {
                this.RemoveFromActiveLinks(activeRun, linkGroup);
                activeRun.LinkGroups.Remove(linkGroup);
            }

            this.runStore.Save();
            return linkGroup;
        }
    }

    /// <inheritdoc />
    public LinkGroup UndoDeath(string guildId, string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        lock (this.operationLock)
        {
            var activeRun = this.GetActiveRun(guildId);
            var linkGroup = this.GetLinkGroup(activeRun, route);

            if (linkGroup.IsLostWithoutEncounter)
            {
                linkGroup.IsLostWithoutEncounter = false;
                linkGroup.LossReason = null;
                linkGroup.FailedEncounterPlayerUserId = null;
                linkGroup.FailedEncounterPlayerName = null;
                linkGroup.LostAtUtc = null;
            }
            else if (linkGroup.Entries.Count == 0 || linkGroup.Entries.All(entry => entry.IsAlive))
            {
                throw new InvalidOperationException("The specified route has no registered death to undo.");
            }
            else
            {
                foreach (var entry in linkGroup.Entries)
                {
                    entry.IsAlive = true;
                    entry.DiedAtUtc = null;
                    entry.DeathReason = null;
                    entry.DeathCausedByPlayerUserId = null;
                    entry.DeathCausedByPlayerName = null;
                }

                activeRun.TryAddToActive(linkGroup);
            }

            this.runStore.Save();
            return linkGroup;
        }
    }

    /// <inheritdoc />
    public CompletedArena CompleteArena(
        string guildId,
        int arenaNumber,
        string edition,
        string leaderName,
        string location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(edition);
        ArgumentException.ThrowIfNullOrWhiteSpace(leaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(location);

        if (arenaNumber is < 1 or > 8)
        {
            throw new InvalidOperationException($"Arena '{arenaNumber}' ist ungültig. Bitte wähle eine Arena zwischen 1 und 8.");
        }

        lock (this.operationLock)
        {
            SoulLinkRun activeRun = this.GetActiveRun(guildId);
            var normalizedEdition = this.NormalizeEdition(edition);

            if (activeRun.CompletedArenas.Any(arena =>
                arena.ArenaNumber == arenaNumber &&
                string.Equals(this.NormalizeEdition(arena.Edition), normalizedEdition, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException($"Arena {arenaNumber} für '{edition.Trim()}' wurde bereits als erledigt markiert.");
            }

            var completedArena = new CompletedArena
            {
                ArenaNumber = arenaNumber,
                Edition = edition.Trim(),
                LeaderName = leaderName.Trim(),
                Location = location.Trim(),
                CompletedAtUtc = DateTime.UtcNow,
            };

            activeRun.CompletedArenas.Add(completedArena);
            this.runStore.Save();

            return completedArena;
        }
    }

    /// <inheritdoc />
    public SoulLinkRun GetActiveRun(string guildId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);

        lock (this.operationLock)
        {
            return this.runStore.GetActiveRun(guildId)
                ?? throw new InvalidOperationException("There is no active run for this guild.");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<SoulLinkRun> GetRuns(string guildId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);

        lock (this.operationLock)
        {
            return this.runStore.GetRunsForGuild(guildId);
        }
    }

    private static bool IsSameLinkGroup(LinkGroup? activeLink, LinkGroup linkGroup)
    {
        if (activeLink is null)
        {
            return false;
        }

        return ReferenceEquals(activeLink, linkGroup) ||
            (activeLink.Id != Guid.Empty && activeLink.Id == linkGroup.Id) ||
            string.Equals(activeLink.Route, linkGroup.Route, StringComparison.OrdinalIgnoreCase);
    }

    private LinkGroup CreateLinkGroup(SoulLinkRun run, string route)
    {
        var linkGroup = new LinkGroup
        {
            Id = Guid.NewGuid(),
            Route = route,
        };

        run.LinkGroups.Add(linkGroup);

        return linkGroup;
    }

    private void RemoveFromActiveLinks(SoulLinkRun run, LinkGroup linkGroup)
    {
        for (var index = 0; index < run.ActiveLinks.Length; index++)
        {
            LinkGroup? activeLink = run.ActiveLinks[index];
            if (activeLink != null &&
                string.Equals(activeLink.Route, linkGroup.Route, StringComparison.OrdinalIgnoreCase))
            {
                run.ActiveLinks[index] = null;
            }
        }
    }

    private LinkGroup GetLinkGroup(SoulLinkRun run, string route)
    {
        var normalizedRoute = this.NormalizeRoute(route);
        return run.LinkGroups.FirstOrDefault(group =>
                string.Equals(group.Route, normalizedRoute, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Route '{normalizedRoute}' was not found in the active run.");
    }

    private LinkGroup GetAliveLinkGroup(SoulLinkRun run, string route)
    {
        var normalizedRoute = this.NormalizeRoute(route);
        var linkGroup = run.LinkGroups.FirstOrDefault(group =>
            string.Equals(group.Route, normalizedRoute, StringComparison.OrdinalIgnoreCase));

        if (linkGroup is null)
        {
            throw new InvalidOperationException($"Route '{normalizedRoute}' was not found in the active run.");
        }

        if (!linkGroup.IsAlive)
        {
            throw new InvalidOperationException($"Route '{normalizedRoute}' is dead and cannot be used.");
        }

        return linkGroup;
    }

    private string NormalizeRoute(string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        return route.ToLowerInvariant().Trim();
    }

    private string NormalizeEdition(string edition)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(edition);

        return edition.ToLowerInvariant().Trim();
    }
}
