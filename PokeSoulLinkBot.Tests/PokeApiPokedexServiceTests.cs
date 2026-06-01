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
}
