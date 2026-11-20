using System.Diagnostics;
using System.IO.Compression;

namespace SoulLink.Player;

/// <summary>Installs and starts the bundled copy without changing the system mGBA installation.</summary>
internal sealed class PortableMgba(string home)
{
    public bool Available => typeof(PortableMgba).Assembly.GetManifestResourceInfo("SoulLink.mGBA.zip") is not null;

    public Process Start(string rom, string bridge, string? testState = null)
    {
        string executable = this.Install();
        var start = new ProcessStartInfo(executable)
        {
            WorkingDirectory = Path.GetDirectoryName(executable)!,
            UseShellExecute = false,
        };
        if (testState is not null)
        {
            start.ArgumentList.Add("--savestate");
            start.ArgumentList.Add(testState);
        }
        start.ArgumentList.Add("--script");
        start.ArgumentList.Add(bridge);
        start.ArgumentList.Add(rom);
        return Process.Start(start) ?? throw new InvalidOperationException("mGBA konnte nicht gestartet werden.");
    }

    public static bool IsAlreadyRunning()
    {
        var processes = Process.GetProcessesByName("mGBA");
        foreach (var process in processes) process.Dispose();
        return processes.Length != 0;
    }

    private string Install()
    {
        string directory = Path.Combine(home, "mGBA-9187");
        string executable = Path.Combine(directory, "mGBA.exe");
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
