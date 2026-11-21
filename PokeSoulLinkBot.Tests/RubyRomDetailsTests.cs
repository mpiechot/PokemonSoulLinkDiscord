using System.Buffers.Binary;
using SoulLink;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class RubyRomDetailsTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void ReadsRandomizedMovesStatsAndEvolutionFromTheSelectedRom(byte revision)
    {
        using var rom = Cartridge(revision);
        var details = RubyRomDetails.Read(rom, 25);

        Assert.Equal(new PokemonStats(31, 87, 45, 60, 88, 52), details.BaseStats);
        Assert.Equal(new[] { new RubyLevelMove(1, 33), new RubyLevelMove(24, 53) }, details.Moves);
        var evolution = Assert.Single(details.Evolutions);
        Assert.Equal(37, evolution.Species);
        Assert.Equal("Mit DONNERSTEIN", evolution.Requirement);
    }

    [Fact]
    public void InvalidRomPointerIsReportedInsteadOfReadingPastTheFile()
    {
        using var rom = Cartridge(0);
        rom.Position = 0x214B98 + 25 * 4;
        rom.Write([0, 0, 0, 0]);
        Assert.Throws<InvalidDataException>(() => RubyRomDetails.Read(rom, 25));
    }

    private static MemoryStream Cartridge(byte revision)
    {
        const int moves = 0x219A00;
        var data = new byte[0x400000];
        "POKEMON RUBYAXVD"u8.CopyTo(data.AsSpan(0xA0));
        data[0xBC] = revision;
        new byte[] { 31, 87, 45, 60, 88, 52 }.CopyTo(data, 0x20BBE8 + 25 * 28);

        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0x214B98 + 25 * 4), 0x08000000 + moves);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(moves), (1 << 9) | 33);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(moves + 2), (24 << 9) | 53);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(moves + 4), ushort.MaxValue);

        var evolution = data.AsSpan(0x210B38 + 25 * 40);
        BinaryPrimitives.WriteUInt16LittleEndian(evolution, 7);
        BinaryPrimitives.WriteUInt16LittleEndian(evolution[2..], 96);
        BinaryPrimitives.WriteUInt16LittleEndian(evolution[4..], 37);
        byte[] stone = "DONNERSTEIN".Select(letter => (byte)(letter - 'A' + 0xBB)).Append((byte)255).ToArray();
        stone.CopyTo(data, 0x3D13DC + 96 * 44);
        return new MemoryStream(data);
    }
}
