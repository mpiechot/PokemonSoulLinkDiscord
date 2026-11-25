using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace SoulLink.Player;

/// <summary>Starts the bundled or explicitly selected portable mGBA.</summary>
internal sealed class PortableMgba(string home, bool preferSoftwareDisplay = false, bool isolated = false)
{
    private readonly string externalSelection = Path.Combine(home, "mgba-path.txt");

    public bool Bundled => typeof(PortableMgba).Assembly.GetManifestResourceInfo("SoulLink.mGBA.zip") is not null;

    public bool Available => this.Bundled || this.ExecutablePath is not null;

    internal string? ExecutablePath => this.Bundled ? Path.Combine(home, "mGBA-9187", "mGBA.exe") : this.ReadExternalSelection();

    public void SelectExternal(string path)
    {
        if (!IsPortable(path)) throw new InvalidDataException("Wähle mGBA.exe aus einem entpackten portablen mGBA-Archiv.");
        string temporary = this.externalSelection + ".tmp";
        File.WriteAllText(temporary, Path.GetFullPath(path));
        File.Move(temporary, this.externalSelection, overwrite: true);
    }

    private string? ReadExternalSelection()
    {
        string? path = File.Exists(this.externalSelection) ? File.ReadAllText(this.externalSelection) : null;
        return IsPortable(path) ? path : null;
    }

    private static bool IsPortable(string? path) => !string.IsNullOrEmpty(path) && Path.IsPathFullyQualified(path) &&
        string.Equals(Path.GetFileName(path), "mGBA.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path) &&
        File.Exists(Path.Combine(Path.GetDirectoryName(path)!, "portable.ini"));

    public Process Start(string rom, string bridge, string? initialState = null, bool softwareDisplay = false, bool resume = false)
    {
        string gameRom = this.PrepareRom(rom);
        if (resume)
        {
            initialState = this.LatestState(gameRom) ?? throw new InvalidDataException("Für diesen Run gibt es keinen gespeicherten Spielstand.");
        }
        string executable = this.Install();
        string directory = Path.GetDirectoryName(executable)!;
        if (this.Bundled && (softwareDisplay || preferSoftwareDisplay))
        {
            // Hosted Windows workers may not expose an accelerated OpenGL context.
            File.WriteAllText(Path.Combine(directory, "qt.ini"), "[General]\ndisplayDriver=0\n");
        }
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = directory,
            UseShellExecute = false,
        };
        if (initialState is not null)
        {
            start.ArgumentList.Add("--savestate");
            start.ArgumentList.Add(initialState);
        }
        start.ArgumentList.Add("--script");
        start.ArgumentList.Add(bridge);
        start.ArgumentList.Add(gameRom);
        return Process.Start(start) ?? throw new InvalidOperationException("mGBA konnte nicht gestartet werden.");
    }

