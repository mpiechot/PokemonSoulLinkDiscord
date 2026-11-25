using SoulLink;
using SoulLink.Demo;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class Gen3ParserTests
{
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)]
    [InlineData(6)] [InlineData(7)] [InlineData(8)] [InlineData(9)] [InlineData(10)] [InlineData(11)]
    [InlineData(12)] [InlineData(13)] [InlineData(14)] [InlineData(15)] [InlineData(16)] [InlineData(17)]
    [InlineData(18)] [InlineData(19)] [InlineData(20)] [InlineData(21)] [InlineData(22)] [InlineData(23)]
    public void AllSubstructurePermutationsParseAndKeepIdentityAcrossContainers(int pid)
    {
        var party = Gen3Parser.Parse(SyntheticPokemon.Bytes((uint)pid, 280), Placement.Party, 0)!;
        var box = Gen3Parser.Parse(SyntheticPokemon.Bytes((uint)pid, 281, box: true), Placement.Box, 0, 0)!;
        Assert.Equal($"12345678:{pid:X8}", party.Identity);
        Assert.Equal(party.Identity, box.Identity);
        Assert.Equal(280, party.Species);
        Assert.Equal(281, box.Species);
        Assert.Equal(125u, party.Experience);
        Assert.Equal(16, party.MetArea);
        Assert.Equal(20, party.Hp);
        Assert.Null(box.Hp);
        Assert.Equal("torchic", party.DisplayName);
    }

    [Fact]
    public void InvalidChecksumAndInvalidLengthAreRejected()
    {
        byte[] raw = SyntheticPokemon.Bytes(7, 280);
        raw[40] ^= 0x20;
        Assert.Throws<InvalidDataException>(() => Gen3Parser.Parse(raw, Placement.Party, 0));
        Assert.Throws<InvalidDataException>(() => Gen3Parser.Parse(raw[..80], Placement.Party, 0));
        Assert.Null(Gen3Parser.Parse(new byte[80], Placement.Box, 0, 0));
    }

    [Fact]
    public void EggsAndGermanNicknamesAreRead()
    {
        byte[] raw = SyntheticPokemon.Bytes(7, 280, egg: true);
        raw[8] = 0xF1; raw[9] = 0xF4; raw[10] = 0xFF;
        var pokemon = Gen3Parser.Parse(raw, Placement.Party, 0)!;
        Assert.True(pokemon.IsEgg);
        Assert.Equal("Ää", pokemon.Nickname);
    }

    [Fact]
    public async Task JsonLinesAcceptsFragmentedAndCombinedMessages()
    {
        using var stream = new FragmentedStream(System.Text.Encoding.UTF8.GetBytes("{\"one\":1}\n{\"two\":2}\r\n"));
        var lines = new List<string>();
        await foreach (string line in JsonLines.ReadAsync(stream)) lines.Add(line);
        Assert.Equal(new[] { "{\"one\":1}", "{\"two\":2}" }, lines);
    }

    [Fact]
    public async Task JsonLinesRejectsUnterminatedAndOversizedFrames()
    {
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            using var stream = new MemoryStream(new byte[Protocol.MaxMessageBytes + 1]);
            await foreach (string line in JsonLines.ReadAsync(stream)) { }
        });
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
        {
            using var stream = new MemoryStream("{}"u8.ToArray());
            await foreach (string line in JsonLines.ReadAsync(stream)) { }
        });
    }

    [Fact]
    public void PartialBridgeInventoryIsNeverAccepted()
    {
        var frame = SyntheticPokemon.Frame(1, SyntheticPokemon.Raw(3, 280));
        Assert.Single(frame.Parse().Pokemon);
        Assert.Throws<InvalidDataException>(() => (frame with { CompleteInventory = false }).Parse());
    }

    [Fact]
    public void MapCatalogNormalizesRoutesAndInteriorSections()
    {
        Assert.Equal(16, MapCatalog.Section(0, 16));
        Assert.Equal("Route 101", AreaCatalog.Name(MapCatalog.Section(0, 16)));
        Assert.Equal(255, MapCatalog.Section(255, 255));
        Assert.Equal("ralts", SpeciesCatalog.Name(392));
    }

    [Fact]
    public void FullFourteenBoxInventoryFitsBothTransportLimits()
    {
        var raw = new List<RawPokemon>();
        for (int slot = 0; slot < 6; slot++) raw.Add(SyntheticPokemon.Raw((uint)slot, 392, slot));
        for (int box = 0; box < 14; box++)
            for (int slot = 0; slot < 30; slot++)
                raw.Add(SyntheticPokemon.Raw((uint)(100 + box * 30 + slot), 392, slot, box: true) with { Box = box });
        var frame = SyntheticPokemon.Frame(1, raw.ToArray());
        Assert.Equal(426, frame.Parse().Pokemon.Count);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(Protocol.Serialize(frame)) < Protocol.MaxMessageBytes);
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(Protocol.Serialize(new Observation(Guid.NewGuid(), Guid.NewGuid(), 1, true, frame.Parse()))) < Protocol.MaxMessageBytes - 1024);
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(3, buffer.Length)], cancellationToken);
    }
}
