using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace SoulLink.Player;

/// <summary>Installs and starts the bundled copy without changing the system mGBA installation.</summary>
internal sealed class PortableMgba(string home, bool preferSoftwareDisplay = false, bool isolated = false)
{
    public bool Available => typeof(PortableMgba).Assembly.GetManifestResourceInfo("SoulLink.mGBA.zip") is not null;

    internal string ExecutablePath => Path.Combine(home, "mGBA-9187", "mGBA.exe");

    public Process Start(string rom, string bridge, string? initialState = null, bool softwareDisplay = false, bool resume = false)
    {
        string gameRom = this.PrepareRom(rom);
        if (resume)
        {
            initialState = this.LatestState(gameRom) ?? throw new InvalidDataException("Für diesen Run gibt es keinen Spielstand in Slot 1 oder 2.");
        }
        string executable = this.Install();
        string directory = Path.GetDirectoryName(executable)!;
        if (softwareDisplay || preferSoftwareDisplay)
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
        var manual = new FileInfo(Path.Combine(directory, stem + ".ss1"));
        var automatic = new FileInfo(Path.Combine(directory, stem + ".ss2"));
        bool hasManual = manual.Exists && manual.Length > 0;
        bool hasAutomatic = automatic.Exists && automatic.Length > 0;
        if (hasAutomatic && (!hasManual || automatic.LastWriteTimeUtc > manual.LastWriteTimeUtc)) return automatic.FullName;
        return hasManual ? manual.FullName : null;
    }

    private string? StateDirectory()
    {
        string portableDirectory = Path.GetDirectoryName(this.ExecutablePath)!;
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
        if (isolated) return false;
        string testDirectory = Path.Combine(home, "TestPlayers") + Path.DirectorySeparatorChar;
        var processes = Process.GetProcessesByName("mGBA");
        try
        {
            foreach (var process in processes)
            {
                try
                {
                    if (!process.HasExited && process.MainModule?.FileName.StartsWith(testDirectory, StringComparison.OrdinalIgnoreCase) != true)
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
        string executable = this.ExecutablePath;
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
