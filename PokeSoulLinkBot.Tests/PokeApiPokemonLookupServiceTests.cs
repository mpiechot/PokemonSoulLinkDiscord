using System.Net;
using System.Text;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Application.Services;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class PokeApiPokemonLookupServiceTests
{
    [Fact]
    public async Task GetPokemonInfoAsync_ShouldCacheSuccessfulLookupsByResolvedName()
    {
        var handler = new CountingPokemonHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
        };
        var service = new PokeApiPokemonLookupService(httpClient, new StubPokemonNameResolver("bulbasaur"));

        var firstInfo = await service.GetPokemonInfoAsync("Bisasam");
        var secondInfo = await service.GetPokemonInfoAsync("Bulbasaur");

        Assert.NotNull(firstInfo);
        Assert.Same(firstInfo, secondInfo);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetPokemonInfoAsync_ShouldRetryTransientHttpFailures()
    {
        var handler = new TransientThenSuccessfulPokemonHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
        };
        var service = new PokeApiPokemonLookupService(httpClient, new StubPokemonNameResolver("bulbasaur"));

        var pokemonInfo = await service.GetPokemonInfoAsync("Bulbasaur");

        Assert.NotNull(pokemonInfo);
        Assert.Equal(2, handler.RequestCount);
    }

    private static HttpResponseMessage CreatePokemonResponse()
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {
                  "sprites": {
                    "other": {
                      "official-artwork": {
                        "front_default": "https://img.example/bulbasaur.png"
                      }
                    }
                  },
                  "types": [
                    {
                      "slot": 1,
                      "type": { "name": "grass" }
                    }
                  ]
                }
                """,
                Encoding.UTF8,
                "application/json"),
        };
    }

    private sealed class StubPokemonNameResolver : IPokemonNameResolver
    {
        private readonly string resolvedName;

        public StubPokemonNameResolver(string resolvedName)
        {
            this.resolvedName = resolvedName;
        }

        public Task<string> ResolvePokemonNameAsync(string pokemonName)
        {
            return Task.FromResult(this.resolvedName);
        }
    }

    private sealed class CountingPokemonHttpMessageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.RequestCount++;

            return Task.FromResult(CreatePokemonResponse());
        }
    }

    private sealed class TransientThenSuccessfulPokemonHttpMessageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.RequestCount++;

            return Task.FromResult(this.RequestCount == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : CreatePokemonResponse());
        }
    }
}
