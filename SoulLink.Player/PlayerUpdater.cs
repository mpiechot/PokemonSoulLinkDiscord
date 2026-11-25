using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Text.Json;
using SoulLink;

namespace SoulLink.Player;

internal sealed class PlayerUpdater : IDisposable
{
    private static readonly string StageRoot = Path.Combine(Path.GetTempPath(), "SoulLink-PlayerUpdates");
    private static readonly Settings? Configuration = ReadSettings();
    private const string PackageName = "candidate.exe";
    private const string ManifestName = "manifest.json";
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false });
    private readonly PlayerReleaseClient client;

    private PlayerUpdater(Settings settings)
    {
        this.client = new PlayerReleaseClient(this.http, settings.Origin, settings.PublicKey, settings.Version);
    }

    public static PlayerUpdater? Create() => Configuration is { } settings ? new PlayerUpdater(settings) : null;

    public Task<PlayerUpdateManifest?> CheckAsync(CancellationToken cancellationToken) => this.client.CheckAsync(cancellationToken);

    public async Task<StagedPlayerUpdate> StageAsync(PlayerUpdateManifest manifest, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(StageRoot);
        if ((File.GetAttributes(StageRoot) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Der temporäre Update-Ordner ist umgeleitet.");
        string directory = Path.Combine(StageRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string candidate = Path.Combine(directory, PackageName);
            await using (var file = new FileStream(candidate, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 131072, FileOptions.Asynchronous))
            {
                await this.client.DownloadAsync(manifest, file, cancellationToken);
            }
            await File.WriteAllTextAsync(Path.Combine(directory, ManifestName), Protocol.Serialize(manifest), cancellationToken);
            return new StagedPlayerUpdate(directory, manifest);
        }
        catch
        {
            CleanStage(directory);
            throw;
        }
    }

    public static void StartInstaller(StagedPlayerUpdate update, string target, string[] restartArguments)
    {
        var settings = Configuration ?? throw new InvalidOperationException("Updates sind für diese Version nicht eingerichtet.");
        if (!IsStage(update.Directory) || update.Manifest.Verify(settings.PublicKey) <= settings.Version)
            throw new InvalidDataException("Ungültiges Update.");
        string package = Path.Combine(update.Directory, PackageName);
        // Keep a verified handle open until the same file is mapped into the installer process.
        using var file = File.OpenRead(package);
        update.Manifest.VerifyFileAsync(file).GetAwaiter().GetResult();
        using var current = Process.GetCurrentProcess();
        var arguments = new[] { "--apply-player-update", target,
            current.Id.ToString(CultureInfo.InvariantCulture), current.StartTime.ToUniversalTime().ToFileTimeUtc().ToString(CultureInfo.InvariantCulture),
            settings.Version.ToString(3) };
        StartProcess(package, [.. arguments, .. restartArguments]);
    }

    public static void Apply(string executable, string[] args)
    {
        try
        {
            if (args.Length < 5 || !int.TryParse(args[2], out int parent) || !long.TryParse(args[3], out long parentStart) ||
                !PlayerUpdateManifest.TryVersion(args[4], out var previous) || Configuration is not { } settings ||
                settings.Version <= previous || Path.GetFileName(executable) != PackageName ||
                Path.GetDirectoryName(executable) is not { } stage || !IsStage(stage))
                throw new InvalidDataException("Ungültiger Update-Aufruf.");
            var manifest = Protocol.Deserialize<PlayerUpdateManifest>(File.ReadAllText(Path.Combine(stage, ManifestName)));
            if (manifest.Verify(settings.PublicKey) != settings.Version)
                throw new InvalidDataException("Die Version passt nicht zum Update.");
            using (var package = File.OpenRead(executable)) manifest.VerifyFileAsync(package).GetAwaiter().GetResult();
            string target = Path.GetFullPath(args[1]);
            WaitForOriginal(parent, parentStart, target);
            Replace(target, executable, stage, args[5..]);
        }
        catch (Exception error) when (error is IOException or Win32Exception or InvalidDataException or
            InvalidOperationException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            ToolWindow.Notice(null, "SoulLink-Update fehlgeschlagen", error.Message);
            Environment.ExitCode = 1;
        }
    }

    public static string[] ConsumeCleanup(string[] args)
    {
        if (args.Length < 3 || args[0] != "--cleanup-player-update" || !int.TryParse(args[1], out int installer) || !IsStage(args[2]))
            return args;
        string directory = args[2];
        _ = Task.Run(() =>
        {
            try
            {
                using var process = Process.GetProcessById(installer);
                if (!process.WaitForExit(5000)) return;
            }
            catch (ArgumentException) { }
            CleanStage(directory);
        });
        return args[3..];
    }

    public static void CleanStage(string directory)
    {
        if (!IsStage(directory)) return;
        try
        {
            if (!Directory.Exists(directory) || (File.GetAttributes(StageRoot) & FileAttributes.ReparsePoint) != 0 ||
                (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0) return;
            File.Delete(Path.Combine(directory, PackageName));
            File.Delete(Path.Combine(directory, ManifestName));
            Directory.Delete(directory);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    public static void CleanOldStages()
    {
        try
        {
            if (!Directory.Exists(StageRoot) || (File.GetAttributes(StageRoot) & FileAttributes.ReparsePoint) != 0) return;
            var cutoff = DateTime.UtcNow.AddDays(-1);
            foreach (var directory in new DirectoryInfo(StageRoot).EnumerateDirectories())
            {
                if (directory.LastWriteTimeUtc < cutoff) CleanStage(directory.FullName);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose() => this.http.Dispose();

    private static bool IsStage(string directory) =>
        string.Equals(Path.GetDirectoryName(directory), StageRoot, StringComparison.OrdinalIgnoreCase) &&
        Guid.TryParseExact(Path.GetFileName(directory), "N", out _);

    private static void WaitForOriginal(int parent, long originalStart, string target)
    {
        Process process;
        try { process = Process.GetProcessById(parent); }
        catch (ArgumentException) { return; }
        using (process)
        {
            if (process.HasExited) return;
            try
            {
                if (process.StartTime.ToUniversalTime().ToFileTimeUtc() != originalStart) return;
                if (!string.Equals(process.MainModule?.FileName, target, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Die laufende SoulLink-EXE passt nicht zum Update-Ziel.");
            }
            catch (Exception error) when (error is Win32Exception or InvalidOperationException && process.HasExited) { return; }
            if (!process.WaitForExit(25000)) throw new IOException("SoulLink ist noch geöffnet. Bitte schließe alle SoulLink-Fenster.");
        }
    }

    private static void Replace(string target, string package, string stage, string[] arguments)
    {
        if (!File.Exists(target) || !target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Die zu aktualisierende SoulLink-EXE fehlt.");
        string temporary = target + ".soullink-" + Guid.NewGuid().ToString("N");
        string backup = temporary + ".backup";
        try
        {
            File.Copy(package, temporary);
            File.Replace(temporary, target, backup);
            try
            {
                StartProcess(target, ["--cleanup-player-update", Environment.ProcessId.ToString(CultureInfo.InvariantCulture), stage, .. arguments]);
            }
            catch
            {
                File.Replace(backup, target, null);
                throw;
            }
            File.Delete(backup);
        }
        finally
        {
            File.Delete(temporary);
        }
    }

    private static void StartProcess(string executable, string[] arguments)
    {
        var options = new ProcessStartInfo(executable) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (string argument in arguments) options.ArgumentList.Add(argument);
        using var process = Process.Start(options) ?? throw new InvalidOperationException("SoulLink konnte nicht gestartet werden.");
    }

    private static Settings? ReadSettings()
    {
        var entries = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
        string? Get(string key) => entries.FirstOrDefault(entry => entry.Key == key)?.Value;
        return PlayerUpdateManifest.TryVersion(Get("SoulLinkUpdateVersion"), out var version) &&
            Uri.TryCreate(Get("SoulLinkUpdateServer"), UriKind.Absolute, out var origin) && Get("SoulLinkUpdatePublicKey") is { Length: > 0 } key
            ? new Settings(origin, version, key) : null;
    }

    private sealed record Settings(Uri Origin, Version Version, string PublicKey);
}

internal sealed record StagedPlayerUpdate(string Directory, PlayerUpdateManifest Manifest);
