using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Services;

/// <summary>
/// Provides the default implementation for Soul Link run management.
/// </summary>
public sealed class RunService : IRunService
{
    private readonly IRunStore runStore;

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

        if (runStore.GetActiveRun(guildId) is not null)
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

        runStore.AddRun(run);

        return run;
    }

    /// <inheritdoc />
    public SoulLinkRun EndRun(string guildId, string? reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);

        SoulLinkRun activeRun = this.GetActiveRun(guildId);

        activeRun.EndedAtUtc = DateTime.UtcNow;
        activeRun.EndReason = string.IsNullOrWhiteSpace(reason)
            ? "No reason given."
            : reason;

        runStore.Save();

        return activeRun;
    }

    /// <inheritdoc />
    public LinkGroup RegisterCatch(string guildId, string route, ulong playerId, string playerName, string pokemon)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);
        ArgumentException.ThrowIfNullOrWhiteSpace(pokemon);

        SoulLinkRun activeRun = this.GetActiveRun(guildId);

        RunPlayer? runPlayer = activeRun.Players.FirstOrDefault(player => player.UserId == playerId);
        if (runPlayer is null)
        {
            throw new InvalidOperationException("The specified player is not part of the active run.");
        }

        LinkGroup? existingGroup = activeRun.LinkGroups.FirstOrDefault(group =>
            string.Equals(group.Route, route, StringComparison.OrdinalIgnoreCase));

        LinkGroup linkGroup = existingGroup ?? this.CreateLinkGroup(activeRun, route);

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
            IsAlive = true,
            CaughtAtUtc = DateTime.UtcNow,
        });

        runStore.Save();

        return linkGroup;
    }

    /// <inheritdoc />
    public LinkGroup RegisterDeath(string guildId, string route)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);

        SoulLinkRun activeRun = this.GetActiveRun(guildId);

        LinkGroup? linkGroup = activeRun.LinkGroups.FirstOrDefault(group =>
            group.Route.Equals(route, StringComparison.OrdinalIgnoreCase));

        if (linkGroup is null)
        {
            throw new InvalidOperationException("The specified Pokémon was not found in the active run.");
        }

        foreach (LinkedPokemon entry in linkGroup.Entries)
        {
            entry.IsAlive = false;
            entry.DiedAtUtc = DateTime.UtcNow;
        }

        runStore.Save();

        return linkGroup;
    }

    /// <inheritdoc />
    public SoulLinkRun GetActiveRun(string guildId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);

        return runStore.GetActiveRun(guildId)
            ?? throw new InvalidOperationException("There is no active run for this guild.");
    }

    /// <inheritdoc />
    public IReadOnlyList<SoulLinkRun> GetRuns(string guildId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guildId);

        return runStore.GetRunsForGuild(guildId);
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
}