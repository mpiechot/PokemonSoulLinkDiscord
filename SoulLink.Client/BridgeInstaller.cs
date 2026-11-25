namespace SoulLink.Client;

/// <summary>Exports the embedded bridge to a stable per-user location for mGBA autorun.</summary>
public static class BridgeInstaller
{
    public static string Install(string directory, int? port = null, string? itemSession = null, string? tradeCheckpointPrefix = null)
    {
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "soullink.lua");
        const string profilePrefix = "SoulLink.GameProfile.";
        var sources = typeof(BridgeInstaller).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(profilePrefix, StringComparison.Ordinal) && name.EndsWith(".lua", StringComparison.Ordinal))
            .Select(name => $"[\"profiles.{name[profilePrefix.Length..^4]}\"] = (function()\n{ReadScript(name)}\nend)(),");
        string content = "local SOULLINK_GAMES = (function()\nlocal modules = {\n" + string.Join('\n', sources) +
            "\n}\nlocal function require(name) return assert(modules[name], name) end\n" + ReadScript("SoulLink.GameRegistry.lua") +
            "\nend)()\n" + ReadScript("SoulLink.ReadOnlyBridge.lua");
        if (port is not null)
        {
            if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
            content = FormattableString.Invariant($"SOULLINK_PORT = {port}\n") + content;
        }
        if (itemSession is not null)
        {
            if (itemSession.Length != 32 || !itemSession.All(Uri.IsHexDigit))
                throw new ArgumentException("Invalid local item session.", nameof(itemSession));
            string features = "local SOULLINK_FEATURES = { ruby = {\n" +
                $"overworld = (function()\n{ReadScript("SoulLink.RubyOverworld.lua")}\nend)(),\n";
            if (tradeCheckpointPrefix is not null)
            {
                string pathBytes = string.Concat(System.Text.Encoding.UTF8.GetBytes(tradeCheckpointPrefix)
                    .Select(value => FormattableString.Invariant($"\\{value:D3}")));
                features = $"local SOULLINK_TRADE_CHECKPOINT = \"{pathBytes}\"\n" + features +
                    $"trade = (function()\n{ReadScript("SoulLink.RubyTrade.lua")}\nend)(),\n";
            }
            content = $"SOULLINK_ITEM_SESSION = \"{itemSession}\"\n" + features + "} }\n" + content;
        }
        if (!File.Exists(path) || File.ReadAllText(path) != content)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, content, new System.Text.UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        return path;
    }

    private static string ReadScript(string name)
    {
        using var source = typeof(BridgeInstaller).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"Ein Teil der mGBA-Brücke fehlt ({name}).");
        using var reader = new StreamReader(source);
        return reader.ReadToEnd();
    }
}
