namespace PokeSoulLinkBot.Core.GameIntegration;

/// <summary>
/// Documents a read-only memory layout reference and its validation state.
/// </summary>
public sealed record RomMemoryLayoutReference
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RomMemoryLayoutReference"/> class.
    /// </summary>
    public RomMemoryLayoutReference(
        string name,
        uint playerPartyAddress,
        int partyEntrySize,
        int partySize,
        string validationState,
        string source)
    {
        this.Name = name;
        this.PlayerPartyAddress = playerPartyAddress;
        this.PartyEntrySize = partyEntrySize;
        this.PartySize = partySize;
        this.ValidationState = validationState;
        this.Source = source;
    }

    /// <summary>
    /// Gets the profile name.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Gets the reference address of the player party.
    /// </summary>
    public uint PlayerPartyAddress { get; }

    /// <summary>
    /// Gets the size of one party entry.
    /// </summary>
    public int PartyEntrySize { get; }

    /// <summary>
    /// Gets the number of party entries.
    /// </summary>
    public int PartySize { get; }

    /// <summary>
    /// Gets the validation state.
    /// </summary>
    public string ValidationState { get; }

    /// <summary>
    /// Gets the source description.
    /// </summary>
    public string Source { get; }
}
