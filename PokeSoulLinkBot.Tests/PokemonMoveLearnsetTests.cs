using System.Net;
using System.Text;
using PokeSoulLinkBot.Application.Services;
using PokeSoulLinkBot.Bot.Presentation;
using PokeSoulLinkBot.Core.Models;
using PokeSoulLinkBot.Application.Interfaces;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class PokemonMoveLearnsetTests
{
    [Fact]
    public void CreateTableMessages_ShouldSplitLongLearnsetsBelowDiscordLimit()
    {
        var presenter = new PokemonMoveLearnsetPresenter();
        var learnset = new PokemonMoveLearnset
        {
            PokemonName = "Pikachu",
            LevelUpMoves = Enumerable.Range(1, 250)
                .Select(level => new LevelUpMove { Level = level, MoveName = $"Sehr lange Attacke {level} mit Zusatztext" })
                .ToList(),
        };

        var messages = presenter.CreateTableMessages(learnset);

        Assert.True(messages.Count > 1);
        Assert.All(messages, message => Assert.True(message.Length <= 2000));
        Assert.All(messages, message => Assert.StartsWith("```", message));
        Assert.All(messages, message => Assert.EndsWith("```", message));
    }

    [Fact]
    public void CreateTableMessage_ShouldRenderLevelAndMachineMovesCompactly()
    {
        var presenter = new PokemonMoveLearnsetPresenter();
        var learnset = new PokemonMoveLearnset
        {
            PokemonName = "Pikachu",
            LevelUpMoves = new List<LevelUpMove>
            {
                new() { Level = 1, MoveName = "Thunder Shock" },
                new() { Level = 5, MoveName = "Quick Attack" },
            },
            MachineMoves = new List<MachineMove>
            {
                new() { MachineName = "TM24", MoveName = "Thunderbolt" },
                new() { MachineName = "HM03", MoveName = "Surf" },
            },
        };

        var message = presenter.CreateTableMessage(learnset);

        Assert.Contains("Level-up", message);
        Assert.Contains("Lv 1: Thunder Shock", message);
        Assert.Contains("Lv 5: Quick Attack", message);
        Assert.Contains("TM/HM", message);
        Assert.Contains("TM24: Thunderbolt", message);
        Assert.Contains("HM03: Surf", message);
    }

    [Fact]
    public async Task GetMoveLearnsetAsync_ShouldParseLevelAndMachineMoves()
    {
        using var httpClient = new HttpClient(new LearnsetHttpMessageHandler())
        {
            BaseAddress = new Uri("https://pokeapi.co/api/v2/"),
        };
        var service = new PokeApiPokedexService(httpClient, new StubPokemonNameResolver());

        PokemonMoveLearnset learnset = await service.GetMoveLearnsetAsync("Pikachu");

        Assert.Equal("Pikachu", learnset.PokemonName);
        Assert.Contains(learnset.LevelUpMoves, move => move.Level == 1 && move.MoveName == "Donnerschock");
                Assert.Contains(learnset.MachineMoves, move => move.MachineName == "TM24" && move.MoveName == "Donnerblitz");
    }

    private sealed class StubPokemonNameResolver : IPokemonNameResolver
    {
        public Task<string> ResolvePokemonNameAsync(string pokemonName)
        {
            return Task.FromResult("pikachu");
        }
    }

    private sealed class LearnsetHttpMessageHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var path = request.RequestUri?.AbsolutePath.TrimEnd('/') ?? string.Empty;
            var json = path switch
            {
                "/api/v2/pokemon/pikachu" => """
                {
                  "name": "pikachu",
                  "moves": [
                    {
                      "move": { "name": "thunder-shock", "url": "https://pokeapi.co/api/v2/move/84/" },
                      "version_group_details": [
                        {
                          "level_learned_at": 1,
                          "move_learn_method": { "name": "level-up" },
                          "version_group": { "name": "red-blue" }
                        }
                      ]
                    },
                    {
                      "move": { "name": "thunderbolt", "url": "https://pokeapi.co/api/v2/move/85/" },
                      "version_group_details": [
                        {
                          "level_learned_at": 0,
                          "move_learn_method": { "name": "machine" },
                          "version_group": { "name": "red-blue" }
                        }
                      ]
                    }
                  ]
                }
                """,
                "/api/v2/move/84" => """
                {
                  "name": "thunder-shock",
                  "names": [
                    { "name": "Donnerschock", "language": { "name": "de" } }
                  ]
                }
                """,
                "/api/v2/move/85" => """
                {
                  "name": "thunderbolt",
                  "names": [
                    { "name": "Donnerblitz", "language": { "name": "de" } }
                  ],
                  "machines": [
                    {
                      "machine": { "url": "https://pokeapi.co/api/v2/machine/24/" },
                      "version_group": { "name": "red-blue" }
                    }
                  ]
                }
                """,
                "/api/v2/machine/24" => """
                {
                  "item": { "name": "tm24", "url": "https://pokeapi.co/api/v2/item/24/" }
                }
                """,
                "/api/v2/item/24" => """
                {
                  "name": "tm24",
                  "names": [
                    { "name": "TM24", "language": { "name": "de" } }
                  ]
                }
                """,
                _ => string.Empty,
            };

            if (string.IsNullOrEmpty(json))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
