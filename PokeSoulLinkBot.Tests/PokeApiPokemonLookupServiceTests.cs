using System.Net;
using System.Text;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Core.Models;
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
    public async Task GetPokemonInfoAsync_ShouldCoalesceConcurrentLookupsByResolvedName()
    {
        var handler = new SlowCountingPokemonHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
        };
        var service = new PokeApiPokemonLookupService(httpClient, new StubPokemonNameResolver("bulbasaur"));

        var firstTask = service.GetPokemonInfoAsync("Bisasam");
        var secondTask = service.GetPokemonInfoAsync("Bulbasaur");
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.NotNull(results[0]);
        Assert.Same(results[0], results[1]);
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

    [Fact]
    public async Task GetPokemonInfoAsync_ShouldUsePersistedCacheWhenAvailable()
    {
        string cacheFilePath = CreateTemporaryCacheFilePath();
        try
        {
            var cacheStore = new PokemonDataCacheStore(cacheFilePath);
            await cacheStore.SavePokemonInfoAsync("bulbasaur", new PokemonInfo
            {
                ImageUrl = "https://img.example/cached-bulbasaur.png",
                Types = new[] { "grass", "poison" },
            });

            var handler = new CountingPokemonHttpMessageHandler();
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
            };
            var service = new PokeApiPokemonLookupService(
                httpClient,
                new StubPokemonNameResolver("bulbasaur"),
                cacheStore);

            PokemonInfo? pokemonInfo = await service.GetPokemonInfoAsync("Bisasam");

            Assert.NotNull(pokemonInfo);
            Assert.Equal("https://img.example/cached-bulbasaur.png", pokemonInfo.ImageUrl);
            Assert.Equal(new[] { "grass", "poison" }, pokemonInfo.Types);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            DeleteTemporaryCacheFile(cacheFilePath);
        }
    }

    [Fact]
    public async Task GetPokemonInfoAsync_ShouldPersistSuccessfulLookupsForLaterRequests()
    {
        string cacheFilePath = CreateTemporaryCacheFilePath();
        try
        {
            var cacheStore = new PokemonDataCacheStore(cacheFilePath);
            var handler = new CountingPokemonHttpMessageHandler();
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
            };
            var service = new PokeApiPokemonLookupService(
                httpClient,
                new StubPokemonNameResolver("bulbasaur"),
                cacheStore);

            PokemonInfo? fetchedInfo = await service.GetPokemonInfoAsync("Bulbasaur");

            Assert.NotNull(fetchedInfo);
            Assert.Equal(1, handler.RequestCount);

            var reloadedCacheStore = new PokemonDataCacheStore(cacheFilePath);
            var offlineHandler = new CountingPokemonHttpMessageHandler();
            using var offlineHttpClient = new HttpClient(offlineHandler)
            {
                BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
            };
            var offlineService = new PokeApiPokemonLookupService(
                offlineHttpClient,
                new StubPokemonNameResolver("bulbasaur"),
                reloadedCacheStore);

            PokemonInfo? cachedInfo = await offlineService.GetPokemonInfoAsync("Bulbasaur");

            Assert.NotNull(cachedInfo);
            Assert.Equal(fetchedInfo.ImageUrl, cachedInfo.ImageUrl);
            Assert.Equal(fetchedInfo.Types, cachedInfo.Types);
            Assert.Equal(0, offlineHandler.RequestCount);
        }
        finally
        {
            DeleteTemporaryCacheFile(cacheFilePath);
        }
    }

    private static string CreateTemporaryCacheFilePath()
    {
        string directoryPath = Path.Combine(
            Path.GetTempPath(),
            "PokeSoulLinkBotTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directoryPath);
        return Path.Combine(directoryPath, "pokemon-data-cache.json");
    }

    private static void DeleteTemporaryCacheFile(string cacheFilePath)
    {
        string? directoryPath = Path.GetDirectoryName(cacheFilePath);
        if (!string.IsNullOrWhiteSpace(directoryPath) && Directory.Exists(directoryPath))
        {
            Directory.Delete(directoryPath, recursive: true);
        }
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

    private sealed class SlowCountingPokemonHttpMessageHandler : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => Volatile.Read(ref this.requestCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.requestCount);
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);

            return CreatePokemonResponse();
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
