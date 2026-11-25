namespace PokeSoulLinkBot.Core.GameIntegration;

/// <summary>
/// Result of evaluating a ROM header against an adapter.
/// </summary>
public sealed record RomCompatibilityResult
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RomCompatibilityResult"/> class.
    /// </summary>
    public RomCompatibilityResult(
        RomSupportStatus status,
        RomIdentity? identity,
        RomMemoryLayoutReference? memoryLayout,
        string diagnostic)
    {
        this.Status = status;
        this.Identity = identity;
        this.MemoryLayout = memoryLayout;
        this.Diagnostic = diagnostic;
    }

    /// <summary>
    /// Gets the support status.
    /// </summary>
    public RomSupportStatus Status { get; }

    /// <summary>
    /// Gets the parsed identity, when the header was long enough.
    /// </summary>
    public RomIdentity? Identity { get; }

    /// <summary>
    /// Gets the layout reference, when one is available.
    /// </summary>
    public RomMemoryLayoutReference? MemoryLayout { get; }

    /// <summary>
    /// Gets the diagnostic explanation.
    /// </summary>
    public string Diagnostic { get; }
}
