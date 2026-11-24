namespace PokeSoulLinkBot.Core.GameIntegration;

/// <summary>
/// Resolves a ROM header into an edition-specific compatibility profile.
/// </summary>
public interface IRomCompatibilityAdapter
{
    /// <summary>
    /// Gets the stable adapter identifier.
    /// </summary>
    string AdapterId { get; }

    /// <summary>
    /// Evaluates the header without relying on a CRC32 value.
    /// </summary>
    /// <param name="romHeader">At least the first 0xBD bytes of the ROM.</param>
    /// <returns>The compatibility result.</returns>
    RomCompatibilityResult Resolve(ReadOnlySpan<byte> romHeader);
}
