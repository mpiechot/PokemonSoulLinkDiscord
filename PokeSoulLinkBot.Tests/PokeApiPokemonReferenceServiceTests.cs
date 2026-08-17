using System.Net;
using System.Text;
using PokeSoulLinkBot.Application.Services;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class PokeApiPokemonReferenceServiceTests
{
    [Fact]
    public async Task GetTypeInfoAsync_ShouldMapDamageRelations()
    {
        using var client = new HttpClient(new JsonHandler("""
            {"name":"fire","damage_relations":{"double_damage_to":[{"name":"grass"}],"half_damage_to":[{"name":"water"}],"no_damage_to":[]}}
            """))
        {
            BaseAddress = new Uri("https://pokeapi.test/api/v2/"),
        };
        var service = new PokeApiPokemonReferenceService(client);

        var result = await service.GetTypeInfoAsync(" Fire ");

        Assert.NotNull(result);
        Assert.Equal("fire", result.Name);
        Assert.Equal(new[] { "grass" }, result.DoubleDamageTo);
        Assert.Equal(new[] { "water" }, result.HalfDamageTo);
    }

    [Fact]
    public async Task GetAttackInfoAsync_ShouldPreferGermanNameAndEnglishEffect()
    {
        using var client = new HttpClient(new JsonHandler("""
            {"name":"thunderbolt","names":[{"name":"Donnerblitz","language":{"name":"de"}}],"type":{"name":"electric"},"damage_class":{"name":"special"},"power":90,"accuracy":100,"pp":15,"effect_entries":[{"effect":"May paralyze.","language":{"name":"en"}}]}
            """))
        {
            BaseAddress = new Uri("https://pokeapi.test/api/v2/"),
        };
        var service = new PokeApiPokemonReferenceService(client);

        var result = await service.GetAttackInfoAsync("Thunder Bolt");

        Assert.NotNull(result);
        Assert.Equal("Donnerblitz", result.GermanName);
        Assert.Equal("electric", result.Type);
        Assert.Equal(90, result.Power);
        Assert.Equal("May paralyze.", result.Effect);
    }

    private sealed class JsonHandler : HttpMessageHandler
    {
        private readonly string json;

        public JsonHandler(string json)
        {
            this.json = json;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(this.json, Encoding.UTF8, "application/json"),
            });
        }
    }
}
