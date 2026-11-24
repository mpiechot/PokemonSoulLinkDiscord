using System.Buffers.Binary;

namespace SoulLink;

public sealed record RubyLevelMove(int Level, int Move);
public sealed record RubyEvolution(int Species, string Requirement);
public sealed record PokemonRomDetails(PokemonStats BaseStats, RubyLevelMove[] Moves, RubyEvolution[] Evolutions)
{
    public bool IsValid() => this.BaseStats is
    {
        Hp: > 0 and <= 255, Attack: > 0 and <= 255, Defense: > 0 and <= 255,
        Speed: > 0 and <= 255, SpecialAttack: > 0 and <= 255, SpecialDefense: > 0 and <= 255
    } &&
        this.Moves is { Length: <= 512 } && this.Moves.All(move => move is { Level: >= 0 and <= 100, Move: > 0 and < 355 }) &&
        this.Evolutions is { Length: <= 5 } && this.Evolutions.All(evolution => evolution is { Requirement.Length: > 0 and <= 128 } &&
            SpeciesCatalog.IsValid(evolution.Species) && !evolution.Requirement.Any(char.IsControl));
}

/// <summary>Reads the installed German Ruby cartridge tables, including Randomizer changes.</summary>
public static class RubyRomDetails
{
    // The two supported AXVD revisions use the same offsets. See docs/soullink/mgba.md.
    private const int BaseStats = 0x20BBE8;
    private const int Evolutions = 0x210B38;
    private const int Learnsets = 0x214B98;
    private const int Items = 0x3D13DC;
    private const int RomAddress = 0x08000000;

    public static PokemonRomDetails Read(Stream rom, int species)
    {
        if (!SpeciesCatalog.IsValid(species)) throw new InvalidDataException("Unbekannte Pokémon-Art.");
        Span<byte> header = stackalloc byte[29];
        ReadAt(rom, 0xA0, header);
        if (!header[..16].SequenceEqual("POKEMON RUBYAXVD"u8) || header[28] > 1)
            throw new InvalidDataException("Für diese ROM sind keine Pokémon-Tabellen hinterlegt.");

        Span<byte> stats = stackalloc byte[6];
        ReadAt(rom, BaseStats + 28 * species, stats);
        if (stats.Contains((byte)0)) throw new InvalidDataException("Die Pokémon-Werte in der ROM konnten nicht gelesen werden.");
        var bases = new PokemonStats(stats[0], stats[1], stats[2], stats[3], stats[4], stats[5]);
        return new PokemonRomDetails(bases, ReadMoves(rom, species), ReadEvolutions(rom, species));
    }

    private static RubyLevelMove[] ReadMoves(Stream rom, int species)
    {
        Span<byte> pointer = stackalloc byte[4];
        ReadAt(rom, Learnsets + 4 * species, pointer);
        long start = (long)BinaryPrimitives.ReadUInt32LittleEndian(pointer) - RomAddress;
        Span<byte> word = stackalloc byte[2];
        var moves = new List<RubyLevelMove>();
        for (int i = 0; i < 512; i++)
        {
            ReadAt(rom, start + i * 2, word);
            ushort entry = BinaryPrimitives.ReadUInt16LittleEndian(word);
            if (entry == ushort.MaxValue) return moves.ToArray();

            int move = entry & 0x1FF;
            int level = entry >> 9;
            if (RubyNames.Move(move) is null || level > 100)
                throw new InvalidDataException("Die Attackenliste dieser ROM hat ein unbekanntes Format.");
            moves.Add(new RubyLevelMove(level, move));
        }
        throw new InvalidDataException("Die Attackenliste hat keinen gültigen Abschluss.");
    }

    private static RubyEvolution[] ReadEvolutions(Stream rom, int species)
    {
        Span<byte> data = stackalloc byte[40];
        ReadAt(rom, Evolutions + 40 * species, data);
        var evolutions = new List<RubyEvolution>();
        for (int offset = 0; offset < data.Length; offset += 8)
        {
            int method = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..]);
            if (method == 0) continue;
            int parameter = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 2)..]);
            int target = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 4)..]);
            if (!SpeciesCatalog.IsValid(target)) throw new InvalidDataException("Ungültiges Entwicklungsziel in der ROM.");
            string requirement = method switch
            {
                1 => "Levelaufstieg mit hoher Freundschaft",
                2 => "Levelaufstieg mit hoher Freundschaft am Tag",
                3 => "Levelaufstieg mit hoher Freundschaft in der Nacht",
                4 => $"Ab Level {parameter}",
                5 => "Durch Tausch",
                6 => $"Tausch mit {ReadItem(rom, parameter)} als getragenem Item",
                7 => $"Mit {ReadItem(rom, parameter)}",
                8 => $"Level {parameter} bei höherem Angriff als Verteidigung",
                9 => $"Level {parameter} bei gleichem Angriff und gleicher Verteidigung",
                10 => $"Level {parameter} bei niedrigerem Angriff als Verteidigung",
                11 or 12 => $"Ab Level {parameter}, abhängig vom Persönlichkeitswert",
                13 => $"Ab Level {parameter}",
                14 => $"Ab Level {parameter} mit einem freien Teamplatz",
                15 => $"Levelaufstieg ab Schönheit {parameter}",
                _ => "Entwicklungsbedingung unbekannt",
            };
            evolutions.Add(new RubyEvolution(target, requirement));
        }
        return evolutions.ToArray();
    }

    private static string ReadItem(Stream rom, int id)
    {
        if (id is > 0 and < 349)
        {
            Span<byte> name = stackalloc byte[14];
            ReadAt(rom, Items + 44 * id, name);
            string decoded = Gen3Parser.DecodeName(name);
            if (!string.IsNullOrWhiteSpace(decoded) && !decoded.Contains('�')) return decoded;
        }
        return $"Item #{id}";
    }

    private static void ReadAt(Stream rom, long offset, Span<byte> bytes)
    {
        if (offset < 0 || offset > rom.Length - bytes.Length)
            throw new InvalidDataException("Die Pokémon-Tabelle liegt außerhalb der ROM.");
        rom.Position = offset;
        rom.ReadExactly(bytes);
    }
}
