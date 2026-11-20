using System.Buffers.Binary;
using System.Text;

namespace SoulLink;

/// <summary>Parses source-defined Gen III structures; it never accesses emulator memory.</summary>
public static class Gen3Parser
{
    private static readonly string[] Orders =
    [
        "GAEM", "GAME", "GEAM", "GEMA", "GMAE", "GMEA",
        "AGEM", "AGME", "AEGM", "AEMG", "AMGE", "AMEG",
        "EGAM", "EGMA", "EAGM", "EAMG", "EMGA", "EMAG",
        "MGAE", "MGEA", "MAGE", "MAEG", "MEGA", "MEAG",
    ];

    public static PokemonState? Parse(ReadOnlySpan<byte> bytes, Placement container, int slot, int? box = null)
    {
        int size = container == Placement.Party ? 100 : 80;
        if (bytes.Length != size)
        {
            throw new InvalidDataException($"Expected {size} bytes for {container} Pokémon.");
        }

        if (bytes.IndexOfAnyExcept((byte)0) < 0 || (bytes[19] & 2) == 0)
        {
            return null;
        }

        uint pid = BinaryPrimitives.ReadUInt32LittleEndian(bytes);
        uint ot = BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..]);
        Span<byte> plain = stackalloc byte[48];
        for (int i = 0; i < 48; i += 4)
        {
            uint encrypted = BinaryPrimitives.ReadUInt32LittleEndian(bytes[(32 + i)..]);
            BinaryPrimitives.WriteUInt32LittleEndian(plain[i..], encrypted ^ pid ^ ot);
        }

        ushort checksum = 0;
        for (int i = 0; i < 48; i += 2)
        {
            checksum = unchecked((ushort)(checksum + BinaryPrimitives.ReadUInt16LittleEndian(plain[i..])));
        }

        if (checksum != BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]) || (bytes[19] & 1) != 0)
        {
            throw new InvalidDataException("Invalid Pokémon checksum or Bad Egg flag; observation discarded.");
        }

        string order = Orders[pid % 24];
        ReadOnlySpan<byte> growth = plain.Slice(order.IndexOf('G') * 12, 12);
        ReadOnlySpan<byte> misc = plain.Slice(order.IndexOf('M') * 12, 12);
        int species = BinaryPrimitives.ReadUInt16LittleEndian(growth);
        if (!SpeciesCatalog.IsValid(species))
        {
            throw new InvalidDataException($"Unsupported Gen III species value {species}.");
        }

        int? hp = container == Placement.Party ? BinaryPrimitives.ReadUInt16LittleEndian(bytes[86..]) : null;
        int? maxHp = container == Placement.Party ? BinaryPrimitives.ReadUInt16LittleEndian(bytes[88..]) : null;
        int? level = container == Placement.Party ? bytes[84] : null;
        if (hp > maxHp || maxHp == 0 || level is < 1 or > 100)
        {
            throw new InvalidDataException("Invalid party stats; wait for a stable game state.");
        }

        return new PokemonState
        {
            Identity = $"{ot:X8}:{pid:X8}",
            Species = species,
            Nickname = DecodeName(bytes.Slice(8, 10)),
            Experience = BinaryPrimitives.ReadUInt32LittleEndian(growth[4..]),
            Level = level,
            Hp = hp,
            MaxHp = maxHp,
            Status = container == Placement.Party ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[80..]) : 0,
            IsEgg = (BinaryPrimitives.ReadUInt32LittleEndian(misc[4..]) & (1u << 30)) != 0,
            IsShiny = ((ot & 65535) ^ (ot >> 16) ^ (pid & 65535) ^ (pid >> 16)) < 8,
            MetArea = misc[1],
            Container = container,
            Slot = slot,
            Box = box,
        };
    }

    internal static string DecodeName(ReadOnlySpan<byte> bytes)
    {
        var result = new StringBuilder();
        foreach (byte value in bytes)
        {
            if (value == 255) break;
            result.Append(value switch
            {
                0 => ' ',
                >= 0xA1 and <= 0xAA => (char)('0' + value - 0xA1),
                >= 0xBB and <= 0xD4 => (char)('A' + value - 0xBB),
                >= 0xD5 and <= 0xEE => (char)('a' + value - 0xD5),
                0xF1 => 'Ä',
                0xF2 => 'Ö',
                0xF3 => 'Ü',
                0xF4 => 'ä',
                0xF5 => 'ö',
                0xF6 => 'ü',
                0xAB => '!',
                0xAC => '?',
                0xAD => '.',
                0xAE => '-',
                _ => '�',
            });
        }

        return result.ToString().Trim();
    }
}
