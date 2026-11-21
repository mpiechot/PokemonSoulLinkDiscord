using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;

namespace SoulLink.Player;

/// <summary>Installs and starts the bundled copy without changing the system mGBA installation.</summary>
internal sealed class PortableMgba(string home, bool preferSoftwareDisplay = false, bool isolated = false)
{
    public bool Available => typeof(PortableMgba).Assembly.GetManifestResourceInfo("SoulLink.mGBA.zip") is not null;

    internal string ExecutablePath => Path.Combine(home, "mGBA-9187", "mGBA.exe");

    public Process Start(string rom, string bridge, string? testState = null, bool softwareDisplay = false)
    {
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
        if (testState is not null)
        {
            start.ArgumentList.Add("--savestate");
            start.ArgumentList.Add(testState);
        }
        start.ArgumentList.Add("--script");
        start.ArgumentList.Add(bridge);
        start.ArgumentList.Add(this.PrepareRom(rom));
        return Process.Start(start) ?? throw new InvalidOperationException("mGBA konnte nicht gestartet werden.");
    }

    internal string PrepareRom(string original)
    {
        if (!isolated) return original;
        using var source = File.OpenRead(original);
        string directory = Path.Combine(home, "Games", Convert.ToHexString(SHA256.HashData(source)));
        string target = Path.Combine(directory, Path.GetFileName(original));
        if (File.Exists(target)) return target;
        Directory.CreateDirectory(directory);
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
