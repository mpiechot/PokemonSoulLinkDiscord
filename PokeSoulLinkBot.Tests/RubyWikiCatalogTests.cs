using System.Buffers.Binary;
using SoulLink;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class RubyWikiCatalogTests
{
    [Fact]
    public void ReferenceCatalogHasAllRubySpeciesAndExplainsEveryMove()
    {
        var wiki = RubyWikiCatalog.Standard;
        Assert.Equal(Enumerable.Range(1, 386), wiki.Pokemon.Select(entry => entry.NationalDex));
        Assert.All(wiki.Pokemon, entry =>
        {
            Assert.True(entry.Details.IsValid());
            Assert.All(entry.Machines, machine => Assert.NotNull(wiki.Move(machine.Move)));
            Assert.All(entry.EggMoves, move => Assert.NotNull(wiki.Move(move)));
        });
        Assert.All(Enumerable.Range(1, 354), id =>
        {
            var move = Assert.IsType<RubyWikiMove>(wiki.Move(id));
            Assert.NotEmpty(move.Name);
            Assert.DoesNotContain("keine Beschreibung", move.Description);
        });
        Assert.Null(wiki.Move(355));
    }

    [Fact]
    public void SearchAndEvolutionUseNationalNumbersAndRubyConditions()
    {
        var wiki = RubyWikiCatalog.Standard;
        var treecko = Assert.Single(wiki.Search("TREECKO"));
        Assert.Equal(277, treecko.Species);
        Assert.Equal(treecko, Assert.Single(wiki.Search("#252")));
        Assert.Equal(treecko, Assert.Single(wiki.Search("Geckarbor")));
        Assert.Equal(83, Assert.Single(wiki.Search("Farfetch’d")).Species);
        Assert.Empty(wiki.Search("..."));

        Assert.Contains(new RubyWikiEvolution(277, 278, "Ab Level 16"), wiki.Evolutions(278));
        var pikachu = Assert.Single(wiki.Search("025"));
        Assert.Equal(172, wiki.Evolutions(pikachu.Species).First().From);
        Assert.Contains(new RubyWikiEvolution(25, 26, "Mit Donnerstein"), wiki.Evolutions(pikachu.Species));
        Assert.Contains(new RubyLevelMove(26, 85), pikachu.Details.Moves);
        Assert.Contains(new RubyMachineMove(24, 85, false), pikachu.Machines);
        Assert.Contains(new RubyMachineMove(4, 70, true), pikachu.Machines);
        Assert.Contains(242, treecko.EggMoves);

        var eevee = wiki.Evolutions(133).ToArray();
        Assert.Equal(5, eevee.Length);
        Assert.Contains(eevee, edge => edge.To == 196 && edge.Requirement.Contains("220 Freundschaft, 12:00–23:59"));
        Assert.Contains(eevee, edge => edge.To == 197 && edge.Requirement.Contains("220 Freundschaft, 00:00–11:59"));
    }

    [Fact]
    public void TooltipsDistinguishBasePowerStatusAndFixedDamage()
    {
        var wiki = RubyWikiCatalog.Standard;
        var flamethrower = wiki.Move(53)!;
        Assert.Equal(("Flammenwurf", "Feuer", "Speziell", "95", 100, 15),
            (flamethrower.Name, flamethrower.TypeName, flamethrower.Category, flamethrower.PowerText, flamethrower.Accuracy, flamethrower.Pp));
        Assert.Contains("verbrennen (10 %)", flamethrower.Description);

        var dragonRage = wiki.Move(82)!;
        Assert.Equal("40 KP", dragonRage.PowerText);
        Assert.Contains("40 KP", dragonRage.Description);
        Assert.Equal("Status", wiki.Move(92)!.Category);
        Assert.Contains("nimmt pro Runde zu", wiki.Move(92)!.Description);
        Assert.Contains("paralysieren (30 %)", wiki.Move(340)!.Description);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void UsesTheCartridgeTablesForRandomizedPokemonAndMoves(byte revision)
    {
        var data = Cartridge(revision);
        using var rom = new MemoryStream(data, writable: false);
        var wiki = RubyWikiCatalog.ReadCartridge(rom);
        var pikachu = wiki.Find(25)!;
        Assert.Equal(("fire", "dragon"), (pikachu.Type1, pikachu.Type2));
        Assert.Equal(99, pikachu.Details.BaseStats.Hp);
        Assert.Equal(new RubyLevelMove(19, 53), Assert.Single(pikachu.Details.Moves));
        Assert.Contains(new RubyWikiEvolution(25, 277, "Ab Level 18"), wiki.Evolutions(277));
        Assert.Equal(new RubyMachineMove(1, 53, false), pikachu.Machines[0]);
        Assert.Equal(new RubyMachineMove(8, 291, true), pikachu.Machines[1]);
        Assert.Equal(new[] { 85 }, pikachu.EggMoves);
        var move = wiki.Move(53)!;
        Assert.Equal(("Wasser", 60, 90, 12, 35, -1), (move.TypeName, move.Power, move.Accuracy, move.Pp, move.Chance, move.Priority));
        Assert.Contains("paralysieren (35 %)", move.Description);
        Assert.Equal(95, RubyWikiCatalog.Standard.Move(53)!.Power);
    }

    [Fact]
    public void InvalidCartridgeDataCannotReplaceTheReferenceCatalog()
    {
        byte[] data = Cartridge(0);
        data[0x2080FC + 53 * 12 + 2] = 250;
        using var rom = new MemoryStream(data, writable: false);
        Assert.Throws<InvalidDataException>(() => RubyWikiCatalog.ReadCartridge(rom));
        Assert.Equal("Feuer", RubyWikiCatalog.Standard.Move(53)!.TypeName);
    }

    private static byte[] Cartridge(byte revision)
    {
        const int learnset = 0x219A00;
        byte[] data = new byte[0x400000];
        "POKEMON RUBYAXVD"u8.CopyTo(data.AsSpan(0xA0));
        data[0xBC] = revision;
        foreach (var entry in RubyWikiCatalog.Standard.Pokemon)
        {
            byte[] stats = entry.Species == 25 ? [99, 87, 45, 60, 88, 52, 10, 16] : [25, 25, 25, 25, 25, 25, 0, 0];
            stats.CopyTo(data, 0x20BBE8 + entry.Species * 28);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x214B98 + entry.Species * 4), 0x08000000 + learnset);
        }

        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(learnset), (19 << 9) | 53);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(learnset + 2), ushort.MaxValue);
        var evolution = data.AsSpan(0x210B38 + 25 * 40);
        BinaryPrimitives.WriteUInt16LittleEndian(evolution, 4);
        BinaryPrimitives.WriteUInt16LittleEndian(evolution[2..], 18);
        BinaryPrimitives.WriteUInt16LittleEndian(evolution[4..], 277);

        for (int index = 0; index < 50; index++)
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x381CEC + index * 2), 53);
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(0x20A0C0 + 25 * 8), 1 | (1UL << 57));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x2161AC), 20025);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x2161AE), 85);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(0x2161B0), ushort.MaxValue);

        for (int id = 1; id <= 354; id++)
        {
            int offset = 0x2080FC + id * 12;
            byte[] move = id == 53 ? [(byte)RubyMoveEffect.ParalyzeHit, 60, 11, 90, 12, 35, 0, 255] : [0, 40, 0, 100, 35, 0, 0, 0];
            move.CopyTo(data, offset);
        }
        return data;
    }
}
