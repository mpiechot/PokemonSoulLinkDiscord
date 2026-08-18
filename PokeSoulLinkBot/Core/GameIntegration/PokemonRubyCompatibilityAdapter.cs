using System.Text;

namespace PokeSoulLinkBot.Core.GameIntegration;

/// <summary>
/// Identifies the German Pokémon Rubin target and its Ruby-family references.
/// </summary>
public sealed class PokemonRubyCompatibilityAdapter : IRomCompatibilityAdapter
{
    private const int GameTitleOffset = 0xA0;
    private const int GameTitleLength = 12;
    private const int GameCodeOffset = 0xAC;
    private const int GameCodeLength = 4;
    private const int RevisionOffset = 0xBC;
    private const int MinimumHeaderLength = RevisionOffset + 1;
    private const string ExpectedTitle = "POKEMON RUBY";
    private const string GermanGameCode = "AXVD";

    /// <inheritdoc />
    public string AdapterId => "pokemon-ruby";

    /// <inheritdoc />
    public RomCompatibilityResult Resolve(ReadOnlySpan<byte> romHeader)
    {
        if (romHeader.Length < MinimumHeaderLength)
        {
            return new RomCompatibilityResult(
                RomSupportStatus.Unsupported,
                null,
                null,
                $"ROM header is too short; expected at least 0x{MinimumHeaderLength:X} bytes.");
        }

        string title = ReadAscii(romHeader.Slice(GameTitleOffset, GameTitleLength));
        string gameCode = ReadAscii(romHeader.Slice(GameCodeOffset, GameCodeLength));
        var identity = new RomIdentity(title, gameCode, romHeader[RevisionOffset]);

        if (!string.Equals(title, ExpectedTitle, StringComparison.OrdinalIgnoreCase))
        {
            return new RomCompatibilityResult(
                RomSupportStatus.Unsupported,
                identity,
                null,
                "ROM title is not Pokémon Ruby.");
        }

        if (!string.Equals(gameCode, GermanGameCode, StringComparison.OrdinalIgnoreCase))
        {
            return new RomCompatibilityResult(
                RomSupportStatus.DiagnosticOnly,
                identity,
                null,
                "Ruby title recognized, but this adapter currently targets the German AXVD profile.");
        }

        return new RomCompatibilityResult(
            RomSupportStatus.DiagnosticOnly,
            identity,
            new RomMemoryLayoutReference(
                "pokemon-ruby-party-reference",
                0x03004360,
                0x64,
                6,
                "reference-only; validate against an AXVD fixture before live polling",
                "pret/pokeruby include/pokemon.h; reference address documented by Bulbapedia"),
            "German Pokémon Ruby AXVD recognized without CRC32; live memory polling remains fixture-gated.");
    }

    private static string ReadAscii(ReadOnlySpan<byte> bytes)
    {
        return Encoding.ASCII.GetString(bytes).TrimEnd('\0', ' ');
    }
}
