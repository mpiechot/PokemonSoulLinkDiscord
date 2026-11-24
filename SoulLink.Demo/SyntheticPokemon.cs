using System.Buffers.Binary;
using SoulLink;

namespace SoulLink.Demo;

/// <summary>Synthetic, source-defined data. This code neither reads nor distributes real saves.</summary>
public static class SyntheticPokemon
{
    private static readonly string[] Orders =
    [
        "GAEM", "GAME", "GEAM", "GEMA", "GMAE", "GMEA",
        "AGEM", "AGME", "AEGM", "AEMG", "AMGE", "AMEG",
        "EGAM", "EGMA", "EAGM", "EAMG", "EMGA", "EMAG",
        "MGAE", "MGEA", "MAGE", "MAEG", "MEGA", "MEAG",
    ];

    public static byte[] Bytes(uint pid, int species, int metArea = 16, int hp = 20, uint experience = 125, bool box = false, bool egg = false)
    {
        byte[] result = new byte[box ? 80 : 100];
        const uint ot = 0x12345678;
        BinaryPrimitives.WriteUInt32LittleEndian(result, pid);
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), ot);
        result.AsSpan(8, 10).Fill(0xFF);
        result[8] = 0xBB;
        result[18] = 5;
        result[19] = 2;
        byte[] plain = new byte[48];
        string order = Orders[pid % 24];
        int g = order.IndexOf('G') * 12;
        int m = order.IndexOf('M') * 12;
        BinaryPrimitives.WriteUInt16LittleEndian(plain.AsSpan(g), (ushort)species);
        BinaryPrimitives.WriteUInt32LittleEndian(plain.AsSpan(g + 4), experience);
        plain[m + 1] = (byte)metArea;
        if (egg) BinaryPrimitives.WriteUInt32LittleEndian(plain.AsSpan(m + 4), 1u << 30);
        ushort checksum = 0;
        for (int i = 0; i < 48; i += 2) checksum = unchecked((ushort)(checksum + BinaryPrimitives.ReadUInt16LittleEndian(plain.AsSpan(i))));
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(28), checksum);
        for (int i = 0; i < 48; i += 4)
            BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(32 + i), BinaryPrimitives.ReadUInt32LittleEndian(plain.AsSpan(i)) ^ pid ^ ot);
        if (!box)
        {
            result[84] = 5;
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(86), (ushort)hp);
            BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(88), 20);
        }
        return result;
    }

    public static RawPokemon Raw(uint pid, int species, int slot = 0, bool box = false, int hp = 20, int area = 16, uint experience = 125) =>
        new(Convert.ToHexString(Bytes(pid, species, area, hp, experience, box)), box ? Placement.Box : Placement.Party, slot, box ? 0 : null);

    public static BridgeFrame Frame(long frame, params RawPokemon[] pokemon) => new()
    {
        GameCode = "AXVD", LayoutId = "synthetic-fixture-v1", Epoch = "demo", Frame = frame,
        PlaySeconds = frame, Area = 16, MapGroup = 0, MapNumber = 16, CompleteInventory = true,
        Pokemon = pokemon.ToList(),
    };
}
