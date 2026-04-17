using System.Net;
using System.Text;
using System.Text.Json;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Core.Models;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class PokeApiGameDataCatalogServiceTests
{
    [Fact]
    public async Task GetEditionsAsync_ShouldUseFallbackEditionsWhenRefreshFails()
    {
        var cacheFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "game-data-catalog.json");
        using var httpClient = new HttpClient(new FailingHttpMessageHandler())
        {
            BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
        };
        var service = new PokeApiGameDataCatalogService(httpClient, cacheFilePath);

        var editions = await service.GetEditionsAsync();

        Assert.Contains(editions, edition => edition.Name == "ruby" && edition.DisplayName == "Ruby");
        Assert.Contains(editions, edition => edition.Name == "scarlet" && edition.DisplayName == "Scarlet");
    }

    [Fact]
    public async Task GetEditionsAsync_ShouldUseCachedCatalog()
    {
        var cacheFilePath = CreateCacheFile(new GameDataCatalog
        {
            RefreshedAtUtc = DateTime.UtcNow,
            Editions =
            [
                new GameEditionInfo
                {
                    Name = "ruby",
                    DisplayName = "Ruby",
                    Routes = ["Petalburg Woods"],
                },
            ],
        });

        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var service = new PokeApiGameDataCatalogService(httpClient, cacheFilePath);

        var editions = await service.GetEditionsAsync();

        Assert.Contains(editions, edition => edition.Name == "ruby" && edition.DisplayName == "Ruby");
    }

    [Theory]
    [InlineData("Ruby")]
    [InlineData("ruby")]
    public async Task GetRoutesAsync_ShouldResolveEditionNamesFromCache(string editionName)
    {
        var cacheFilePath = CreateCacheFile(new GameDataCatalog
        {
            RefreshedAtUtc = DateTime.UtcNow,
            Editions =
            [
                new GameEditionInfo
                {
                    Name = "ruby",
                    DisplayName = "Ruby",
                    Routes = ["Petalburg Woods", "Route 101"],
                },
            ],
        });

        using var httpClient = new HttpClient(new FailingHttpMessageHandler());
        var service = new PokeApiGameDataCatalogService(httpClient, cacheFilePath);

        var routes = await service.GetRoutesAsync(editionName);

        Assert.Equal(["Petalburg Woods", "Route 101"], routes);
    }

    private static string CreateCacheFile(GameDataCatalog catalog)
    {
        var cacheDirectoryPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(cacheDirectoryPath);

        var cacheFilePath = Path.Combine(cacheDirectoryPath, "game-data-catalog.json");
        var json = JsonSerializer.Serialize(catalog, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        File.WriteAllText(cacheFilePath, json, Encoding.UTF8);

        return cacheFilePath;
    }

    private sealed class FailingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
        }
    }
}
