namespace PokeSoulLinkBot.Core.Configuration;

/// <summary>
/// Runtime switches and paths for the SoulLink host.
/// </summary>
public sealed class SoulLinkOptions
{
    /// <summary>
    /// The configuration section name.
    /// </summary>
    public const string SectionName = "SoulLink";

    /// <summary>
    /// Gets or sets a value indicating whether the SoulLink host is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether Discord event handling is enabled.
    /// </summary>
    public bool EnableDiscordEvents { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether read-only game tracking is enabled.
    /// </summary>
    public bool EnableReadTracking { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether remote game writes are enabled.
    /// </summary>
    public bool EnableRemoteWrites { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether automatic team synchronization is enabled.
    /// </summary>
    public bool EnableAutoTeamSync { get; set; }

    /// <summary>
    /// Gets or sets the optional run persistence path.
    /// </summary>
    public string? PersistencePath { get; set; }

    /// <summary>
    /// Gets or sets the optional game data cache path.
    /// </summary>
    public string? GameDataCachePath { get; set; }

    /// <summary>
    /// Gets or sets the optional Pokémon data cache path.
    /// </summary>
    public string? PokemonDataCachePath { get; set; }

    /// <summary>
    /// Validates relationships between feature flags.
    /// </summary>
    /// <returns><see langword="true"/> when the options are consistent.</returns>
    public bool IsValid()
    {
        return !this.EnableAutoTeamSync || this.EnableRemoteWrites;
    }
}
