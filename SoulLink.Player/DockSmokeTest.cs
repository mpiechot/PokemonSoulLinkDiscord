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
                VerifySidebar(form);
                if (detached)
                {
                    form.ChangeDock();
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

    private static void VerifySidebar(CompanionForm form)
    {
        var layout = form.Controls.OfType<TableLayoutPanel>().Single();
        var game = Descendants(layout).OfType<EmulatorSurface>().Single();
        var map = Descendants(layout).OfType<RunDashboard>().Single();
        var roster = Descendants(layout).OfType<RosterGrid>().Single();
        var gameBounds = game.RectangleToScreen(game.ClientRectangle);
        var mapBounds = map.RectangleToScreen(map.ClientRectangle);
        var rosterBounds = roster.RectangleToScreen(roster.ClientRectangle);
        if (gameBounds.Right > rosterBounds.Left || gameBounds.Right > mapBounds.Left ||
            gameBounds.Bottom < rosterBounds.Bottom || mapBounds.Bottom > rosterBounds.Top)
            throw new InvalidOperationException($"The team must stay beside the full-height game. Game: {gameBounds}, map: {mapBounds}, team: {rosterBounds}.");
        var menu = form.Controls.Find("ProgramMenu", true).Single();
        if (menu.Text != "SoulLink") throw new InvalidOperationException("The system menu must display the SoulLink label.");
        using var label = new Bitmap(menu.Width, menu.Height);
        menu.DrawToBitmap(label, menu.ClientRectangle);
        bool drewLabel = false;
        for (int y = 0; y < label.Height && !drewLabel; y++)
            for (int x = 0; x < label.Width && !drewLabel; x++)
            {
                var pixel = label.GetPixel(x, y);
                drewLabel = pixel.G > 150 && pixel.G > pixel.R + 35;
            }
        if (!drewLabel) throw new InvalidOperationException("The SoulLink label was blank when painted.");
    }

    private static IEnumerable<Control> Descendants(Control parent) =>
        parent.Controls.Cast<Control>().SelectMany(child => new[] { child }.Concat(Descendants(child)));

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

                    VerifyDockBounds(dock, surface);
                    foreach (var size in new[] { new Size(690, 620), new Size(560, 400) })
                    {
                        window.ClientSize = size;
                        dock.Resize();
                        await Task.Yield();
                        VerifyDockBounds(dock, surface);
                    }
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
                    using (var notice = new DeathReasonForm(new PendingDeath(Guid.NewGuid(), 25, null, Guid.NewGuid())))
                    {
                        notice.Show(window);
                        await Task.Delay(75, timeout.Token);
                        if (!dock.IsFocused) throw new InvalidOperationException("The death notice stole keyboard focus from mGBA.");
                        notice.Close();
                        if (!dock.IsFocused) throw new InvalidOperationException("Dismissing a passive death notice moved keyboard focus.");
                    }
                    VerifyFeed(home);
                    await VerifyArtworkAsync(home, timeout.Token);
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

    private static void VerifyFeed(string home)
    {
        using var icons = new DiscordIcons();
        using var sprites = new RubySprites(home);
        if (new[] { "catch", "death", "arena", "team" }.Any(kind => icons.Get(kind) is null))
            throw new InvalidOperationException("A Discord icon was missing from the player bundle.");
        using var feed = new RunFeed(icons, sprites) { Size = new Size(265, 180) };
        var now = DateTimeOffset.UtcNow;
        var pokemon = new EventPokemon("Anne", 25);
        feed.Render([
            new AutomationEvent(Guid.NewGuid(), now, "caught", "", Area: 16,
                Details: new EventDetails(pokemon, CatchCheck: new CatchCheck("Pikachu", null, null, null), AreaAvailable: true)),
            new AutomationEvent(Guid.NewGuid(), now, "linked", "", Area: 16, Details: new EventDetails(Members: [pokemon])),
            new AutomationEvent(Guid.NewGuid(), now, "death", "", Area: 16,
                Details: new EventDetails(pokemon, Members: [new EventPokemon("Max", 1)],
                    Battle: new BattleTrace(0, 1, 33, null, true), Reason: new string('x', 200))),
        ], "");
        using var drawing = new Bitmap(feed.Width, feed.Height);
        feed.DrawToBitmap(drawing, feed.ClientRectangle);
        if (feed.AutoScrollMinSize.Height <= feed.ClientSize.Height)
            throw new InvalidOperationException("The feed did not make the additional event cards scrollable.");

        if (RunDialogs.BattleMove(new BattleTrace(0, 1, 33, null, true)) != ("K.-o.-Attacke", "Tackle") ||
            RunDialogs.BattleMove(new BattleTrace(0, 1, 33, null)) != ("Letzte erfasste Attacke", "Tackle") ||
            RunDialogs.BattleMove(null) != ("Attacke", "Unbekannt"))
            throw new InvalidOperationException("The feed did not distinguish a knockout move from an unconfirmed or unknown move.");
    }

    private static void VerifyDockBounds(EmulatorDock dock, EmulatorSurface surface)
    {
        var expected = surface.RectangleToScreen(surface.ClientRectangle);
        if (dock.ClientBounds != expected)
            throw new InvalidOperationException($"The embedded mGBA client was {dock.ClientBounds} instead of {expected}.");
    }

    private static async Task VerifyArtworkAsync(string home, CancellationToken cancellationToken)
    {
        using var image = new Bitmap(24, 24);
        using var png = new MemoryStream();
        image.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        using var handler = new ArtworkResponse(png.ToArray());
        using var client = new HttpClient(handler) { BaseAddress = new Uri("https://artwork.test/") };
        string cache = Path.Combine(home, "Artwork");
        using var sprites = new RubySprites(home, cache, client);
        var pikachu = RubyDashboardCatalog.Appearance(25)!;
        sprites.TypeIcon("electric");
        sprites.Find(pikachu, false);

        using var offline = new RubySprites(cache);
        while (new FileInfo(Path.Combine(cache, "type-icons", "electric.png")) is not { Exists: true, Length: > 0 } ||
            new FileInfo(Path.Combine(cache, "25.png")) is not { Exists: true, Length: > 0 })
            await Task.Delay(20, cancellationToken);

        if (offline.TypeIcon("electric") is not { Image.Width: 24, Label: "Elektro" } || offline.Find(pikachu, false)?.Width != 24)
            throw new InvalidOperationException("A downloaded type symbol or Pokémon sprite could not be reused offline.");
    }

    private sealed class ArtworkResponse(byte[] png) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath is not ("/types/generation-viii/legends-arceus/small/13.png" or
                "/pokemon/versions/generation-iii/ruby-sapphire/25.png"))
                throw new InvalidOperationException($"Unexpected artwork request: {request.RequestUri}.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) });
        }
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
