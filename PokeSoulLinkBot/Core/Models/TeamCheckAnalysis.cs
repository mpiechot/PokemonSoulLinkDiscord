namespace PokeSoulLinkBot.Core.Models;

public sealed class TeamCheckAnalysis
{
    public IReadOnlyList<TeamTypeCoverage> CurrentCoverage { get; init; } = Array.Empty<TeamTypeCoverage>();

    public IReadOnlyList<TeamTypeCoverage> OptimalCoverage { get; init; } = Array.Empty<TeamTypeCoverage>();

    public IReadOnlyList<LinkGroup> OptimalLinkGroups { get; init; } = Array.Empty<LinkGroup>();
}

public sealed class TeamTypeCoverage
{
    public ulong PlayerId { get; init; }

    public string PlayerName { get; init; } = string.Empty;

    public IReadOnlyList<string> Types { get; init; } = Array.Empty<string>();
}
