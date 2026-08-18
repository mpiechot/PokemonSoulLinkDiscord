using PokeSoulLinkBot.Core.GameIntegration;
using Xunit;

namespace PokeSoulLinkBot.Tests;

public sealed class PokemonRubyCompatibilityAdapterTests
{
    [Fact]
    public void Resolve_RecognizesGermanRubyWithoutUsingCrc32()
    {
        byte[] header = CreateHeader("POKEMON RUBY", "AXVD", revision: 1);
        header[0xB0] = 0xFF;
        header[0xB1] = 0xFF;

        RomCompatibilityResult result = new PokemonRubyCompatibilityAdapter().Resolve(header);

        Assert.Equal(RomSupportStatus.DiagnosticOnly, result.Status);
        Assert.Equal("AXVD", result.Identity?.GameCode);
        Assert.Equal(0x03004360u, result.MemoryLayout?.PlayerPartyAddress);
        Assert.Contains("CRC32", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Resolve_RejectsDifferentTitle()
    {
        byte[] header = CreateHeader("POKEMON SAPP", "AXPD", revision: 0);

        RomCompatibilityResult result = new PokemonRubyCompatibilityAdapter().Resolve(header);

        Assert.Equal(RomSupportStatus.Unsupported, result.Status);
        Assert.Null(result.MemoryLayout);
    }

    [Fact]
    public void Resolve_RecognizesRubyTitleButKeepsOtherVariantsDiagnosticOnly()
    {
        byte[] header = CreateHeader("POKEMON RUBY", "AXVE", revision: 0);

        RomCompatibilityResult result = new PokemonRubyCompatibilityAdapter().Resolve(header);

        Assert.Equal(RomSupportStatus.DiagnosticOnly, result.Status);
        Assert.Null(result.MemoryLayout);
        Assert.Contains("AXVD", result.Diagnostic, StringComparison.OrdinalIgnoreCase);
    }

    private static byte[] CreateHeader(string title, string gameCode, byte revision)
    {
        var header = new byte[0xC0];
        WriteAscii(header, 0xA0, 12, title);
        WriteAscii(header, 0xAC, 4, gameCode);
        header[0xBC] = revision;
        return header;
    }

    private static void WriteAscii(byte[] target, int offset, int length, string value)
    {
        for (int index = 0; index < Math.Min(length, value.Length); index++)
        {
            target[offset + index] = (byte)value[index];
        }
    }
}
