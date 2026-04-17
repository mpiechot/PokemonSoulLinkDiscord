using System.Net;
using PokeSoulLinkBot.Application.Services;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class PokemonDbArenaInfoServiceTests
{
    [Fact]
    public async Task GetArenaInfoAsync_ShouldLoadLeaderAndLevelsFromPokemonDbPage()
    {
        var service = CreateService(
            """
            <main>
            <h2 id="gym-1">Gym #1, Rustboro City</h2>
            <div class="infocard-list-trainer-pkmn">
            <span class="infocard trainer-head"><span class="ent-name">Roxanne</span></span>
            <div class="infocard trainer-pkmn"><small>Level 14</small></div>
            <div class="infocard trainer-pkmn"><small>Level 15</small></div>
            </div>
            <h2 id="gym-2">Gym #2, Dewford Town</h2>
            </main>
            """);

        var arenaInfo = await service.GetArenaInfoAsync("ruby", 1);

        Assert.Equal("ruby", arenaInfo.Edition);
        Assert.Equal(1, arenaInfo.ArenaNumber);
        Assert.Equal("Rustboro City", arenaInfo.Location);
        Assert.Equal("Roxanne", arenaInfo.LeaderName);
        Assert.Equal(new[] { 14, 15 }, arenaInfo.Levels);
    }

    [Fact]
    public async Task GetArenaInfoAsync_ShouldDecodeHtmlEntitiesInLeaderName()
    {
        var service = CreateService(
            """
            <main>
            <h2 id="gym-7">Gym #7, Mossdeep City</h2>
            <div class="infocard-list-trainer-pkmn">
            <span class="infocard trainer-head"><span class="ent-name">Tate &amp; Liza</span></span>
            <div class="infocard trainer-pkmn"><small>Level 42</small></div>
            <div class="infocard trainer-pkmn"><small>Level 42</small></div>
            </div>
            <h2 id="gym-8">Gym #8, Sootopolis City</h2>
            </main>
            """);

        var arenaInfo = await service.GetArenaInfoAsync("sapphire", 7);

        Assert.Equal("Tate & Liza", arenaInfo.LeaderName);
        Assert.Equal(new[] { 42, 42 }, arenaInfo.Levels);
    }

    [Fact]
    public async Task GetArenaInfoAsync_ShouldRejectInvalidArenaNumbers()
    {
        var service = CreateService("<main></main>");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetArenaInfoAsync("ruby", 9));

        Assert.Equal("Arena '9' ist ungültig. Bitte wähle eine Arena zwischen 1 und 8.", exception.Message);
    }

    private static PokemonDbArenaInfoService CreateService(string html)
    {
        return new PokemonDbArenaInfoService(new HttpClient(new StubHttpMessageHandler(html)));
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly string html;

        public StubHttpMessageHandler(string html)
        {
            this.html = html;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(this.html),
            });
        }
    }
}
