using System.Security.Cryptography;
using System.Text;
using SoulLink;

namespace SoulLink.Client;

public sealed class PlayerProfileLibrary(string home)
{
    private readonly string directory = Path.Combine(home, "Profiles");

    public PlayerProfile[] List()
    {
        if (!Directory.Exists(this.directory)) return [];
        return Directory.EnumerateFiles(this.directory, "*.json")
            .Select(path => Guid.TryParseExact(Path.GetFileNameWithoutExtension(path), "N", out Guid id) ? this.Read(id) : null)
            .OfType<PlayerProfile>().OrderBy(p => p.Label ?? p.Server, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public PlayerProfile? Read(Guid id)
    {
        try
        {
            if (id == Guid.Empty) return null;
            string path = this.ProfilePath(id);
            if (!File.Exists(path)) return null;
            var profile = Protocol.Deserialize<PlayerProfile>(File.ReadAllText(path));
            PlayerBundle.Validate(profile);
            return profile.Id == id ? profile : null;
        }
        catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException) { return null; }
    }

    public PlayerProfile? Current()
    {
        string path = this.CurrentPath;
        return File.Exists(path) && Guid.TryParse(File.ReadAllText(path), out Guid id) ? this.Read(id) : null;
    }

    public PlayerProfile? OpenForStartup(PlayerProfile? bundled, Guid? selection)
    {
        if (selection is Guid id)
        {
            var selected = id == Guid.Empty ? null : this.Read(id) ?? throw new InvalidDataException("Das Run-Profil fehlt.");
            this.Choose(id);
            return selected;
        }

        if (bundled is not null && this.Read(bundled.Id) is null) this.Remember(bundled);
        if (File.Exists(this.CurrentPath) || bundled is null) return this.Current();
        this.Choose(bundled.Id);
        return bundled;
    }

    public void Remember(PlayerProfile profile)
    {
        PlayerBundle.Validate(profile);
        string path = this.ProfilePath(profile.Id);
        WriteFile(path, Protocol.Serialize(profile));
    }

    public void Choose(Guid id)
    {
        if (id != Guid.Empty && this.Read(id) is null) throw new InvalidDataException("Das Run-Profil fehlt.");
        WriteFile(this.CurrentPath, id.ToString("N"));
    }

    public PlayerProfile AddInvitation(string server, string invitation, string label)
    {
        server = server.Trim().TrimEnd('/');
        invitation = invitation.Trim();
        PlayerBundle.Validate(new PlayerProfile(Guid.NewGuid(), server, invitation));
        var previous = this.List().FirstOrDefault(p => new Uri(p.Server) == new Uri(server, UriKind.Absolute) && p.Invitation == invitation);
        var profile = new PlayerProfile(previous?.Id ?? Guid.NewGuid(), server, invitation,
            previous?.Name, string.IsNullOrWhiteSpace(label) ? previous?.Label : label.Trim(), previous?.Marker);
        this.Remember(profile);
        return profile;
    }

    public static PlayerProfile ForHostedRun(string server, RunGroup group, HostRun run)
    {
        string endpoint = server.Trim().TrimEnd('/');
        byte[] key = SHA256.HashData(Encoding.UTF8.GetBytes(new Uri(endpoint).AbsoluteUri + "\n" + run.Id.ToString("N")));
        return new PlayerProfile(new Guid(key.AsSpan(0, 16)), endpoint, run.Invitation, Label: group.Name + " / " + run.Name);
    }

    private string ProfilePath(Guid id) => Path.Combine(this.directory, $"{id:N}.json");

    private string CurrentPath => Path.Combine(this.directory, "current.txt");

    private static void WriteFile(string path, string data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, data);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }
}
