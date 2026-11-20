using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal static class DockSmokeTest
{
    public static void RunClose(ClientRuntime runtime, PortableMgba emulator, RomSelection rom, string home, string path, bool detached)
    {
        using var form = new CompanionForm(runtime, emulator, rom, Path.Combine(home, "Bridge"), path, null, home);
        int port = 0;
        string? failure = null;
        Process? game = null;
        var progress = Path.Combine(home, "shutdown-progress.txt");
        File.WriteAllText(progress, "Starting the player.");
        using var diagnostics = new System.Windows.Forms.Timer { Interval = 500 };
        diagnostics.Tick += (_, _) => File.WriteAllText(progress,
            $"Player status: {form.ConnectionStatus}; docked: {form.IsGameDocked}; emulator exited: {game?.HasExited}; client task: {runtime.ExecuteTask?.Status}.");
        form.FormClosing += (_, _) => File.AppendAllText(progress, " Requested the player close.");
        form.FormClosed += (_, _) => File.WriteAllText(progress, "The player window closed.");
        form.Shown += async (_, _) =>
        {
            diagnostics.Start();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
            try
            {
                while (!form.IsGameDocked || game is null)
                {
                    game ??= FindFixtureEmulator(emulator);
                    await Task.Delay(50, timeout.Token);
                }
                port = runtime.Status().BridgePort;
                File.WriteAllText(progress, $"The emulator is docked with bridge port {port}.");
                if (detached)
                {
                    ((Button)form.Controls.Find("ToggleEmulatorDock", true).Single()).PerformClick();
                    if (form.IsGameDocked) throw new InvalidOperationException("Could not detach mGBA before closing the player.");
                }
            }
            catch (Exception error)
            {
                failure = $"The close setup could not complete (docked: {form.IsGameDocked}, bridge: {runtime.Status().Bridge}). {error}";
                File.WriteAllText(Path.Combine(home, "game-error.txt"), failure);
            }
            form.Close();
        };

        Application.Run(form);
        diagnostics.Stop();
        try
        {
            if (failure is not null) throw new InvalidOperationException(failure);
            if (game is not { HasExited: true }) throw new InvalidOperationException("Closing SoulLink left the owned mGBA process running.");
            if (port <= 0) throw new InvalidOperationException("The bridge did not bind a port.");
            var restartedBridge = new TcpListener(IPAddress.Loopback, port);
            try { restartedBridge.Start(); }
            finally { restartedBridge.Stop(); }
            File.WriteAllText(Path.Combine(home, "shutdown-result.txt"), "Emulator exited and bridge port released.");
        }
        catch (Exception error)
        {
            File.WriteAllText(Path.Combine(home, "game-error.txt"), error + Environment.NewLine + string.Join(Environment.NewLine, runtime.Status().Log));
            Environment.ExitCode = 1;
        }
        finally
        {
            game ??= FindFixtureEmulator(emulator);
            if (game is { HasExited: false })
            {
                game.CloseMainWindow();
                if (!game.WaitForExit(3000)) { game.Kill(); game.WaitForExit(); }
            }
            game?.Dispose();
        }
    }

    private static Process? FindFixtureEmulator(PortableMgba emulator)
    {
        Process? owned = null;
        foreach (var candidate in Process.GetProcessesByName("mGBA"))
        {
            if (owned is null && !candidate.HasExited &&
                string.Equals(candidate.MainModule?.FileName, emulator.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                owned = candidate;
            else candidate.Dispose();
        }
        return owned;
    }

    public static void Run(ClientRuntime runtime, PortableMgba emulator, RomSelection rom, string home, string path, string? testState)
    {
        using var window = new Form
        {
            ClientSize = new Size(640, 435),
            ShowInTaskbar = false,
            Text = "SoulLink – isolierter mGBA-Test",
        };
        var surface = new EmulatorSurface { Dock = DockStyle.Fill };
        var focusOutsideGame = new TextBox { Dock = DockStyle.Top };
        window.Controls.Add(surface);
        window.Controls.Add(focusOutsideGame);
        using var dock = new EmulatorDock(surface);
        surface.GameClicked += () => dock.FocusGame();
        window.Shown += async (_, _) =>
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            try
            {
                rom.Save(path);
                using var originalMap = RubyMapArtwork.Read(path);
                if (originalMap is not null)
                    File.WriteAllText(Path.Combine(home, "rubymap-result.txt"), $"{originalMap.Width}×{originalMap.Height}");
                await runtime.StartAsync(timeout.Token);
                string bridge = BridgeInstaller.Install(Path.Combine(home, "Bridge"), runtime.Status().BridgePort);
                using var process = emulator.Start(rom.Read()!, bridge, testState, softwareDisplay: true);
                try
                {
                    GameSnapshot? snapshot;
                    do
                    {
                        if (process.HasExited) throw new InvalidOperationException($"mGBA exited with code {process.ExitCode}.");
                        dock.TryAttach(process);
                        snapshot = runtime.Status().Snapshot;
                        if (!dock.Attached || snapshot is null) await Task.Delay(100, timeout.Token);
                    }
                    while (!dock.Attached || snapshot is null);

                    window.ClientSize = new Size(690, 458);
                    dock.Resize();
                    if (!EmulatorDock.CloseStartupScriptingWindow(process))
                        throw new InvalidOperationException("Could not close the startup scripting view.");
                    while (EmulatorDock.HasScriptingWindow(process)) await Task.Delay(50, timeout.Token);
                    GameSnapshot? after;
                    while ((after = runtime.Status().Snapshot) is null || after.Frame <= snapshot.Frame)
                        await Task.Delay(50, timeout.Token);
                    window.Activate();
                    if (!focusOutsideGame.Focus()) throw new InvalidOperationException("Could not move focus to the test control.");
                    SendMessage(surface.Handle, 0x0210, 0x0201, 0);
                    if (!dock.IsFocused) throw new InvalidOperationException("Click notification did not focus the embedded game.");
                    if (!dock.Attached || !dock.Detach()) throw new InvalidOperationException("The native mGBA window was not restored.");
                    File.WriteAllText(Path.Combine(home, "game-result.json"), Protocol.Serialize(after));
                }
                finally
                {
                    dock.Dispose();
                    if (!process.HasExited)
                    {
                        process.Refresh();
                        process.CloseMainWindow();
                        if (!process.WaitForExit(3000)) process.Kill();
                    }
                }
            }
            catch (Exception exception)
            {
                File.WriteAllText(Path.Combine(home, "game-error.txt"), exception + Environment.NewLine + string.Join(Environment.NewLine, runtime.Status().Log));
                Environment.ExitCode = 1;
            }
            finally
            {
                await runtime.StopAsync(CancellationToken.None);
                window.Close();
            }
        };
        Application.Run(window);
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
