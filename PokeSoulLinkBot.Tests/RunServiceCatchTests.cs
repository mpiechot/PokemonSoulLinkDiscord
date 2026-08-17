using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Core.Models;
using PokeSoulLinkBot.Infrastructure.Persistence;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class RunServiceCatchTests
{
    private const string GuildId = "guild-1";

    [Fact]
    public void StartRun_ShouldRejectEmptyPlayers()
    {
        var service = new RunService(new InMemoryRunStore());

        var exception = Assert.Throws<ArgumentException>(() =>
            service.StartRun(GuildId, "Ruby", "ruby", Array.Empty<RunPlayer>()));

        Assert.Equal("At least one player must be provided. (Parameter 'players')", exception.Message);
    }

    [Fact]
    public void StartRun_ShouldRejectDuplicateActiveRunForSameGuild()
    {
        var service = CreateServiceWithStartedRun();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.StartRun(GuildId, "Sapphire", "sapphire", CreatePlayers()));

        Assert.Equal("An active run already exists for this guild.", exception.Message);
    }

    [Fact]
    public void StartRun_ShouldAllowSeparateGuilds()
    {
        var service = CreateServiceWithStartedRun();

        var secondRun = service.StartRun("guild-2", "Sapphire", "sapphire", CreatePlayers());

        Assert.Equal("guild-2", secondRun.GuildId);
        Assert.Equal("Sapphire", secondRun.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void StartRun_ShouldRejectBlankRequiredText(string value)
    {
        var service = new RunService(new InMemoryRunStore());

        Assert.Throws<ArgumentException>(() => service.StartRun(value, "Ruby", "ruby", CreatePlayers()));
        Assert.Throws<ArgumentException>(() => service.StartRun(GuildId, value, "ruby", CreatePlayers()));
        Assert.Throws<ArgumentException>(() => service.StartRun(GuildId, "Ruby", value, CreatePlayers()));
    }

    [Fact]
    public void RegisterCatch_ShouldCreateLinkGroupWithPokemonTypes()
    {
        var store = new InMemoryRunStore();
        var service = new RunService(store);
        service.StartRun(GuildId, "Ruby", "ruby", CreatePlayers());

        var linkGroup = service.RegisterCatch(
            GuildId,
            "101",
            1,
            "marpie1",
            "Bisasam",
            new[] { "grass", "poison" });

        Assert.Equal("101", linkGroup.Route);
        var entry = Assert.Single(linkGroup.Entries);
        Assert.Equal(1UL, entry.PlayerUserId);
        Assert.Equal("Bisasam", entry.PokemonName);
        Assert.Equal(new[] { "grass", "poison" }, entry.Types);
        Assert.True(entry.IsAlive);
        Assert.True(store.SaveCount > 0);
    }

    [Fact]
    public void RegisterCatch_ShouldNormalizeRoute()
    {
        var service = CreateServiceWithStartedRun();

        var linkGroup = service.RegisterCatch(
            GuildId,
            "  Route 101  ",
            1,
            "marpie1",
            "Bisasam",
            Array.Empty<string>());

        Assert.Equal("route 101", linkGroup.Route);
    }

    [Fact]
    public void RegisterCatch_ShouldAddSecondPlayerToExistingRouteGroup()
    {
        var store = new InMemoryRunStore();
        var service = new RunService(store);
        service.StartRun(GuildId, "Ruby", "ruby", CreatePlayers());

        var firstGroup = service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", new[] { "grass" });
        var secondGroup = service.RegisterCatch(GuildId, "101", 2, "bene", "Pichu", new[] { "electric" });

        Assert.Same(firstGroup, secondGroup);
        Assert.Equal(2, secondGroup.Entries.Count);
        Assert.Collection(
            secondGroup.Entries,
            entry => Assert.Equal("Bisasam", entry.PokemonName),
            entry => Assert.Equal("Pichu", entry.PokemonName));
        Assert.Single(store.GetActiveRun(GuildId)!.LinkGroups);
    }

    [Fact]
    public void RegisterCatch_ShouldRejectDuplicatePlayerOnSameRoute()
    {
        var service = CreateServiceWithStartedRun();
        service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", Array.Empty<string>());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.RegisterCatch(GuildId, "101", 1, "marpie1", "Glumanda", Array.Empty<string>()));

        Assert.Equal("The player already has a registered catch for this route.", exception.Message);
    }

    [Fact]
    public void EditCatch_ShouldUpdatePokemonAndTypesForSelectedPlayer()
    {
        var service = CreateServiceWithStartedRun();
        service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", new[] { "grass" });

        var editedGroup = service.EditCatch(GuildId, "101", 1, "Bisaflor", new[] { "grass", "poison" });

        var entry = Assert.Single(editedGroup.Entries);
        Assert.Equal("Bisaflor", entry.PokemonName);
        Assert.Equal(new[] { "grass", "poison" }, entry.Types);
    }

    [Fact]
    public void RemoveCatch_ShouldDeleteEmptyRouteGroup()
    {
        var service = CreateServiceWithStartedRun();
        service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", Array.Empty<string>());

        service.RemoveCatch(GuildId, "101", 1);

        var activeRun = service.GetActiveRun(GuildId);
        Assert.Empty(activeRun.LinkGroups);
        Assert.DoesNotContain(activeRun.ActiveLinks, link => link?.Route == "101");
    }

    [Fact]
    public void UndoDeath_ShouldReviveLinkAndAllowItToBeUsedAgain()
    {
        var service = CreateServiceWithStartedRun();
        service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", Array.Empty<string>());
        service.RegisterDeath(GuildId, "101", "Critical hit.", null, null);

        var restoredGroup = service.UndoDeath(GuildId, "101");

        Assert.True(restoredGroup.IsAlive);
        Assert.All(restoredGroup.Entries, entry =>
        {
            Assert.True(entry.IsAlive);
            Assert.Null(entry.DiedAtUtc);
            Assert.Null(entry.DeathReason);
        });
        Assert.Same(restoredGroup, service.UseRoute(GuildId, "101", 1).ActiveLinks[0]);
    }

    [Fact]
    public void UndoDeath_ShouldReopenRouteWithoutCatch()
    {
        var service = CreateServiceWithStartedRun();
        service.MarkRouteLost(GuildId, "101", "Missed.", null, null);

        var restoredGroup = service.UndoDeath(GuildId, "101");

        Assert.False(restoredGroup.IsLostWithoutEncounter);
        Assert.Null(restoredGroup.LostAtUtc);
        Assert.Null(restoredGroup.LossReason);
        Assert.Empty(restoredGroup.Entries);
    }
    [Fact]
    public void RegisterCatch_ShouldRejectPlayerOutsideRun()
        {
            var service = CreateServiceWithStartedRun();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.RegisterCatch(GuildId, "101", 99, "outsider", "Bisasam", Array.Empty<string>()));

        Assert.Equal("The specified player is not part of the active run.", exception.Message);
    }

    [Fact]
    public void RegisterDeath_ShouldMarkAllPokemonInRouteGroupDead()
    {
        var service = CreateServiceWithStartedRun();
        var linkGroup = service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", Array.Empty<string>());
        service.RegisterCatch(GuildId, "101", 2, "bene", "Pichu", Array.Empty<string>());

        var deadGroup = service.RegisterDeath(GuildId, "101", "Critical hit.", 2, "bene");

        Assert.Same(linkGroup, deadGroup);
        Assert.All(deadGroup.Entries, entry =>
        {
            Assert.False(entry.IsAlive);
            Assert.NotNull(entry.DiedAtUtc);
            Assert.Equal("Critical hit.", entry.DeathReason);
            Assert.Equal(2UL, entry.DeathCausedByPlayerUserId);
            Assert.Equal("bene", entry.DeathCausedByPlayerName);
        });
    }

    [Fact]
    public void RegisterDeath_ShouldRejectPlayerOutsideRun()
    {
        var service = CreateServiceWithStartedRun();
        service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", Array.Empty<string>());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.RegisterDeath(GuildId, "101", "Critical hit.", 99, "outsider"));

        Assert.Equal("The specified player is not part of the active run.", exception.Message);
    }

    [Fact]
    public void MarkRouteLost_ShouldCreateDeadRouteWithoutPokemon()
    {
        var store = new InMemoryRunStore();
        var service = new RunService(store);
        service.StartRun(GuildId, "Ruby", "ruby", CreatePlayers());

        var linkGroup = service.MarkRouteLost(GuildId, "101", "Encounter fled.", 1, "marpie1");

        Assert.Equal("101", linkGroup.Route);
        Assert.Empty(linkGroup.Entries);
        Assert.False(linkGroup.IsAlive);
        Assert.True(linkGroup.IsLostWithoutEncounter);
        Assert.Equal("Encounter fled.", linkGroup.LossReason);
        Assert.Equal(1UL, linkGroup.FailedEncounterPlayerUserId);
        Assert.Equal("marpie1", linkGroup.FailedEncounterPlayerName);
        Assert.NotNull(linkGroup.LostAtUtc);
        Assert.True(store.SaveCount > 0);
    }

    [Fact]
    public void MarkRouteLost_ShouldUseDefaultReason()
    {
        var service = CreateServiceWithStartedRun();

        var linkGroup = service.MarkRouteLost(GuildId, "101", null, null, null);

        Assert.Equal("First encounter was not caught.", linkGroup.LossReason);
    }

    [Fact]
    public void MarkRouteLost_ShouldRejectPlayerOutsideRun()
    {
        var service = CreateServiceWithStartedRun();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.MarkRouteLost(GuildId, "101", "Encounter fled.", 99, "outsider"));

        Assert.Equal("The specified player is not part of the active run.", exception.Message);
    }

    [Fact]
    public void MarkRouteLost_ShouldRejectRouteWithRegisteredCatches()
    {
        var service = CreateServiceWithStartedRun();
        service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", Array.Empty<string>());

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.MarkRouteLost(GuildId, "101", "Encounter fled.", null, null));

        Assert.Equal("The route already has registered catches and must be marked dead with /death.", exception.Message);
    }

    [Fact]
    public void MarkRouteLost_ShouldRejectAlreadyLostRoute()
    {
        var service = CreateServiceWithStartedRun();
        service.MarkRouteLost(GuildId, "101", "Encounter fled.", null, null);

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.MarkRouteLost(GuildId, "101", "Encounter fled again.", null, null));

        Assert.Equal("The route has already been marked as lost.", exception.Message);
    }

    [Fact]
    public void TryAddToActive_ShouldAddGroupToFirstFreeTeamSlot()
    {
        var run = CreateRun();
        var route101 = new LinkGroup { Route = "101" };
        var route102 = new LinkGroup { Route = "102" };

        run.TryAddToActive(route101);
        run.TryAddToActive(route102);

        Assert.Same(route101, run.ActiveLinks[0]);
        Assert.Same(route102, run.ActiveLinks[1]);
    }

    [Fact]
    public void TryAddToActive_ShouldNotDuplicateExistingRouteGroupWhenTeamIsNotFull()
    {
        var run = CreateRun();
        var route101 = new LinkGroup { Route = "101" };

        run.TryAddToActive(route101);
        run.TryAddToActive(route101);

        Assert.Same(route101, run.ActiveLinks[0]);
        Assert.Null(run.ActiveLinks[1]);
    }

    [Fact]
    public void TryAddToActive_ShouldKeepExistingTeamWhenTeamIsFull()
    {
        var run = CreateRun();
        var activeGroups = Enumerable.Range(1, 6)
            .Select(index => new LinkGroup { Route = $"10{index}" })
            .ToArray();

        foreach (var group in activeGroups)
        {
            run.TryAddToActive(group);
        }

        run.TryAddToActive(new LinkGroup { Route = "107" });

        Assert.Equal(activeGroups, run.ActiveLinks);
    }

    [Fact]
    public void RegisterCatch_ShouldKeepNewRouteInBoxWhenTeamIsFull()
    {
        var service = CreateServiceWithStartedRun();
        for (var route = 101; route <= 107; route++)
        {
            service.RegisterCatch(GuildId, route.ToString(), 1, "marpie1", $"Pokemon {route}", Array.Empty<string>());
        }

        var activeRun = service.GetActiveRun(GuildId);

        Assert.Equal(6, activeRun.ActiveLinks.Count(linkGroup => linkGroup is not null));
        Assert.DoesNotContain(activeRun.ActiveLinks, linkGroup => linkGroup?.Route == "107");
        Assert.Contains(activeRun.LinkGroups, linkGroup => linkGroup.Route == "107");
    }

    [Fact]
    public void CatchFlow_ShouldAddFirstCatchToEmptyTeamInMemory()
    {
        var store = new InMemoryRunStore();
        var service = new RunService(store);
        service.StartRun(GuildId, "Ruby", "ruby", CreatePlayers());

        var linkGroup = service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", Array.Empty<string>());
        var activeRun = service.GetActiveRun(GuildId);
        activeRun.TryAddToActive(linkGroup);

        Assert.Same(linkGroup, activeRun.ActiveLinks[0]);
    }

    [Fact]
    public void CatchFlow_ShouldPersistActiveTeamAfterCatchAndReload()
    {
        var filePath = CreateTemporaryRunStorePath();
        try
        {
            var store = new RunStore(filePath);
            var service = new RunService(store);
            service.StartRun(GuildId, "Ruby", "ruby", CreatePlayers());

            var linkGroup = service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", Array.Empty<string>());
            var activeRun = service.GetActiveRun(GuildId);
            activeRun.TryAddToActive(linkGroup);

            var reloadedStore = new RunStore(filePath);
            var reloadedRun = reloadedStore.GetActiveRun(GuildId);

            Assert.NotNull(reloadedRun);
            Assert.Equal("101", reloadedRun.ActiveLinks[0]?.Route);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public void RunStoreSave_ShouldPersistActiveTeamAfterExplicitSaveAndReload()
    {
        var filePath = CreateTemporaryRunStorePath();
        try
        {
            var store = new RunStore(filePath);
            var service = new RunService(store);
            service.StartRun(GuildId, "Ruby", "ruby", CreatePlayers());

            var linkGroup = service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", Array.Empty<string>());
            var activeRun = service.GetActiveRun(GuildId);
            activeRun.TryAddToActive(linkGroup);
            store.Save();

            var reloadedStore = new RunStore(filePath);
            var reloadedRun = reloadedStore.GetActiveRun(GuildId);

            Assert.NotNull(reloadedRun);
            Assert.Equal("101", reloadedRun.ActiveLinks[0]?.Route);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    [Fact]
    public void UseRoute_ShouldSetRouteAtRequestedTeamPositionAndSave()
    {
        var store = new InMemoryRunStore();
        var service = new RunService(store);
        service.StartRun(GuildId, "Ruby", "ruby", CreatePlayers());
        service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", Array.Empty<string>());
        service.RegisterCatch(GuildId, "102", 1, "marpie1", "Pichu", Array.Empty<string>());

        var activeRun = service.UseRoute(GuildId, "102", 2);

        Assert.Equal("102", activeRun.ActiveLinks[1]?.Route);
        Assert.True(store.SaveCount >= 3);
    }

    [Fact]
    public void UseRoute_ShouldRemoveRouteFromOtherTeamPositions()
    {
        var service = CreateServiceWithStartedRun();
        service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", Array.Empty<string>());

        var activeRun = service.UseRoute(GuildId, "101", 2);

        Assert.Null(activeRun.ActiveLinks[0]);
        Assert.Equal("101", activeRun.ActiveLinks[1]?.Route);
    }

    [Fact]
    public void UseRoute_ShouldSupportSixthPositionWhenPersistedSlotsWereShorter()
    {
        var service = CreateServiceWithStartedRun();
        service.RegisterCatch(GuildId, "104", 1, "marpie1", "Kleinstein", Array.Empty<string>());
        var activeRun = service.GetActiveRun(GuildId);
        activeRun.ActiveLinks = activeRun.ActiveLinks.Take(5).ToArray();

        var updatedRun = service.UseRoute(GuildId, "104", 6);

        Assert.Equal(6, updatedRun.ActiveLinks.Length);
        Assert.Equal("104", updatedRun.ActiveLinks[5]?.Route);
    }

    [Fact]
    public void UseRoute_ShouldRejectZeroBasedPosition()
    {
        var service = CreateServiceWithStartedRun();

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() =>
            service.UseRoute(GuildId, "104", 0));

        Assert.Equal("position", exception.ParamName);
    }

    [Fact]
    public void UseRoute_ShouldRejectDeadRoute()
    {
        var service = CreateServiceWithStartedRun();
        service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", Array.Empty<string>());
        service.RegisterDeath(GuildId, "101", "Critical hit.", null, null);

        var exception = Assert.Throws<InvalidOperationException>(() => service.UseRoute(GuildId, "101", 1));

        Assert.Equal("Route '101' is dead and cannot be used.", exception.Message);
    }

    [Fact]
    public void RegisterDeath_ShouldRemoveRouteFromActiveTeam()
    {
        var service = CreateServiceWithStartedRun();
        service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", Array.Empty<string>());

        service.RegisterDeath(GuildId, "101", "Critical hit.", null, null);

        var activeRun = service.GetActiveRun(GuildId);
        Assert.DoesNotContain(activeRun.ActiveLinks, group => group?.Route == "101");
    }

    [Fact]
    public void SwapRoute_ShouldReplaceTeamRouteWithBoxRouteAndSave()
    {
        var store = new InMemoryRunStore();
        var service = new RunService(store);
        service.StartRun(GuildId, "Ruby", "ruby", CreatePlayers());
        for (var route = 101; route <= 107; route++)
        {
            service.RegisterCatch(GuildId, route.ToString(), 1, "marpie1", $"Pokemon {route}", Array.Empty<string>());
        }

        var activeRun = service.SwapRoute(GuildId, "101", "107");

        Assert.Equal("107", activeRun.ActiveLinks[0]?.Route);
        Assert.DoesNotContain(activeRun.ActiveLinks, group => group?.Route == "101");
        Assert.True(store.SaveCount >= 8);
    }

    [Fact]
    public void SwapRoute_ShouldRejectRouteThatIsNotInTeam()
    {
        var service = CreateServiceWithStartedRun();
        service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", Array.Empty<string>());
        service.RegisterCatch(GuildId, "102", 1, "marpie1", "Pichu", Array.Empty<string>());

        var exception = Assert.Throws<InvalidOperationException>(() => service.SwapRoute(GuildId, "103", "102"));

        Assert.Equal("Route '103' is not in the current team.", exception.Message);
    }

    [Fact]
    public void SwapRoute_ShouldRejectBoxRouteAlreadyInTeam()
    {
        var service = CreateServiceWithStartedRun();
        service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", Array.Empty<string>());
        service.RegisterCatch(GuildId, "102", 1, "marpie1", "Pichu", Array.Empty<string>());

        var exception = Assert.Throws<InvalidOperationException>(() => service.SwapRoute(GuildId, "101", "102"));

        Assert.Equal("Route '102' is already in the current team.", exception.Message);
    }

    [Fact]
    public void MarkRouteLost_ShouldTrimReasonAndPlayerName()
    {
        var service = CreateServiceWithStartedRun();

        var linkGroup = service.MarkRouteLost(GuildId, "101", "  Encounter fled.  ", 1, "  marpie1  ");

        Assert.Equal("Encounter fled.", linkGroup.LossReason);
        Assert.Equal("marpie1", linkGroup.FailedEncounterPlayerName);
    }

    [Fact]
    public void MarkRouteLost_ShouldRemoveRouteFromActiveTeam()
    {
        var service = CreateServiceWithStartedRun();
        var activeRun = service.GetActiveRun(GuildId);
        var linkGroup = new LinkGroup
        {
            Id = Guid.NewGuid(),
            Route = "101",
        };
        activeRun.LinkGroups.Add(linkGroup);
        activeRun.ActiveLinks[0] = linkGroup;

        service.MarkRouteLost(GuildId, "101", "Encounter fled.", null, null);

        Assert.Null(activeRun.ActiveLinks[0]);
    }

    [Fact]
    public void CompleteArena_ShouldPersistArenaProgress()
    {
        var store = new InMemoryRunStore();
        var service = new RunService(store);
        service.StartRun(GuildId, "Ruby", "ruby", CreatePlayers());

        var completedArena = service.CompleteArena(GuildId, 1, "ruby", "Roxanne", "Rustboro City");
        var activeRun = service.GetActiveRun(GuildId);

        Assert.Equal(1, completedArena.ArenaNumber);
        Assert.Equal("ruby", completedArena.Edition);
        Assert.Equal("Roxanne", completedArena.LeaderName);
        Assert.Equal("Rustboro City", completedArena.Location);
        Assert.NotNull(Assert.Single(activeRun.CompletedArenas));
        Assert.True(store.SaveCount > 0);
    }

    [Fact]
    public void CompleteArena_ShouldRejectDuplicateArenaForSameEdition()
    {
        var service = CreateServiceWithStartedRun();
        service.CompleteArena(GuildId, 1, "ruby", "Roxanne", "Rustboro City");

        var exception = Assert.Throws<InvalidOperationException>(() =>
            service.CompleteArena(GuildId, 1, "Ruby", "Roxanne", "Rustboro City"));

        Assert.Equal("Arena 1 für 'Ruby' wurde bereits als erledigt markiert.", exception.Message);
    }

    [Fact]
    public void CompleteArena_ShouldPersistAfterReload()
    {
        var filePath = CreateTemporaryRunStorePath();
        try
        {
            var store = new RunStore(filePath);
            var service = new RunService(store);
            service.StartRun(GuildId, "Ruby", "ruby", CreatePlayers());

            service.CompleteArena(GuildId, 1, "ruby", "Roxanne", "Rustboro City");

            var reloadedStore = new RunStore(filePath);
            var reloadedRun = reloadedStore.GetActiveRun(GuildId);

            Assert.NotNull(reloadedRun);
            var completedArena = Assert.Single(reloadedRun.CompletedArenas);
            Assert.Equal(1, completedArena.ArenaNumber);
            Assert.Equal("Roxanne", completedArena.LeaderName);
        }
        finally
        {
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }
        }
    }

    private static RunService CreateServiceWithStartedRun()
    {
        var service = new RunService(new InMemoryRunStore());
        service.StartRun(GuildId, "Ruby", "ruby", CreatePlayers());
        return service;
    }

    private static SoulLinkRun CreateRun()
    {
        return new SoulLinkRun
        {
            GuildId = GuildId,
            Name = "Ruby",
            Game = "ruby",
            StartedAtUtc = DateTime.UtcNow,
            Players = CreatePlayers().ToList(),
        };
    }

    private static IReadOnlyList<RunPlayer> CreatePlayers()
    {
        return new[]
        {
            new RunPlayer { UserId = 1, UserName = "marpie1" },
            new RunPlayer { UserId = 2, UserName = "bene" },
            new RunPlayer { UserId = 3, UserName = "darkstyle" },
        };
    }

    private static string CreateTemporaryRunStorePath()
    {
        return Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-runs.json");
    }

    private sealed class InMemoryRunStore : IRunStore
    {
        private readonly List<SoulLinkRun> runs = new();

        public int SaveCount { get; private set; }

        public SoulLinkRun? GetActiveRun(string guildId)
        {
            return this.runs.FirstOrDefault(run => run.GuildId == guildId && run.EndedAtUtc is null);
        }

        public IReadOnlyList<SoulLinkRun> GetRunsForGuild(string guildId)
        {
            return this.runs.Where(run => run.GuildId == guildId).ToList();
        }

        public void AddRun(SoulLinkRun run)
        {
            this.runs.Add(run);
        }

        public void Save()
        {
            this.SaveCount++;
        }
    }
}
