namespace PokeSoulLinkBot.Core.GameIntegration;

/// <summary>
/// Describes how far a ROM adapter can safely proceed.
/// </summary>
public enum RomSupportStatus
{
    Unsupported,
    DiagnosticOnly,
    Supported,
}
