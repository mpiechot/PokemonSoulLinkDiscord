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

    [Fact]
    public async Task ResolvePokemonNameAsync_ShouldUsePersistedLocalizedNameIndex()
    {
        string cacheFilePath = CreateTemporaryCacheFilePath();
        try
        {
            var cacheStore = new PokemonDataCacheStore(cacheFilePath);
            await cacheStore.SaveNameIndexAsync(new Dictionary<string, string>
            {
                ["bisasam"] = "bulbasaur",
            });

            var handler = new NotFoundNameResolverHttpMessageHandler();
            using var httpClient = new HttpClient(handler)
            {
                BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
            };
            var resolver = new PokeApiPokemonNameResolver(httpClient, cacheStore);

            var resolvedName = await resolver.ResolvePokemonNameAsync("Bisasam");

            Assert.Equal("bulbasaur", resolvedName);
            Assert.Equal(1, handler.DirectRequestCount);
            Assert.Equal(0, handler.SpeciesIndexRequestCount);
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

    private sealed class NotFoundNameResolverHttpMessageHandler : HttpMessageHandler
    {
        public int DirectRequestCount { get; private set; }

        public int SpeciesIndexRequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.Contains("pokemon-species", StringComparison.OrdinalIgnoreCase) == true)
            {
                this.SpeciesIndexRequestCount++;
            }
            else
            {
                this.DirectRequestCount++;
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }
}
