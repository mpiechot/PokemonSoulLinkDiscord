using PokeSoulLinkBot.Core.Configuration;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class SoulLinkOptionsTests
{
    [Fact]
    public void Defaults_KeepRemoteWritesAndAutoTeamSyncDisabled()
    {
        var options = new SoulLinkOptions();

        Assert.True(options.Enabled);
        Assert.True(options.EnableDiscordEvents);
        Assert.True(options.EnableReadTracking);
        Assert.False(options.EnableRemoteWrites);
        Assert.False(options.EnableAutoTeamSync);
        Assert.True(options.IsValid());
    }

    [Fact]
    public void AutoTeamSyncWithoutRemoteWrites_IsInvalid()
    {
        var options = new SoulLinkOptions
        {
            EnableAutoTeamSync = true,
        };

        Assert.False(options.IsValid());
    }
}
