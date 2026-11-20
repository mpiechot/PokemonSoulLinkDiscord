namespace SoulLink.Client;

/// <summary>Exports the embedded read-only bridge to a stable per-user location for mGBA autorun.</summary>
public static class BridgeInstaller
{
    public static string Install(string directory, int? port = null)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "soullink.lua");
        using var source = typeof(BridgeInstaller).Assembly.GetManifestResourceStream("SoulLink.ReadOnlyBridge.lua")
            ?? throw new InvalidOperationException("Die mGBA-Brücke fehlt in dieser Anwendung.");
        using var reader = new StreamReader(source);
        string content = reader.ReadToEnd();
        if (port is not null)
        {
            if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            content = FormattableString.Invariant($"SOULLINK_PORT = {port}\n") + content;
        }
        if (!File.Exists(path) || File.ReadAllText(path) != content)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, content, new System.Text.UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        return path;
    }
}
