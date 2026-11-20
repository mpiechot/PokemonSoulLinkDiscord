using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal static class DockSmokeTest
{
    public static void RunClose(ClientRuntime runtime, PortableMgba emulator, RomSelection rom, string home, string path, bool detached, bool emptyParty)
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
                if (emptyParty)
                {
                    while (!runtime.OwnedBridgeReady || runtime.Status().Snapshot is null || EmulatorDock.HasScriptingWindow(game))
                        await Task.Delay(50, timeout.Token);
                    if (runtime.Status().Snapshot is not { Pokemon.Count: 0, Position.Valid: true })
                        throw new InvalidOperationException("The startup script must close while an empty team on the field is readable.");
                }
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

    public static void Run(ClientRuntime runtime, PortableMgba emulator, RomSelection rom, string home, string path, string? testState, bool testItems)
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
                if (testItems)
                {
                    using var fixture = File.OpenRead(path);
                    fixture.Position = 0x700;
                    ReadOnlySpan<byte> expected = "SoulLinkPCFixture"u8;
                    Span<byte> marker = stackalloc byte[expected.Length];
                    fixture.ReadExactly(marker);
                    if (!marker.SequenceEqual(expected))
                        throw new InvalidDataException("PC item tests require the synthetic test ROM.");
                }
                rom.Save(path);
                using var originalMap = RubyMapArtwork.Read(path);
                if (originalMap is not null)
                    File.WriteAllText(Path.Combine(home, "rubymap-result.txt"), $"{originalMap.Width}×{originalMap.Height}");
                await runtime.StartAsync(timeout.Token);
                string bridge = BridgeInstaller.Install(Path.Combine(home, "Bridge"), runtime.Status().BridgePort,
                    testItems ? runtime.BeginLocalItemSession() : null);
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
                    if (testItems)
                    {
                        if (after.InBattle)
                        {
                            if (runtime.CanAddPcItems) throw new InvalidOperationException("The item menu was available during battle.");
                            try
                            {
                                await runtime.AddPcItemsAsync(PcItem.MasterBall, timeout.Token);
                                throw new InvalidDataException("The fixture accepted a PC item grant during battle.");
                            }
                            catch (InvalidOperationException) { }
                        }
                        else
                        {
                            while (!runtime.CanAddPcItems) await Task.Delay(25, timeout.Token);
                            if (await runtime.AddPcItemsAsync(PcItem.RareCandy, timeout.Token) != 1049)
                                throw new InvalidOperationException("The existing 950 candies did not receive exactly 99 more.");
                            foreach (var ball in new[] { PcItem.PokeBall, PcItem.GreatBall, PcItem.UltraBall, PcItem.MasterBall })
                                if (await runtime.AddPcItemsAsync(ball, timeout.Token) != 99)
                                    throw new InvalidOperationException($"The fixture did not receive 99 {ball}.");
                        }
                    }
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
        if (feed.VerticalRail.IsEmpty)
            throw new InvalidOperationException("The feed did not make the additional event cards scrollable.");

        feed.Render([
            new AutomationEvent(Guid.NewGuid(), now, "caught", "", Area: 16,
                Details: new EventDetails(pokemon with { Shiny = true })),
        ], "");
        feed.DrawToBitmap(drawing, feed.ClientRectangle);
        if (drawing.GetPixel(100, 8).ToArgb() != Color.FromArgb(92, 65, 24).ToArgb())
            throw new InvalidOperationException("Shiny catches must use the gold feed background.");

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

    internal static async Task VerifyArtworkAsync(string home, CancellationToken cancellationToken)
    {
        using var image = new Bitmap(24, 24);
        using var png = new MemoryStream();
        image.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        using var handler = new ArtworkResponse(png.ToArray());
        using var client = new HttpClient(handler);
        string cache = Path.Combine(home, "Artwork");
        using var sprites = new RubySprites(home, cache, client);
        var pikachu = RubyDashboardCatalog.Appearance(25)!;
        foreach (string type in new[] { "electric", "normal", "flying", "ground", "rock", "fighting" })
            sprites.TypeIcon(type);
        sprites.Find(pikachu, false);

        using var offline = new RubySprites(cache);
        string[] artwork = ["tcg-icons/Électrique.png", "tcg-icons/Combat.png", "tcg-icons/Incolore.png",
            "type-icons/flying.png", "type-icons/ground.png", "type-icons/rock.png", "25.png"];
        while (artwork.Any(file => new FileInfo(Path.Combine(cache, file)) is not { Exists: true, Length: > 0 }))
            await Task.Delay(20, cancellationToken);

        if (offline.TypeIcon("electric") is not { Image.Width: 24, Label: "Elektro" } || offline.Find(pikachu, false)?.Width != 24)
            throw new InvalidOperationException("A downloaded type symbol or Pokémon sprite could not be reused offline.");
        var expected = new[] { ("ground", "Boden"), ("rock", "Gestein"), ("fighting", "Kampf"), ("normal", "Normal"), ("flying", "Flug") };
        var rendered = expected.Select(pair => offline.TypeIcon(pair.Item1)).ToArray();
        if (rendered.Where((icon, index) => icon.Image?.Width != 24 || icon.Label != expected[index].Item2).Any() ||
            rendered.Select(icon => icon.Image).Distinct().Count() != expected.Length)
            throw new InvalidOperationException("The distinct types displayed for Geodude and Pidgeot must have separate symbols.");
    }

    internal static void VerifyFirstHostConnection(string home)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var response = ReplyWithEmptyGroupsAsync();
        using var form = new HostingForm(new PlayerProfileLibrary(home), $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}");
        var status = Descendants(form).OfType<Label>().Single();
        Descendants(form).OfType<TextBox>().Single(box => box.UseSystemPasswordChar).Text = new string('a', 64);
        var connection = Task.WhenAll(response, form.ConnectAsync()).WaitAsync(timeout.Token);
        while (!connection.IsCompleted)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
        connection.GetAwaiter().GetResult();
        if (status.ForeColor != DashboardTheme.Accent || !status.Text.StartsWith("Verbunden.", StringComparison.Ordinal) ||
            !status.Text.Contains("Gruppe anlegen", StringComparison.Ordinal))
            throw new InvalidOperationException("An empty groups response must confirm the connection and explain how to create the first group.");

        async Task ReplyWithEmptyGroupsAsync()
        {
            using var request = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
            using var stream = request.GetStream();
            using var headers = new StreamReader(stream, leaveOpen: true);
            var line = await headers.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (line != "GET /api/soullink/admin/groups HTTP/1.1")
                throw new InvalidOperationException("The host dialog requested an unexpected URL.");
            while (!string.IsNullOrEmpty(await headers.ReadLineAsync(timeout.Token).ConfigureAwait(false))) { }
            await stream.WriteAsync("HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nContent-Length: 2\r\nConnection: close\r\n\r\n[]"u8.ToArray(), timeout.Token)
                .ConfigureAwait(false);
        }
    }

    internal static void VerifyScrollDesign()
    {
        using var sample = new ScrollProbe { Size = new Size(250, 150), ContentSize = new Size(750, 600) };
        using var drawing = new Bitmap(sample.Width, sample.Height);
        sample.DrawToBitmap(drawing, sample.ClientRectangle);
        if (sample.VerticalRail.IsEmpty || sample.HorizontalRail.IsEmpty ||
            drawing.GetPixel(sample.VerticalRail.Left + 1, sample.VerticalRail.Bottom - 5).ToArgb() != DashboardTheme.Background.ToArgb() ||
            drawing.GetPixel(sample.HorizontalRail.Right - 5, sample.HorizontalRail.Top + 1).ToArgb() != DashboardTheme.Background.ToArgb())
            throw new InvalidOperationException("Scrollbar tracks must use the dark theme on both axes.");
        sample.WheelDown();
        sample.PageRight();
        if (sample.ScrollPosition.X <= 0 || sample.ScrollPosition.Y <= 0)
            throw new InvalidOperationException("The styled scrollbars did not respond to scrolling.");
        sample.ContentSize = new Size(10, 10);
        if (!sample.VerticalRail.IsEmpty || !sample.HorizontalRail.IsEmpty || sample.ScrollPosition != Point.Empty)
            throw new InvalidOperationException("Scrollbar state was not cleared when the content fit.");

        var document = new RichTextBox { Text = string.Join(Environment.NewLine, Enumerable.Repeat("Eine zusätzliche Regel.", 24)) };
        using var text = DashboardScrollHost.ForText(document);
        text.Size = new Size(200, 100);
        text.CreateControl();
        if (text.VerticalRail.IsEmpty || document.ScrollBars != RichTextBoxScrollBars.None)
            throw new InvalidOperationException("Long text must use the themed scrollbar.");
        document.Text = "Eine Regel.";
        if (!text.VerticalRail.IsEmpty)
            throw new InvalidOperationException("The text scrollbar stayed visible after the content shrank.");
    }

    private sealed class ScrollProbe : DashboardScrollView
    {
        public void WheelDown() => this.OnMouseWheel(new MouseEventArgs(MouseButtons.None, 0, 20, 20, -120));

        public void PageRight() => this.OnMouseDown(new MouseEventArgs(MouseButtons.Left, 1, this.HorizontalRail.Right - 3,
            this.HorizontalRail.Top + 3, 0));
    }

    private sealed class ArtworkResponse(byte[] png) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!;
            string path = Uri.UnescapeDataString(uri.AbsolutePath);
            bool symbol = uri.Host == "www.pokepedia.fr" && uri.Query == "?width=120" &&
                path is "/Spécial:Chemin_du_fichier/Miniature_Type_Combat_(flat)_JCC.svg" or
                    "/Spécial:Chemin_du_fichier/Miniature_Type_Électrique_(flat)_JCC.svg" or
                    "/Spécial:Chemin_du_fichier/Miniature_Type_Incolore_(flat)_JCC.svg";
            bool game = uri.Host == "raw.githubusercontent.com" &&
                (path.EndsWith("/legends-arceus/small/3.png", StringComparison.Ordinal) ||
                 path.EndsWith("/legends-arceus/small/5.png", StringComparison.Ordinal) ||
                 path.EndsWith("/legends-arceus/small/6.png", StringComparison.Ordinal));
            bool pokemon = uri.Host == "raw.githubusercontent.com" &&
                path.EndsWith("/pokemon/versions/generation-iii/ruby-sapphire/25.png", StringComparison.Ordinal);
            if (!symbol && !pokemon && !game)
                throw new InvalidOperationException($"Unexpected artwork request: {request.RequestUri}.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(png) });
        }
    }

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}
