using System.Text;
using SoulLink;
using SoulLink.Demo;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class GameProfilesTests
{
    [Theory]
    [InlineData("POKEMON RUBY", "AXVD", 0, true)]
    [InlineData("POKEMON RUBY", "AXVD", 1, true)]
    [InlineData("POKEMON RUBY", "AXVD", 2, false)]
    [InlineData("POKEMON RUBY", "AXVE", 0, false)]
    [InlineData("POKEMON SAPP", "AXPD", 0, false)]
    [InlineData("POKEMON SAPP", "AXVD", 0, false)]
    public void CartridgeHeaderSelectsOnlyVerifiedRevisions(string title, string code, byte revision, bool supported)
    {
        byte[] header = new byte[0xBD];
        Encoding.ASCII.GetBytes(title + code).CopyTo(header, 0xA0);
        header[0xBC] = revision;
        using var rom = new MemoryStream(header) { Position = 35 };

        var profile = GameProfiles.Identify(rom);

        Assert.Equal(supported, profile is not null);
        Assert.Equal(35, rom.Position);
        if (profile is not null) Assert.Same(profile, GameProfiles.ForGameCode(code));
    }

    [Fact]
    public void ReadOnlyLayoutsStillDecodeRubyButDoNotEnableLocalActions()
    {
        var profile = Assert.IsAssignableFrom<GameProfile>(GameProfiles.ForEdition("rubin"));
        Assert.Same(profile, GameProfiles.ForEdition("ruby"));
        Assert.True(profile.SupportsLocalActions("ruby-axvd-reference-v2"));

        var frame = SyntheticPokemon.Frame(1, SyntheticPokemon.Raw(1, 280)) with { LayoutId = "my-verified-axvd-layout" };
        Assert.False(profile.SupportsLocalActions(frame.LayoutId));
        Assert.Equal(16, frame.Parse().Area);
        Assert.Throws<InvalidDataException>(() => (frame with { GameCode = "AXPD" }).Parse());
    }
}
