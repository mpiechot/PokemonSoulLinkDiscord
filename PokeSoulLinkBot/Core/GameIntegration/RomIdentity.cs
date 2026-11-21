namespace PokeSoulLinkBot.Core.GameIntegration;

/// <summary>
/// Identifies a GBA ROM using header values that survive ROM randomization.
/// </summary>
public sealed record RomIdentity
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RomIdentity"/> class.
    /// </summary>
    public RomIdentity(string title, string gameCode, byte revision)
    {
        this.Title = title;
        this.GameCode = gameCode;
        this.Revision = revision;
    }

    /// <summary>
    /// Gets the ROM title.
    /// </summary>
    public string Title { get; }

    /// <summary>
    /// Gets the four-character game code.
    /// </summary>
    public string GameCode { get; }

    /// <summary>
    /// Gets the ROM revision byte.
    /// </summary>
    public byte Revision { get; }
}
