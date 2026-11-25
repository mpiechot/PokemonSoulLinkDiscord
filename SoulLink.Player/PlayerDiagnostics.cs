using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace SoulLink.Player;

internal sealed class PlayerDiagnostics(string home)
{
    private const int MaximumBytes = 512 * 1024;
    private readonly object gate = new();
    private readonly Guid session = Guid.NewGuid();
    private static readonly string Version = typeof(PlayerDiagnostics).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "SoulLinkUpdateVersion")?.Value ??
        typeof(PlayerDiagnostics).Assembly.GetName().Version?.ToString() ?? "";
    private static PlayerDiagnostics? current;
    internal string DirectoryPath { get; } = Path.Combine(home, "Logs");

    public static void Start(string home, bool observeUnhandled)
    {
        current = new PlayerDiagnostics(home);
        Report("player.started");
        if (!observeUnhandled) return;
        AppDomain.CurrentDomain.UnhandledException += (_, args) => Report("process.unhandled", args.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, args) => Report("task.unobserved", args.Exception);
        Application.ThreadException += (_, args) =>
        {
            Report("ui.unhandled", args.Exception);
            if (MessageBox.Show("Ein unerwarteter Fehler wurde im Diagnoseprotokoll erfasst. SoulLink weiterverwenden?",
                "SoulLink", MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.No)
                Application.Exit();
        };
    }

    public static void OpenDirectory()
    {
        if (current is not { } log) return;
        Directory.CreateDirectory(log.DirectoryPath);
        var options = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        options.ArgumentList.Add(log.DirectoryPath);
        using var process = Process.Start(options);
    }

    public static void Report(string code, Exception? error = null, object? fields = null) => current?.Write(code, error, fields);

    internal void Write(string code, Exception? error = null, object? fields = null)
    {
        try
        {
            // Exception messages and source paths can contain credentials, ROM paths, or invitation links.
            var stack = error is null ? null : new StackTrace(error, fNeedFileInfo: false).GetFrames().Take(18)
                .Select(frame => frame.GetMethod()).Where(method => method is not null)
                .Select(method => method!.DeclaringType?.FullName + "." + method.Name).ToArray();
            string entry = JsonSerializer.Serialize(new
            {
                timeUtc = DateTimeOffset.UtcNow,
                this.session,
                version = Version,
                code,
                error = error?.GetType().FullName,
                stack,
                fields,
            });
            lock (this.gate)
            {
                Directory.CreateDirectory(this.DirectoryPath);
                var file = new FileInfo(Path.Combine(this.DirectoryPath, "player.jsonl"));
                if (file.Exists && file.Length >= MaximumBytes)
                    File.Move(file.FullName, Path.Combine(this.DirectoryPath, "player.previous.jsonl"), overwrite: true);
                File.AppendAllText(file.FullName, entry + Environment.NewLine);
            }
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException) { }
    }
}
