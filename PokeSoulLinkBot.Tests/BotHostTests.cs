using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PokeSoulLinkBot.Bot.Handlers;
using PokeSoulLinkBot.Bot.Hosting;
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
    }
}
