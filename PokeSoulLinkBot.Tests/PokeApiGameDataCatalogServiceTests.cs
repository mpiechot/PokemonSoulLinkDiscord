using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Core.Models;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class PokeApiGameDataCatalogServiceTests
{
    [Fact]
    public async Task GetEditionsAsync_ShouldReturnNoEditionsWhenCatalogIsUnavailable()
    {
        var cacheFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "game-data-catalog.json");
        using var httpClient = new HttpClient(new FailingHttpMessageHandler())
        {
            BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
        };
        var service = new PokeApiGameDataCatalogService(httpClient, cacheFilePath);

        var editions = await service.GetEditionsAsync();

        Assert.Empty(editions);
    }

    [Fact]
    public async Task GetEditionsAsync_ShouldReturnWithoutWaitingForApiRefresh()
    {
        var cacheFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "game-data-catalog.json");
        using var httpClient = new HttpClient(new BlockingHttpMessageHandler())
        {
            BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
        };
        var service = new PokeApiGameDataCatalogService(httpClient, cacheFilePath);

        var editions = await service.GetEditionsAsync().WaitAsync(TimeSpan.FromSeconds(1));

        Assert.Empty(editions);
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

    [Fact]
    public async Task BackgroundRefresh_ShouldFetchLocationAreasInParallelAndSaveCache()
    {
        var cacheFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "game-data-catalog.json");
        var handler = new ParallelLocationAreaHttpMessageHandler();
        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
        };
        var service = new PokeApiGameDataCatalogService(httpClient, cacheFilePath);

        await service.GetEditionsAsync();
        await handler.AllDetailRequestsStarted.Task.WaitAsync(TimeSpan.FromSeconds(1));
        handler.ReleaseDetailRequests.SetResult();
        var catalog = await WaitForSavedCatalogAsync(cacheFilePath);

        Assert.True(handler.MaxConcurrentDetailRequests > 1);
        var ruby = Assert.Single(catalog.Editions, edition => edition.Name == "ruby");
        Assert.Equal(["Route A", "Route B", "Route C"], ruby.Routes);
    }

    [Fact]
    public async Task BackgroundRefresh_ShouldUseRefreshedCatalogWhenCacheCannotBeSaved()
    {
        var cacheFilePath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "game-data-catalog.json");
        Directory.CreateDirectory(cacheFilePath);
        using var httpClient = new HttpClient(new ImmediateCatalogHttpMessageHandler())
        {
            BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
        };
        var service = new PokeApiGameDataCatalogService(httpClient, cacheFilePath);

        await service.GetEditionsAsync();
        var editions = await WaitForEditionsAsync(service);

        var ruby = Assert.Single(editions, edition => edition.Name == "ruby");
        Assert.Equal(["Route A"], ruby.Routes);
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

    private static async Task<GameDataCatalog> WaitForSavedCatalogAsync(string cacheFilePath)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (File.Exists(cacheFilePath))
            {
                await using var stream = File.OpenRead(cacheFilePath);
                var catalog = await JsonSerializer.DeserializeAsync<GameDataCatalog>(
                    stream,
                    new JsonSerializerOptions(JsonSerializerDefaults.Web));

                if (catalog is not null)
                {
                    return catalog;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new InvalidOperationException("Game data catalog cache was not saved in time.");
    }

    private static async Task<IReadOnlyCollection<GameEditionInfo>> WaitForEditionsAsync(
        PokeApiGameDataCatalogService service)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var editions = await service.GetEditionsAsync();
            if (editions.Count > 0)
            {
                return editions;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50));
        }

        throw new InvalidOperationException("Game data catalog was not refreshed in time.");
    }

    private sealed class BlockingHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var responseCompletion = new TaskCompletionSource<HttpResponseMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            cancellationToken.Register(() => responseCompletion.TrySetCanceled(cancellationToken));

            return responseCompletion.Task;
        }
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

    private sealed class ImmediateCatalogHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/version", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CreateJsonResponse(
                    """
                    {
                      "results": [
                        { "name": "ruby", "url": "https://pokeapi.co/api/v2/version/7/" }
                      ]
                    }
                    """));
            }

            if (path.EndsWith("/location-area", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CreateJsonResponse(
                    """
                    {
                      "results": [
                        { "name": "route-a", "url": "https://pokeapi.co/api/v2/location-area/1/" }
                      ]
                    }
                    """));
            }

            if (path.Contains("/location-area/", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(CreateJsonResponse(
                    """
                    {
                      "name": "route-a",
                      "names": [
                        {
                          "name": "Route A",
                          "language": { "name": "en" }
                        }
                      ],
                      "pokemon_encounters": [
                        {
                          "version_details": [
                            {
                              "version": { "name": "ruby" }
                            }
                          ]
                        }
                      ]
                    }
                    """));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static HttpResponseMessage CreateJsonResponse(string json)
        {
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class ParallelLocationAreaHttpMessageHandler : HttpMessageHandler
    {
        private int activeDetailRequests;
        private int completedDetailRequests;
        private int maxConcurrentDetailRequests;

        public TaskCompletionSource AllDetailRequestsStarted { get; } =
            new (TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource ReleaseDetailRequests { get; } =
            new (TaskCreationOptions.RunContinuationsAsynchronously);

        public int MaxConcurrentDetailRequests => this.maxConcurrentDetailRequests;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath ?? string.Empty;
            if (path.EndsWith("/version", StringComparison.OrdinalIgnoreCase))
            {
                return CreateJsonResponse(
                    """
                    {
                      "results": [
                        { "name": "ruby", "url": "https://pokeapi.co/api/v2/version/7/" }
                      ]
                    }
                    """);
            }

            if (path.EndsWith("/location-area", StringComparison.OrdinalIgnoreCase))
            {
                return CreateJsonResponse(
                    """
                    {
                      "results": [
                        { "name": "route-a", "url": "https://pokeapi.co/api/v2/location-area/1/" },
                        { "name": "route-b", "url": "https://pokeapi.co/api/v2/location-area/2/" },
                        { "name": "route-c", "url": "https://pokeapi.co/api/v2/location-area/3/" }
                      ]
                    }
                    """);
            }

            if (path.Contains("/location-area/", StringComparison.OrdinalIgnoreCase))
            {
                var activeRequests = Interlocked.Increment(ref this.activeDetailRequests);
                this.UpdateMaxConcurrentDetailRequests(activeRequests);

                if (Interlocked.Increment(ref this.completedDetailRequests) == 3)
                {
                    this.AllDetailRequestsStarted.SetResult();
                }

                await this.ReleaseDetailRequests.Task.WaitAsync(cancellationToken);
                Interlocked.Decrement(ref this.activeDetailRequests);

                return CreateJsonResponse(CreateLocationAreaJson(path));
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

        private static string CreateLocationAreaJson(string path)
        {
            var routeName = path.Split('/').Last();
            var displayName = routeName switch
            {
                "route-a" => "Route A",
                "route-b" => "Route B",
                "route-c" => "Route C",
                _ => "Unknown Route",
            };

            return $$"""
                {
                  "name": "{{routeName}}",
                  "names": [
                    {
                      "name": "{{displayName}}",
                      "language": { "name": "en" }
                    }
                  ],
                  "pokemon_encounters": [
                    {
                      "version_details": [
                        {
                          "version": { "name": "ruby" }
                        }
                      ]
                    }
                  ]
                }
                """;
        }

        private void UpdateMaxConcurrentDetailRequests(int activeRequests)
        {
            int initialValue;
            do
            {
                initialValue = this.maxConcurrentDetailRequests;
                if (activeRequests <= initialValue)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(
                ref this.maxConcurrentDetailRequests,
                activeRequests,
                initialValue) != initialValue);
        }
    }
}
