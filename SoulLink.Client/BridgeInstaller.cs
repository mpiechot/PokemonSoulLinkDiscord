namespace SoulLink.Client;

/// <summary>Exports the embedded bridge to a stable per-user location for mGBA autorun.</summary>
public static class BridgeInstaller
{
    public static string Install(string directory, int? port = null, string? itemSession = null, string? tradeCheckpointPrefix = null)
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
        if (itemSession is not null)
        {
            if (itemSession.Length != 32 || !itemSession.All(Uri.IsHexDigit))
                throw new ArgumentException("Invalid local item session.", nameof(itemSession));
            using var renderer = typeof(BridgeInstaller).Assembly.GetManifestResourceStream("SoulLink.OverworldBridge.lua")
                ?? throw new InvalidOperationException("Die mGBA-Mitspieleranzeige fehlt in dieser Anwendung.");
            using var rendererReader = new StreamReader(renderer);
            content = $"SOULLINK_ITEM_SESSION = \"{itemSession}\"\nlocal SOULLINK_OVERWORLD = (function()\n{rendererReader.ReadToEnd()}\nend)()\n" + content;
            if (tradeCheckpointPrefix is not null)
            {
                using var trade = typeof(BridgeInstaller).Assembly.GetManifestResourceStream("SoulLink.TradeBridge.lua")
                    ?? throw new InvalidOperationException("Die Poké-Tausch-Brücke fehlt in dieser Anwendung.");
                using var tradeReader = new StreamReader(trade);
                string pathBytes = string.Concat(System.Text.Encoding.UTF8.GetBytes(tradeCheckpointPrefix)
                    .Select(value => FormattableString.Invariant($"\\{value:D3}")));
                content = $"local SOULLINK_TRADE_CHECKPOINT = \"{pathBytes}\"\nlocal SOULLINK_TRADE = (function()\n{tradeReader.ReadToEnd()}\nend)()\n" + content;
            }
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
