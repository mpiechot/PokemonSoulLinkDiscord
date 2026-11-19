using System.Buffers.Binary;
using System.Text;
using SoulLink;

namespace SoulLink.Client;

public sealed record PlayerProfile(Guid Id, string Server, string Invitation, string Name);

/// <summary>A host may append join details to a copy of the published app; bearer tokens are never bundled.</summary>
public static class PlayerBundle
{
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SOULLINK-PLAYER-PROFILE-V1\n");
    private const int MaxProfileBytes = 8192;

    public static PlayerProfile? Read(string path)
    {
        using var stream = File.OpenRead(path);
        var profile = ReadFooter(stream);
        if (profile is null) return null;
        stream.Position = profile.Value.Offset;
        byte[] bytes = new byte[profile.Value.Length];
        stream.ReadExactly(bytes);
        var result = Protocol.Deserialize<PlayerProfile>(Encoding.UTF8.GetString(bytes));
        Validate(result);
        return result;
    }

    public static void Create(string source, string destination, PlayerProfile profile)
    {
        Validate(profile);
        if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(destination), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Wähle für die Spielerdatei einen anderen Dateinamen.");
        byte[] bytes = Encoding.UTF8.GetBytes(Protocol.Serialize(profile));
        if (bytes.Length > MaxProfileBytes) throw new InvalidDataException("Die Einladungsdaten sind zu groß.");
        using var input = File.OpenRead(source);
        long sourceLength = ReadFooter(input)?.Offset ?? input.Length;
        input.Position = 0;
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write);
        byte[] buffer = new byte[81920];
        for (long remaining = sourceLength; remaining > 0;)
        {
            int read = input.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
            if (read == 0) throw new EndOfStreamException();
            output.Write(buffer, 0, read);
            remaining -= read;
        }
        output.Write(bytes);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(length, bytes.Length);
        output.Write(length);
        output.Write(Magic);
        output.Flush(true);
    }

    public static void Validate(PlayerProfile profile)
    {
        if (profile.Id == Guid.Empty || string.IsNullOrWhiteSpace(profile.Name) || profile.Name.Length > 64 ||
            profile.Name.Any(c => char.IsControl(c) || c is '@' or '`' or '<' or '>') ||
            string.IsNullOrWhiteSpace(profile.Invitation) || profile.Invitation.Length > 128 ||
            !Uri.TryCreate(profile.Server, UriKind.Absolute, out var uri) ||
            (uri.Scheme != "https" && !(uri.Scheme == "http" && uri.IsLoopback)) ||
            uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new InvalidDataException("Bitte gültige Serveradresse, Einladung und Spielernamen angeben. Entfernte Server benötigen HTTPS.");
    }

    private static (long Offset, int Length)? ReadFooter(Stream stream)
    {
        if (stream.Length < Magic.Length + 4) return null;
        stream.Position = stream.Length - Magic.Length;
        byte[] magic = new byte[Magic.Length];
        stream.ReadExactly(magic);
        if (!magic.AsSpan().SequenceEqual(Magic)) return null;
        stream.Position -= Magic.Length + 4;
        Span<byte> bytes = stackalloc byte[4];
        stream.ReadExactly(bytes);
        int length = BinaryPrimitives.ReadInt32LittleEndian(bytes);
        long offset = stream.Length - Magic.Length - 4 - length;
        if (length is < 1 or > MaxProfileBytes || offset < 1)
            throw new InvalidDataException("Die Spielerdatei enthält beschädigte Einladungsdaten.");
        return (offset, length);
    }
}
