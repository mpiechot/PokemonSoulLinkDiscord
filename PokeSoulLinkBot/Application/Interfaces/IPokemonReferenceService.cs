using PokeSoulLinkBot.Core.Models;

namespace PokeSoulLinkBot.Application.Interfaces;

public interface IPokemonReferenceService
{
    Task<TypeInfo?> GetTypeInfoAsync(string typeName);

    Task<AttackInfo?> GetAttackInfoAsync(string moveName);
}
