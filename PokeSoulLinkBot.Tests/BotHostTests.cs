using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PokeSoulLinkBot.Bot.Handlers;
using PokeSoulLinkBot.Bot.Hosting;
using PokeSoulLinkBot.Core.Configuration;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class BotHostTests
{
    [Fact]
    public void CreateBuilder_ComposesHostedServiceAndCommandRouter()
    {
        HostApplicationBuilder builder = BotHost.CreateBuilder(Array.Empty<string>());
        using IHost host = builder.Build();

        Assert.Contains(
            host.Services.GetServices<IHostedService>(),
            hostedService => hostedService is DiscordBotHostedService);
        Assert.NotNull(host.Services.GetRequiredService<SlashCommandRouter>());

        SoulLinkOptions options = host.Services.GetRequiredService<IOptions<SoulLinkOptions>>().Value;
        Assert.True(options.EnableReadTracking);
        Assert.False(options.EnableRemoteWrites);
        Assert.False(options.EnableAutoTeamSync);
    }
}
