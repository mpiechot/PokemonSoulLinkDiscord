using System.Buffers.Binary;
using SoulLink;

namespace SoulLink.Player;

/// <summary>Reads a battle portrait from the user's own German Ruby cartridge.</summary>
internal static class RubyWikiArtwork
{
    // AXVD 0/1 front sprite and normal palette tables, as documented in gen3_offsets.ini.
    private const int FrontSprites = 0x1F52D0;
    private const int NormalPalettes = 0x1F7530;
    private const int ImageSize = 64;
    private const int FrameBytes = ImageSize * ImageSize / 2;

    public static Bitmap? Read(string path, int species)
    {
        try
        {
            using var rom = File.OpenRead(path);
            if (GameProfiles.Identify(rom)?.Edition != "ruby" || !SpeciesCatalog.IsValid(species)) return null;

            byte[] pixels = ReadEntry(rom, FrontSprites + 8 * species, FrameBytes * 4);
            byte[] palette = ReadEntry(rom, NormalPalettes + 8 * species, 32 * 4);
            if (pixels.Length < FrameBytes || palette.Length < 32) return null;
            var colors = new Color[16];
            for (int color = 1; color < colors.Length; color++)
            {
                int packed = BinaryPrimitives.ReadUInt16LittleEndian(palette.AsSpan(color * 2));
                colors[color] = Color.FromArgb(Expand(packed & 31), Expand((packed >> 5) & 31), Expand((packed >> 10) & 31));
            }

            var sprite = new Bitmap(ImageSize, ImageSize);
            for (int y = 0; y < ImageSize; y++)
            {
                for (int x = 0; x < ImageSize; x++)
                {
                    int tile = (y / 8) * (ImageSize / 8) + x / 8;
                    int packed = pixels[tile * 32 + (y % 8) * 4 + (x % 8) / 2];
                    int color = (packed >> ((x % 2) * 4)) & 15;
                    if (color != 0) sprite.SetPixel(x, y, colors[color]);
                }
            }
            return sprite;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int Expand(int value) => (value << 3) | (value >> 2);

    private static byte[] ReadEntry(Stream rom, int row, int limit)
    {
        Span<byte> pointer = stackalloc byte[4];
        rom.Position = row;
        rom.ReadExactly(pointer);
        long offset = (long)BinaryPrimitives.ReadUInt32LittleEndian(pointer) - 0x08000000;
        if (offset < 0 || offset > rom.Length - 4) throw new InvalidDataException("Invalid Ruby sprite pointer.");
        rom.Position = offset;
        return RubyMapArtwork.ReadLz77(rom, limit);
    }
}
