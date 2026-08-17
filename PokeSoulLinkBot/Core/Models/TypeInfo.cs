namespace PokeSoulLinkBot.Core.Models;

public sealed class TypeInfo
{
    public string Name { get; init; } = string.Empty;

    public IReadOnlyList<string> DoubleDamageTo { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> HalfDamageTo { get; init; } = Array.Empty<string>();

    public IReadOnlyList<string> NoDamageTo { get; init; } = Array.Empty<string>();
}

public sealed class AttackInfo
{
    public string Name { get; init; } = string.Empty;

    public string? GermanName { get; init; }

    public string? Type { get; init; }

    public string? DamageClass { get; init; }

    public int? Power { get; init; }

    public int? Accuracy { get; init; }

    public int? Pp { get; init; }

    public string? Effect { get; init; }
}
