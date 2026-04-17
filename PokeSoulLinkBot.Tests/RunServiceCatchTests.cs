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

        var deadGroup = service.RegisterDeath(GuildId, "101");

        Assert.Same(linkGroup, deadGroup);
        Assert.All(deadGroup.Entries, entry =>
        {
            Assert.False(entry.IsAlive);
            Assert.NotNull(entry.DiedAtUtc);
        });
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
    public void UseRoute_ShouldRejectDeadRoute()
    {
        var service = CreateServiceWithStartedRun();
        service.RegisterCatch(GuildId, "101", 1, "marpie1", "Bisasam", Array.Empty<string>());
        service.RegisterDeath(GuildId, "101");

        var exception = Assert.Throws<InvalidOperationException>(() => service.UseRoute(GuildId, "101", 1));

        Assert.Equal("Route '101' is dead and cannot be used.", exception.Message);
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
