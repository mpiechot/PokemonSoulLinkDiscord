using System.Net;
using System.Text;
using PokeSoulLinkBot.Application.Services;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class PokeApiPokemonNameResolverTests
{
    [Fact]
    public async Task ResolvePokemonNameAsync_ShouldCacheResolvedDirectNames()
    {
        var handler = new CountingNameResolverHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
        };
        var resolver = new PokeApiPokemonNameResolver(httpClient);

        var firstName = await resolver.ResolvePokemonNameAsync("Bulbasaur");
        var secondName = await resolver.ResolvePokemonNameAsync("bulbasaur");

        Assert.Equal("bulbasaur", firstName);
        Assert.Equal("bulbasaur", secondName);
        Assert.Equal(1, handler.RequestCount);
    }

    private sealed class CountingNameResolverHttpMessageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.RequestCount++;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "name": "bulbasaur"
                    }
                    """,
                    Encoding.UTF8,
                    "application/json"),
            });
        }
    }
}
