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

        if ((bytes[19] & 1) != 0)
        {
            throw new InvalidDataException("Schlechtes Ei (Bad-Egg-Markierung).");
        }

        if (checksum != BinaryPrimitives.ReadUInt16LittleEndian(bytes[28..]))
        {
            throw new InvalidDataException("Ungültige Pokémon-Prüfsumme.");
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
            Stats = maxHp is int maximum ? new PokemonStats(maximum,
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[90..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[92..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[94..]), BinaryPrimitives.ReadUInt16LittleEndian(bytes[96..]),
                BinaryPrimitives.ReadUInt16LittleEndian(bytes[98..])) : null,
            Status = container == Placement.Party ? BinaryPrimitives.ReadUInt32LittleEndian(bytes[80..]) : 0,
            IsEgg = (BinaryPrimitives.ReadUInt32LittleEndian(misc[4..]) & (1u << 30)) != 0,
            IsShiny = ((ot & 65535) ^ (ot >> 16) ^ (pid & 65535) ^ (pid >> 16)) < 8,
            MetArea = misc[1],
            Container = container,
            Slot = slot,
            Box = box,
        };
    }

    internal static PokemonState ParseTrade(RawPokemon raw)
    {
        if (raw.Container == Placement.Party ? raw.Slot is < 0 or >= 6 || raw.Box is not null :
            raw.Container != Placement.Box || raw.Slot is < 0 or >= 30 || raw.Box is not (>= 0 and < 14))
            throw new InvalidDataException("Der Pokémon-Platz ist ungültig.");
        int length = raw.Container == Placement.Party ? 200 : 160;
        if (raw.Hex is null || raw.Hex.Length != length || !raw.Hex.All(Uri.IsHexDigit))
            throw new InvalidDataException("Der Pokémon-Datensatz ist ungültig.");
        byte[] data = Convert.FromHexString(raw.Hex);
        if (Parse(data, raw.Container, raw.Slot, raw.Box) is not { IsEgg: false } pokemon || pokemon.Hp == 0)
            throw new InvalidDataException("Dieses Pokémon kann nicht getauscht werden.");
        foreach (var name in new[] { data.AsSpan(8, 10).ToArray(), data.AsSpan(20, 7).ToArray() })
        {
            if (name.TakeWhile(value => value != 0xFF).Any(value => value >= 0xFA))
                throw new InvalidDataException("Ein Name enthält ungültige Spielzeichen.");
        }
        uint pid = BinaryPrimitives.ReadUInt32LittleEndian(data);
        uint key = pid ^ BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(4));
        string order = Orders[pid % 24];
        ushort item = (ushort)((BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(32 + order.IndexOf('G') * 12)) ^ key) >> 16);
        if (item > 348 || item is >= 121 and <= 132 || (data.Length == 100 && data[85] != 0xFF))
            throw new InvalidDataException("Pokémon mit Briefen oder ungültigen Items können hier nicht getauscht werden.");
        int moves = 32 + order.IndexOf('A') * 12;
        for (int i = 0; i < 2; i++)
        {
            uint pair = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(moves + i * 4)) ^ key;
            if ((pair & 0xFFFF) > 354 || (pair >> 16) > 354)
                throw new InvalidDataException("Das Pokémon enthält eine ungültige Attacke.");
        }
        if (pokemon.Experience > 1_640_000) throw new InvalidDataException("Das Pokémon enthält ungültige Erfahrungspunkte.");
        return pokemon;
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
                0x06 => 'É',
                0x15 => 'ß',
                0x1B => 'é',
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
                0xB4 => '\'',
                _ => '�',
            });
        }

        return result.ToString().Trim();
    }
}
