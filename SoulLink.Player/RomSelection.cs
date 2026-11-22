namespace SoulLink.Player;

/// <summary>Remembers the locally chosen ROM for one prepared player profile.</summary>
internal sealed class RomSelection(string settingsPath)
{
    private static readonly RomProfile[] Supported =
    [
        new("POKEMON RUBY", "AXVD", [0, 1]),
    ];

    public static RomSelection ForProfile(string home, Guid? id) =>
        new(Path.Combine(home, $"rom-{id?.ToString("N") ?? "local"}.txt"));

    public RomSelection ForProfile(Guid id) =>
        ForProfile(Path.GetDirectoryName(settingsPath)!, id == Guid.Empty ? null : id);

    public string? Read()
    {
        return File.Exists(settingsPath) ? File.ReadAllText(settingsPath) : null;
    }

    public void Save(string path)
    {
        if (!IsSupported(path)) throw new InvalidDataException("Für diese ROM gibt es noch kein SoulLink-Spielprofil.");
        string temporary = settingsPath + ".tmp";
        File.WriteAllText(temporary, Path.GetFullPath(path));
        File.Move(temporary, settingsPath, overwrite: true);
    }

    public static bool IsSupported(string path)
    {
        if (!Path.GetExtension(path).Equals(".gba", StringComparison.OrdinalIgnoreCase)) return false;
        using var stream = File.OpenRead(path);
        Span<byte> header = stackalloc byte[0xBD];
        if (stream.Length < header.Length) return false;
        stream.ReadExactly(header);
        string title = System.Text.Encoding.ASCII.GetString(header[0xA0..0xAC]).TrimEnd('\0', ' ');
        string code = System.Text.Encoding.ASCII.GetString(header[0xAC..0xB0]);
        byte revision = header[0xBC];
        return Supported.Any(profile => profile.Title == title && profile.Code == code && profile.Revisions.Contains(revision));
    }

    private sealed record RomProfile(string Title, string Code, byte[] Revisions);
}
