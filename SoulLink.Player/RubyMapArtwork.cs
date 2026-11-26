using System.Buffers.Binary;
using SoulLink;

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
    private const int RegionEntries = 0x3F3770;
    private const int LandmarkLists = 0x40794C;

    public static RubyMapLabels? ReadLabels(string path)
    {
        try
        {
            using var rom = File.OpenRead(path);
            if (rom.Length < LandmarkLists + 0x170 || GameProfiles.Identify(rom)?.Edition != "ruby") return null;
            var names = new Dictionary<int, string>();
            var landmarks = new Dictionary<(int Area, int Cell), RubyMapLandmark[]>();
            var regions = ReadAt(rom, RegionEntries, 0x2C0);
            for (int area = 0; area < regions.Length / 8; area++)
            {
                if (ReadName(ReadPointer(regions.AsSpan(area * 8 + 4)), rom) is { } name) names.Add(area, name);
            }

            var lists = ReadAt(rom, LandmarkLists, 0x170);
            int lastArea = -1;
            for (int index = 0; index < lists.Length; index += 8)
            {
                int area = lists[index];
                if (area >= regions.Length / 8) break;
                // Ruby stops looking when it leaves an area's contiguous landmark entries.
                if (area < lastArea) continue;
                lastArea = area;
                int list = ReadPointer(lists.AsSpan(index + 4));
                if (list < 0 || list > rom.Length - 16) continue;
                var points = new List<RubyMapLandmark>();
                var pointers = ReadAt(rom, list, 16);
                for (int i = 0; i < pointers.Length; i += 4)
                {
                    int landmark = ReadPointer(pointers.AsSpan(i));
                    if (landmark < 0 || landmark > rom.Length - 8) break;
                    var definition = ReadAt(rom, landmark, 8);
                    if (ReadName(ReadPointer(definition), rom) is { } name)
                        points.Add(new RubyMapLandmark(name, BinaryPrimitives.ReadUInt16LittleEndian(definition.AsSpan(4))));
                }
                landmarks.TryAdd((area, lists[index + 1]), points.ToArray());
            }

            return new RubyMapLabels(names, landmarks);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { return null; }
    }

    private static int ReadPointer(ReadOnlySpan<byte> bytes) => BinaryPrimitives.ReadInt32LittleEndian(bytes) - 0x08000000;

    private static string? ReadName(int offset, FileStream rom)
    {
        if (offset < 0 || offset > rom.Length - 64) return null;
        var bytes = ReadAt(rom, offset, 64);
        if (Array.IndexOf(bytes, (byte)0xFF) <= 0) return null;
        string name = RubyNames.DecodePokeNavText(bytes);
        return name.Length > 0 && !name.Contains('�') ? name : null;
    }

    public static Bitmap? Read(string path)
    {
        try
        {
            using var rom = File.OpenRead(path);
            if (rom.Length < TileMapEnd || GameProfiles.Identify(rom)?.Edition != "ruby") return null;
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
        using var input = new MemoryStream(data, writable: false);
        return ReadLz77(input, 16_384);
    }

    internal static byte[] ReadLz77(Stream input, int limit)
    {
        if (Read() != 0x10) throw new InvalidDataException("Invalid Ruby LZ77 compression.");
        int length = Read() | (Read() << 8) | (Read() << 16);
        if (length < 1 || length > limit) throw new InvalidDataException("Invalid Ruby LZ77 size.");
        var result = new byte[length];
        int write = 0;
        byte Read()
        {
            int value = input.ReadByte();
            if (value < 0) throw new InvalidDataException("Truncated Ruby LZ77 data.");
            return (byte)value;
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
                if (offset > write) throw new InvalidDataException("Invalid Ruby LZ77 reference.");
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

internal sealed record RubyMapLandmark(string Name, int Flag);

internal sealed record RubyMapLabels(IReadOnlyDictionary<int, string> Names,
    IReadOnlyDictionary<(int Area, int Cell), RubyMapLandmark[]> Landmarks)
{
    public static bool IsVisible(int area, byte[]? flags) => area switch
    {
        58 => FlagSet(flags, 0x848),
        73 => FlagSet(flags, 0x849),
        _ => true,
    };

    public IEnumerable<string> LandmarkNames(int area, int cell, byte[]? flags) =>
        (this.Landmarks.GetValueOrDefault((area, cell)) ?? []).Where(point => point.Flag == 0xFFFF || FlagSet(flags, point.Flag))
            .Select(point => point.Name);

    private static bool FlagSet(byte[]? flags, int flag)
    {
        int bit = flag - 0x800;
        return flags is not null && bit >= 0 && bit / 8 < flags.Length && (flags[bit / 8] & (1 << (bit % 8))) != 0;
    }
}
