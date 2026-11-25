using SoulLink;

namespace SoulLink.Player;

/// <summary>Remembers the locally chosen ROM for one prepared player profile.</summary>
internal sealed class RomSelection(string settingsPath)
{
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
        string extension = Path.GetExtension(path);
        if (!GameProfiles.SupportsRomExtension(extension)) return false;
        using var stream = File.OpenRead(path);
        return GameProfiles.Identify(stream)?.RomExtension.Equals(extension, StringComparison.OrdinalIgnoreCase) == true;
    }
}
