using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal static class DockSmokeTest
{
    public static void Run(ClientRuntime runtime, PortableMgba emulator, RomSelection rom, string home, string path, string? testState)
    {
        using var window = new Form
        {
            ClientSize = new Size(640, 435),
            ShowInTaskbar = false,
            Text = "SoulLink – isolierter mGBA-Test",
        };
        var surface = new Panel { Dock = DockStyle.Fill };
        window.Controls.Add(surface);
        using var dock = new EmulatorDock(surface);
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
                    if (!dock.FocusGame()) throw new InvalidOperationException("The native mGBA window could not receive keyboard focus.");
                    if (!dock.Attached || !dock.Detach()) throw new InvalidOperationException("The native mGBA window was not restored.");
                    File.WriteAllText(Path.Combine(home, "game-result.json"), Protocol.Serialize(snapshot));
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
}