    internal bool HasResumeState(string? rom)
    {
        if (!File.Exists(rom)) return false;
        try
        {
            if (!isolated) return this.LatestState(rom!) is not null;
            using var source = File.OpenRead(rom!);
            string copy = this.TestRomPath(rom!, source);
            return File.Exists(copy) && this.LatestState(copy) is not null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    internal string? LatestState(string rom)
    {
        string directory = this.StateDirectory() ?? Path.GetDirectoryName(Path.GetFullPath(rom))!;
        string stem = Path.GetFileNameWithoutExtension(rom);
        var slots = new[] { Path.Combine(directory, stem + ".ss1"), Path.Combine(directory, stem + ".ss2") };
        string prefix = Path.GetFileName(this.TradeCheckpointPrefix(rom));
        var checkpoints = Directory.Exists(directory) ? Directory.EnumerateFiles(directory, "*.ss").Where(path =>
        {
            string name = Path.GetFileName(path);
            return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && name.Length >= prefix.Length + 32 &&
                Guid.TryParseExact(name.AsSpan(prefix.Length, 32), "N", out _) &&
                (name.EndsWith("-before.ss", StringComparison.Ordinal) || name.EndsWith("-after.ss", StringComparison.Ordinal));
        }) : [];
        return slots.Concat(checkpoints).Select(path => new FileInfo(path)).Where(file => file.Exists && file.Length > 0)
            .OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault()?.FullName;
    }

    internal string TradeCheckpointPrefix(string rom) =>
        Path.Combine(this.StateDirectory() ?? Path.GetDirectoryName(Path.GetFullPath(rom))!, Path.GetFileName(rom) + ".soullink-trade-");

    private string? StateDirectory()
    {
        if (this.ExecutablePath is not { } executable) return null;
        string portableDirectory = Path.GetDirectoryName(executable)!;
        string configuration = Path.Combine(portableDirectory, "config.ini");
        if (!File.Exists(configuration)) return null;

        string section = "";
        string? general = null;
        string? qt = null;
        foreach (string line in File.ReadLines(configuration))
        {
            string entry = line.Trim();
            if (entry.StartsWith('[') && entry.EndsWith(']')) section = entry[1..^1];
            int separator = entry.IndexOf('=');
            if (separator < 0 || !entry.AsSpan(0, separator).Trim().SequenceEqual("savestatePath")) continue;
            if (section == "ports.qt") qt = entry[(separator + 1)..].Trim();
            if (section.Length == 0) general = entry[(separator + 1)..].Trim();
        }

        string? configured = qt ?? general;
        if (string.IsNullOrEmpty(configured)) return null;
        string path = Path.GetFullPath(configured, portableDirectory);
        return Directory.Exists(path) ? path : null;
    }

    private string TestRomPath(string original, Stream source) =>
        Path.Combine(home, "Games", Convert.ToHexString(SHA256.HashData(source)), Path.GetFileName(original));

    internal string PrepareRom(string original)
    {
        if (!isolated) return original;
        using var source = File.OpenRead(original);
        string target = this.TestRomPath(original, source);
        if (File.Exists(target)) return target;
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        string temporary = target + ".tmp";
        try
        {
            source.Position = 0;
            using (var copy = File.Create(temporary)) source.CopyTo(copy);
            File.Move(temporary, target);
        }
        finally { File.Delete(temporary); }
        return target;
    }

    public bool IsAlreadyRunning()
    {
        if (isolated && this.Bundled) return false;
        string testDirectory = Path.Combine(home, "TestPlayers") + Path.DirectorySeparatorChar;
        string? selected = this.ExecutablePath;
        var processes = Process.GetProcessesByName("mGBA");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (process.HasExited) continue;
                    string? running = process.MainModule?.FileName;
                    if (isolated ? string.Equals(running, selected, StringComparison.OrdinalIgnoreCase) :
                        running?.StartsWith(testDirectory, StringComparison.OrdinalIgnoreCase) != true)
                        return true;
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { return true; }
            }
            return false;
        }
        finally
        {
            foreach (var process in processes) process.Dispose();
        }
    }

    private string Install()
    {
        string executable = this.ExecutablePath ?? throw new InvalidDataException("Wähle zuerst eine portable mGBA-Version mit Lua-Unterstützung aus.");
        if (!this.Bundled) return executable;
        string directory = Path.GetDirectoryName(executable)!;
        string installed = Path.Combine(directory, ".ready");
        if (File.Exists(installed) && File.Exists(executable)) return executable;

        using var archive = typeof(PortableMgba).Assembly.GetManifestResourceStream("SoulLink.mGBA.zip")
            ?? throw new InvalidOperationException("mGBA fehlt im Spielerpaket.");
        using var zip = new ZipArchive(archive, ZipArchiveMode.Read);
        zip.ExtractToDirectory(directory, overwriteFiles: true);
        if (!File.Exists(Path.Combine(directory, "portable.ini")) || !File.Exists(executable))
        {
            throw new InvalidDataException("Das portable mGBA-Paket ist unvollständig.");
        }
        File.WriteAllText(installed, "");
        return executable;
    }
}
