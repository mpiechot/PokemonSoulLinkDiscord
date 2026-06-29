using System.Net;
using System.Text;
using PokeSoulLinkBot.Application.Interfaces;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Core.Models;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class PokeApiPokedexServiceTests
{
    [Fact]
    public async Task GetPokedexEntryAsync_ShouldUsePersistedCacheWhenAvailable()
    {
        string cacheFilePath = CreateTemporaryCacheFilePath();
        try
        {
            var cacheStore = new PokemonDataCacheStore(cacheFilePath);
            await cacheStore.SavePokedexEntryAsync("bulbasaur", new PokedexEntry
            {
                PokemonName = "Bulbasaur",
                ImageUrl = "https://img.example/bulbasaur.png",
                Rows = new List<PokedexTableRow>
                {
                    new()
                    {
                        PokemonName = "Bulbasaur",
                        RequirementText = "Basis",
                        Types = new[] { "Grass", "Poison" },
                    },
                },
            });

            var handler = new CountingPokedexHttpMessageHandler();
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
            };
            var service = new PokeApiPokedexService(
                httpClient,
                new StubPokemonNameResolver("bulbasaur"),
                cacheStore);

            PokedexEntry entry = await service.GetPokedexEntryAsync("Bisasam");

            Assert.Equal("Bulbasaur", entry.PokemonName);
            Assert.Equal("https://img.example/bulbasaur.png", entry.ImageUrl);
            Assert.Single(entry.Rows);
            Assert.Equal(0, handler.RequestCount);
        }
        finally
        {
            DeleteTemporaryCacheFile(cacheFilePath);
        }
    }

    [Fact]
    public async Task GetPokedexEntryAsync_ShouldCoalesceConcurrentRequests()
    {
        var handler = new SuccessfulPokedexHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
        };
        var service = new PokeApiPokedexService(
            httpClient,
            new StubPokemonNameResolver("bulbasaur"));

        var firstTask = service.GetPokedexEntryAsync("Bisasam");
        var secondTask = service.GetPokedexEntryAsync("Bulbasaur");
        var results = await Task.WhenAll(firstTask, secondTask);

        Assert.Same(results[0], results[1]);
        Assert.Equal("Bulbasaur", results[0].PokemonName);
        Assert.Equal(3, handler.RequestCount);
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

    private sealed class CountingPokedexHttpMessageHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            this.RequestCount++;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.InternalServerError));
        }
    }

    private sealed class SuccessfulPokedexHttpMessageHandler : HttpMessageHandler
    {
        private int requestCount;

        public int RequestCount => Volatile.Read(ref this.requestCount);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref this.requestCount);
            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);

            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/pokemon/bulbasaur", StringComparison.OrdinalIgnoreCase))
            {
                return CreateJsonResponse(
                    """
                    {
                      "name": "bulbasaur",
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
                    """);
            }

            if (path.EndsWith("/pokemon-species/bulbasaur", StringComparison.OrdinalIgnoreCase))
            {
                return CreateJsonResponse(
                    """
                    {
                      "name": "bulbasaur",
                      "evolution_chain": {
                        "url": "https://pokeapi.co/api/v2/evolution-chain/1/"
                      }
                    }
                    """);
            }

            if (path.TrimEnd('/').EndsWith("/evolution-chain/1", StringComparison.OrdinalIgnoreCase))
            {
                return CreateJsonResponse(
                    """
                    {
                      "chain": {
                        "species": { "name": "bulbasaur" },
                        "evolution_details": [],
                        "evolves_to": []
                      }
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        private static HttpResponseMessage CreateJsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }
}
