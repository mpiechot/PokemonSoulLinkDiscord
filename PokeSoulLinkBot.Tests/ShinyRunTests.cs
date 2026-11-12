using System.Text.Json;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Bot.Factories;
using PokeSoulLinkBot.Core.Models;
using PokeSoulLinkBot.Infrastructure.Persistence;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class ShinyRunTests
{
    private const string GuildId = "shiny-test-guild";

    [Fact]
    public void RegisterShiny_ShouldPreserveRouteEncountersAndAllowMultipleCatchesOfSameSpecies()
    {
        var service = CreateService();
        var group = Catch(service, "route 101");
        var original = Assert.Single(group.Entries);
        var run = service.GetActiveRun(GuildId);

        var first = Shiny(service, "  Route 101  ");
        var second = Shiny(service, "route 101");

        Assert.Equal("route 101", first.ShinyCaughtRoute);
        Assert.NotEqual(first.ShinyId, second.ShinyId);
        Assert.True(first.IsAlive);
        Assert.Equal(new[] { "electric" }, first.Types);
        Assert.Single(run.LinkGroups);
        Assert.Same(original, Assert.Single(group.Entries));
        Assert.Equal(2, run.UnassignedShinies.Count);
        Assert.All(run.GetShinyAssignments(), item => Assert.Null(item.Route));

        Shiny(service, "route 102");
        Assert.DoesNotContain(run.LinkGroups, item => item.Route == "route 102");
        Assert.NotNull(Catch(service, "route 102"));
        var lost = service.MarkRouteLost(GuildId, "lost", null, null, null);
        Shiny(service, "lost");
        Assert.True(lost.IsLostWithoutEncounter);
        Assert.Empty(lost.Entries);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void RegisterShiny_ShouldAllowMissingCatchLocationWithoutConfusingLinkAssignment(string? caughtRoute)
    {
        var service = CreateService();
        var shiny = Shiny(service, caughtRoute);
        var run = service.GetActiveRun(GuildId);
        var factory = new EmbedFactory();

        Assert.Null(shiny.ShinyCaughtRoute);
        Assert.Empty(run.LinkGroups);
        Assert.Null(Assert.Single(run.GetShinyAssignments()).Route);
        Assert.Contains("Fund: nicht angegeben | Einsatz: keine Route", factory.CreateStatusMessage(run));

        var group = Catch(service, "route 101");
        var original = Assert.Single(group.Entries);
        service.SwapShiny(GuildId, 1, shiny.ShinyId!.Value, group.Route);

        Assert.Null(shiny.ShinyCaughtRoute);
        Assert.Equal("route 101", Assert.Single(run.GetShinyAssignments()).Route);
        Assert.Contains("Fund: nicht angegeben | Einsatz: route 101", factory.CreateStatusMessage(run));

        service.SwapShiny(GuildId, 1, shiny.ShinyId.Value, null);
        Assert.Same(original, Assert.Single(group.Entries));
        Assert.Null(Assert.Single(run.GetShinyAssignments()).Route);
        Assert.Null(shiny.ShinyCaughtRoute);
    }

    [Fact]
    public void SwapShiny_ShouldPreserveOriginalPartnerAndTeamSlotAndRestoreOriginal()
    {
        var service = CreateService();
        var group = Catch(service, "route 101");
        var original = Assert.Single(group.Entries);
        service.RegisterCatch(GuildId, "route 101", 2, "Partner", "Squirtle", new[] { "water" });
        var partner = group.Entries[1];
        var shiny = Shiny(service);

        var run = service.SwapShiny(GuildId, 1, shiny.ShinyId!.Value, "  Route 101  ");

        Assert.Same(shiny, group.Entries[0]);
        Assert.Same(original, shiny.ReplacedPokemon);
        Assert.Same(partner, group.Entries[1]);
        Assert.Same(group, run.ActiveLinks[0]);
        Assert.Empty(run.UnassignedShinies);
        Assert.Equal("route 101", Assert.Single(run.GetShinyAssignments()).Route);
        Assert.Equal("forest", shiny.ShinyCaughtRoute);

        service.SwapShiny(GuildId, 1, shiny.ShinyId.Value, null);

        Assert.Same(original, group.Entries[0]);
        Assert.Same(shiny, Assert.Single(run.UnassignedShinies));
        Assert.Null(shiny.ReplacedPokemon);
        Assert.Same(partner, group.Entries[1]);
        Assert.Same(group, run.ActiveLinks[0]);
    }

    [Fact]
    public void SwapShiny_ShouldMoveBetweenTeamAndBoxAndExchangeTwoShiniesWithoutLosingOriginals()
    {
        var service = CreateService();
        var firstGroup = Catch(service, "route 101");
        var firstOriginal = Assert.Single(firstGroup.Entries);
        for (var route = 102; route <= 107; route++)
        {
            Catch(service, $"route {route}");
        }

        var run = service.GetActiveRun(GuildId);
        var boxGroup = run.LinkGroups.Last();
        var boxOriginal = Assert.Single(boxGroup.Entries);
        var firstShiny = Shiny(service);
        var secondShiny = Shiny(service);

        service.SwapShiny(GuildId, 1, firstShiny.ShinyId!.Value, firstGroup.Route);
        service.SwapShiny(GuildId, 1, secondShiny.ShinyId!.Value, boxGroup.Route);
        service.SwapShiny(GuildId, 1, firstShiny.ShinyId.Value, boxGroup.Route);

        Assert.Same(firstOriginal, Assert.Single(firstGroup.Entries));
        Assert.Same(firstShiny, Assert.Single(boxGroup.Entries));
        Assert.Same(boxOriginal, firstShiny.ReplacedPokemon);
        Assert.Same(secondShiny, Assert.Single(run.UnassignedShinies));
        Assert.Null(secondShiny.ReplacedPokemon);
        Assert.DoesNotContain(boxGroup, run.ActiveLinks);
        Assert.Equal(2, run.GetShinyAssignments().Count());

        service.SwapShiny(GuildId, 1, firstShiny.ShinyId.Value, null);
        Assert.Same(boxOriginal, Assert.Single(boxGroup.Entries));
        Assert.Equal(2, run.UnassignedShinies.Count);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("dead")]
    [InlineData("lost")]
    [InlineData("no-player-catch")]
    [InlineData("foreign-owner")]
    [InlineData("unknown-shiny")]
    [InlineData("same-route")]
    [InlineData("blank-route")]
    public void SwapShiny_ShouldRejectInvalidTargetsWithoutChangingRun(string scenario)
    {
        var service = CreateService();
        Catch(service, "source");
        var target = service.RegisterCatch(GuildId, "target", 2, "Partner", "Squirtle", new[] { "water" });
        if (scenario != "no-player-catch")
        {
            Catch(service, "target");
        }

        var shiny = Shiny(service);
        service.SwapShiny(GuildId, 1, shiny.ShinyId!.Value, "source");
        if (scenario == "dead")
        {
            service.RegisterDeath(GuildId, "target", "Test death", 2, "Partner");
        }

        if (scenario == "lost")
        {
            service.MarkRouteLost(GuildId, "lost", null, null, null);
        }

        var run = service.GetActiveRun(GuildId);
        var before = JsonSerializer.Serialize(run);
        var route = scenario switch
        {
            "missing" => "missing",
            "lost" => "lost",
            "same-route" => "source",
            "blank-route" => "  ",
            _ => target.Route,
        };
        var playerId = scenario == "foreign-owner" ? 2UL : 1UL;
        var shinyId = scenario == "unknown-shiny" ? Guid.NewGuid() : shiny.ShinyId.Value;

        if (scenario == "blank-route")
        {
            Assert.Throws<ArgumentException>(() => service.SwapShiny(GuildId, playerId, shinyId, route));
        }
        else
        {
            Assert.Throws<InvalidOperationException>(() => service.SwapShiny(GuildId, playerId, shinyId, route));
        }

        Assert.Equal(before, JsonSerializer.Serialize(run));
    }

    [Fact]
    public void ShinyCommands_ShouldRejectMissingRunNonParticipantAndOtherRunShinies()
    {
        var service = new RunService(new MemoryStore());
        Assert.Throws<InvalidOperationException>(() => Shiny(service));
        service = CreateService();
        Assert.Throws<InvalidOperationException>(() => service.RegisterShiny(GuildId, "forest", 99, "Outsider", "Pikachu", new[] { "electric" }));
        var shiny = Shiny(service);
        Assert.Throws<InvalidOperationException>(() => service.SwapShiny(GuildId, 99, shiny.ShinyId!.Value, null));
        Assert.Throws<InvalidOperationException>(() => service.SwapShiny(GuildId, 1, shiny.ShinyId!.Value, null));
        service.EndRun(GuildId, "Test");
        service.StartRun(GuildId, "Next", "ruby", new[] { new RunPlayer { UserId = 1, UserName = "Player" } });
        Catch(service, "route 101");
        Assert.Throws<InvalidOperationException>(() => service.SwapShiny(GuildId, 1, shiny.ShinyId!.Value, "route 101"));
    }

    [Fact]
    public void DeathAndUndo_ShouldAffectDeployedShinyAndPartnersAndPreventEscapingDeadLink()
    {
        var service = CreateService();
        var group = Catch(service, "route 101");
        var original = Assert.Single(group.Entries);
        service.RegisterCatch(GuildId, group.Route, 2, "Partner", "Squirtle", new[] { "water" });
        var shiny = Shiny(service);
        var reserve = Shiny(service);
        service.SwapShiny(GuildId, 1, shiny.ShinyId!.Value, group.Route);

        service.RegisterDeath(GuildId, group.Route, "Critical hit", 1, "Player");

        Assert.All(group.Entries, entry => Assert.False(entry.IsAlive));
        Assert.True(original.IsAlive);
        Assert.True(reserve.IsAlive);
        Assert.Equal("Critical hit", shiny.DeathReason);
        Assert.Throws<InvalidOperationException>(() => service.SwapShiny(GuildId, 1, shiny.ShinyId.Value, null));
        Assert.Throws<InvalidOperationException>(() => service.SwapShiny(GuildId, 1, reserve.ShinyId!.Value, group.Route));
        Assert.Throws<InvalidOperationException>(() => service.RemoveCatch(GuildId, group.Route, 1));

        service.UndoDeath(GuildId, group.Route);
        service.SwapShiny(GuildId, 1, shiny.ShinyId.Value, null);
        Assert.True(shiny.IsAlive);
        Assert.Null(shiny.DeathReason);
        Assert.Same(original, group.Entries[0]);
    }

    [Fact]
    public void EditCatch_ShouldRetainShinyIdentityAndOriginalAndRequireReturnBeforeRemoval()
    {
        var service = CreateService();
        var group = Catch(service, "route 101");
        var original = Assert.Single(group.Entries);
        var shiny = Shiny(service);
        service.SwapShiny(GuildId, 1, shiny.ShinyId!.Value, group.Route);

        service.EditCatch(GuildId, group.Route, 1, "Raichu", new[] { "electric" });

        Assert.Same(shiny, Assert.Single(group.Entries));
        Assert.Equal("Raichu", shiny.PokemonName);
        Assert.Same(original, shiny.ReplacedPokemon);
        Assert.Throws<InvalidOperationException>(() => service.RemoveCatch(GuildId, group.Route, 1));
        service.SwapShiny(GuildId, 1, shiny.ShinyId.Value, null);
        service.RemoveCatch(GuildId, group.Route, 1);
        Assert.Same(shiny, Assert.Single(service.GetActiveRun(GuildId).UnassignedShinies));
    }

    [Fact]
    public void Reload_ShouldPreserveShinyOriginalAndTeamReferencesAcrossSwapsAndDeath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shiny-{Guid.NewGuid():N}.json");
        try
        {
            var service = CreateService(new RunStore(path));
            Catch(service, "route 101");
            var shiny = Shiny(service, null);
            var reserve = Shiny(service);
            service.SwapShiny(GuildId, 1, shiny.ShinyId!.Value, "route 101");

            service = new RunService(new RunStore(path));
            var run = service.GetActiveRun(GuildId);
            var group = Assert.Single(run.LinkGroups);
            Assert.Same(group, run.ActiveLinks[0]);
            Assert.Equal(shiny.ShinyId, Assert.Single(group.Entries).ShinyId);
            Assert.Null(Assert.Single(group.Entries).ShinyCaughtRoute);
            Assert.Equal(reserve.ShinyId, Assert.Single(run.UnassignedShinies).ShinyId);

            service.SwapShiny(GuildId, 1, shiny.ShinyId.Value, null);
            Assert.Equal("Bulbasaur", Assert.Single(run.ActiveLinks[0]!.Entries).PokemonName);
            service = new RunService(new RunStore(path));
            service.SwapShiny(GuildId, 1, shiny.ShinyId.Value, "route 101");
            service.RegisterDeath(GuildId, "route 101", "Test", 1, "Player");
            service = new RunService(new RunStore(path));
            run = service.GetActiveRun(GuildId);
            Assert.False(run.GetShinyAssignments().Single(item => item.Pokemon.ShinyId == shiny.ShinyId).Pokemon.IsAlive);
            service.UndoDeath(GuildId, "route 101");
            service.SwapShiny(GuildId, 1, shiny.ShinyId.Value, null);
            Assert.Equal("Bulbasaur", Assert.Single(run.LinkGroups[0].Entries).PokemonName);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".bak");
        }
    }

    [Fact]
    public void OldJson_ShouldLoadWithEmptyShinyPool()
    {
        var path = Path.Combine(Path.GetTempPath(), $"shiny-legacy-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, "[{\"GuildId\":\"shiny-test-guild\",\"Name\":\"Legacy\",\"LinkGroups\":[]}]");
            var run = new RunStore(path).GetActiveRun(GuildId);
            Assert.NotNull(run);
            Assert.Empty(run.UnassignedShinies);
            Assert.Empty(run.GetShinyAssignments());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Status_ShouldListFreeDeployedAndDeadShiniesWithOwnersAndBothRoutes()
    {
        var service = CreateService();
        Catch(service, "route 101");
        Catch(service, "route 102");
        var deployed = Shiny(service, "forest");
        var dead = Shiny(service, "cave");
        var free = Shiny(service, "lake");
        service.SwapShiny(GuildId, 1, deployed.ShinyId!.Value, "route 101");
        service.SwapShiny(GuildId, 1, dead.ShinyId!.Value, "route 102");
        service.RegisterDeath(GuildId, "route 102", "Test", 1, "Player");

        var status = new EmbedFactory().CreateStatusMessage(service.GetActiveRun(GuildId));

        Assert.Contains("✨ Shinies", status);
        Assert.Contains("Player · eingesetzt", status);
        Assert.Contains("Player · tot", status);
        Assert.Contains("Player · frei", status);
        Assert.Contains("Fund: forest | Einsatz: route 101", status);
        Assert.Contains("Fund: cave | Einsatz: route 102", status);
        Assert.Contains("Fund: lake | Einsatz: keine Route", status);
        Assert.Contains(free.ShinyId!.Value.ToString(), status);
    }

    [Fact]
    public void Status_ShouldPageLargeShinyPoolsWithoutDroppingIdsOrExceedingDiscordLimits()
    {
        var service = CreateService();
        for (var index = 0; index < 80; index++)
        {
            Shiny(service, new string('r', 200));
        }

        var run = service.GetActiveRun(GuildId);
        var messages = new EmbedFactory().CreateStatusMessages(run);
        var all = string.Join(Environment.NewLine, messages);
        Assert.All(messages, message => Assert.InRange(message.Length, 1, 2000));
        Assert.All(run.GetShinyAssignments(), item => Assert.Contains(item.Pokemon.ShinyId!.Value.ToString(), all));
    }

    private static RunService CreateService(IRunStore? store = null)
    {
        var service = new RunService(store ?? new MemoryStore());
        service.StartRun(GuildId, "Shiny test", "ruby", new[]
        {
            new RunPlayer { UserId = 1, UserName = "Player" },
            new RunPlayer { UserId = 2, UserName = "Partner" },
        });
        return service;
    }

    private static LinkGroup Catch(RunService service, string route)
        => service.RegisterCatch(GuildId, route, 1, "Player", "Bulbasaur", new[] { "grass", "poison" });

    private static LinkedPokemon Shiny(RunService service, string? route = "forest")
        => service.RegisterShiny(GuildId, route, 1, "Player", "Pikachu", new[] { "electric" });

    private sealed class MemoryStore : IRunStore
    {
        private readonly List<SoulLinkRun> runs = new();

        public SoulLinkRun? GetActiveRun(string guildId) => this.runs.LastOrDefault(run => run.GuildId == guildId && run.EndedAtUtc is null);

        public IReadOnlyList<SoulLinkRun> GetRunsForGuild(string guildId) => this.runs.Where(run => run.GuildId == guildId).ToList();

        public void AddRun(SoulLinkRun run) => this.runs.Add(run);

        public void Save()
        {
        }
    }
}
