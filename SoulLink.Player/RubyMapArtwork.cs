using System.Buffers.Binary;

namespace SoulLink.Player;

/// <summary>Decodes the original PokéNav map from the selected German Ruby cartridge at runtime.</summary>
internal static class RubyMapArtwork
{
    // Same data symbols in the AXVD revision 0 and 1 symbol tables from pret/pokeruby.
    private const int PaletteOffset = 0x3F2138;
    private const int TilesOffset = 0x3F2178;
    private const int TileMapOffset = 0x3F2EDC;
    private const int TileMapEnd = 0x3F3228;
    private const int PaletteStart = 0x70;
    private const int TileSize = 8;
    private const int MapStride = 64;
    private const int CropTileX = 1;
    private const int CropTileY = 2;
    private const int MapWidth = 28;
    private const int MapHeight = 15;

    public static Bitmap? Read(string path)
    {
        try
        {
            using var rom = File.OpenRead(path);
            if (rom.Length < TileMapEnd) return null;
            byte[] palette = ReadAt(rom, PaletteOffset, TilesOffset - PaletteOffset);
            byte[] tiles = Decompress(ReadAt(rom, TilesOffset, TileMapOffset - TilesOffset));
            byte[] tileMap = Decompress(ReadAt(rom, TileMapOffset, TileMapEnd - TileMapOffset));
            if (tiles.Length % 64 != 0 || tileMap.Length != MapStride * MapStride) return null;
            var colors = new Color[palette.Length / 2];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = DecodeColor(BinaryPrimitives.ReadUInt16LittleEndian(palette.AsSpan(i * 2)));

            var pixels = new Color[MapWidth * TileSize * MapHeight * TileSize];
            int width = MapWidth * TileSize;
            for (int y = 0; y < MapHeight * TileSize; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int tile = tileMap[(CropTileY + y / TileSize) * MapStride + CropTileX + x / TileSize];
                    int position = tile * 64 + (y % TileSize) * TileSize + x % TileSize;
                    if (position >= tiles.Length) return null;
                    int color = tiles[position] - PaletteStart;
                    if (color < 0 || color >= colors.Length) return null;
                    pixels[y * width + x] = colors[color];
                }
            }
            var bitmap = new Bitmap(width, MapHeight * TileSize);
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++) bitmap.SetPixel(x, y, pixels[y * width + x]);
            return bitmap;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static byte[] ReadAt(Stream rom, int offset, int size)
    {
        var data = new byte[size];
        rom.Position = offset;
        rom.ReadExactly(data);
        return data;
    }

    private static Color DecodeColor(ushort raw)
    {
        static int Expand(int component) => (component << 3) | (component >> 2);
        return Color.FromArgb(Expand(raw & 31), Expand((raw >> 5) & 31), Expand((raw >> 10) & 31));
    }

    private static byte[] Decompress(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0x10) throw new InvalidDataException("Invalid Ruby map compression.");
        int length = data[1] | (data[2] << 8) | (data[3] << 16);
        if (length is < 1 or > 16_384) throw new InvalidDataException("Invalid Ruby map size.");
        var result = new byte[length];
        int read = 4;
        int write = 0;
        byte Read()
        {
            if (read == data.Length) throw new InvalidDataException("Truncated Ruby map.");
            return data[read++];
        }

        while (write < result.Length)
        {
            byte flags = Read();
            for (int bit = 7; bit >= 0 && write < result.Length; bit--)
            {
                if ((flags & (1 << bit)) == 0)
                {
                    result[write++] = Read();
                    continue;
                }
                byte first = Read();
                int count = (first >> 4) + 3;
                int offset = ((first & 15) << 8) + Read() + 1;
                if (offset > write) throw new InvalidDataException("Invalid Ruby map reference.");
                while (count-- > 0 && write < result.Length)
                {
                    result[write] = result[write - offset];
                    write++;
                }
            }
        }
        return result;
    }
}
