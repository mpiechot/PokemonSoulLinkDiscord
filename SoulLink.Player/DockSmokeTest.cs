using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal static class DockSmokeTest
{
    public static void RunClose(ClientRuntime runtime, PortableMgba emulator, RomSelection rom, string home, string path,
        bool detached, bool emptyParty, bool resume)
    {
        if (resume) rom.Save(path);
        using var form = new CompanionForm(runtime, emulator, rom, Path.Combine(home, "Bridge"), resume ? null : path, null, home);
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
                if (resume) Descendants(form).OfType<Button>().Single(button => button.Text == "Run fortsetzen").PerformClick();
                while (!form.IsGameDocked || game is null)
                {
                    game ??= FindFixtureEmulator(emulator);
                    await Task.Delay(50, timeout.Token);
                }
                port = runtime.Status().BridgePort;
                File.WriteAllText(progress, $"The emulator is docked with bridge port {port}.");
                VerifySidebar(form);
                if (resume)
                {
                    while (runtime.Status().Snapshot is null) await Task.Delay(50, timeout.Token);
                    if (runtime.Status().Snapshot is not { MapNumber: 16, InBattle: false })
                        throw new InvalidDataException("The launcher did not load the newest autosave at startup.");
                }
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

    // This code runs only in the verified synthetic cartridge's copy of the bridge.
    private const string WildBattleFixture = """

        local testFrame = 0
        local function testWildBattle()
            -- The last PC grant lets the test start after all existing item assertions.
            if testFrame == 0 then
                for slot = 0, 49 do
                    if read16(0x02025BCC + 4 * slot) == 12 then testFrame = 1; break end
                end
                if testFrame == 0 then return end
            end
            testFrame = testFrame + 1
            local function startBattle()
                readCore():write32(0x03001BAC, 0x200) -- Main.inBattle is the second byte.
                readCore():write32(0x02024D24, 0) -- The outcome is the third byte.
                readCore():write32(0x020239F8, 4)
                readCore():write32(0x02024A68, 2)
                readCore():write32(0x02024A70, 0x01000000)
                readCore():write32(0x02024A80, 25)
                readCore():write32(0x02024AA8, 15)
                readCore():write32(0x02024AAC, 20)
                readCore():write32(0x02024AD8, 280)
                readCore():write32(0x02024B00, 9)
                readCore():write32(0x02024B04, 17)
            end
            local function escape()
                readCore():write32(0x02024D24, 0x00040000)
                readCore():write32(0x03001BAC, 0)
            end
            if testFrame == 30 then
                readCore():write8(0x02025739, 16)
                readCore():write8(0x02026962, 0x10) -- Adventure flag set, but balls only in PC storage.
                startBattle()
            elseif testFrame == 120 then
                readCore():write32(0x03001774, 0) -- Return to the field only after the transition.
                escape()
            elseif testFrame == 140 then
                readCore():write32(0x03001774, 0x080546E5)
            elseif testFrame == 150 then
                readCore():write8(0x02026962, 0)
                readCore():write32(0x02025D38, 0x00010003) -- One Great Ball in the second bag slot.
                startBattle()
            elseif testFrame == 210 then
                readCore():write32(0x02025D38, 0) -- Using the last ball must not unmark the encounter.
                readCore():write32(0x02024B00, 7)
            elseif testFrame == 270 then
                escape()
            elseif testFrame == 300 then
                startBattle()
                readCore():write32(0x020239F8, 8)
            elseif testFrame == 360 then
                readCore():write32(0x02024D24, 0x00010000)
                readCore():write32(0x03001BAC, 0)
            end
        end
        """;

    private static async Task VerifyBattleAutosavesAsync(ClientRuntime runtime, string autoSlot, CancellationToken cancellationToken)
    {
        async Task<GameSnapshot> WaitFor(string stage, Func<GameSnapshot, bool> matches)
        {
            try
            {
                while (true)
                {
                    if (runtime.Status().Snapshot is { } snapshot && matches(snapshot)) return snapshot;
                    await Task.Delay(25, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                var last = runtime.Status().Snapshot;
                throw new InvalidDataException($"Wild battle test timed out at {stage}. Last frame: {last?.Frame}, area: {last?.Area}, " +
                    $"battle: {Protocol.Serialize(last?.Battle)}, outcomes: {Protocol.Serialize(last?.WildBattles)}.");
            }
        }

        var noBalls = await WaitFor("battle with only PC balls", snapshot => snapshot.Battle is { });
        if (noBalls.Battle!.CatchEncounter) throw new InvalidDataException("PC balls counted as balls in the bag.");
        await WaitFor("escape without balls", snapshot => !snapshot.InBattle);
        if (File.Exists(autoSlot)) throw new InvalidDataException("Autosave ran before the field transition completed.");
        var encounter = await WaitFor("battle with a Great Ball", snapshot => snapshot.Battle is { } battle && battle.Key != noBalls.Battle.Key);
        DateTime firstSave = SaveTime();
        if (encounter.Battle!.CatchEncounter is false || encounter.WildBattles is { Length: > 0 })
            throw new InvalidDataException("The first wild battle with a ball in the bag was not recognized.");
        await WaitFor("last ball used", snapshot => snapshot.Battle is { CatchEncounter: true, Combatants: [_, { Hp: 7 }] });
        if (SaveTime() != firstSave) throw new InvalidDataException("Slot 2 was written again before the second battle ended.");
        var escaped = await WaitFor("eligible escape", snapshot => !snapshot.InBattle && snapshot.WildBattles is { Length: > 0 });
        if (escaped.WildBattles is not [{ Area: 16, Species: 280, Outcome: 4 }])
            throw new InvalidDataException("The wild encounter outcome did not retain its route and opponent.");
        DateTime secondSave = SaveTime();
        if (secondSave <= firstSave) throw new InvalidDataException("The second wild battle did not update slot 2.");
        var trainer = await WaitFor("trainer battle", snapshot => snapshot.Battle is { } battle && battle.Key != encounter.Battle.Key);
        if (trainer.Battle!.CatchEncounter || SaveTime() != secondSave)
            throw new InvalidDataException("Slot 2 changed before the trainer battle ended.");
        await WaitFor("trainer victory", snapshot => !snapshot.InBattle);
        if (SaveTime() <= secondSave) throw new InvalidDataException("Trainer victory did not update slot 2.");

        DateTime SaveTime() => new FileInfo(autoSlot) is { Exists: true, Length: > 0 } file ? file.LastWriteTimeUtc
            : throw new InvalidDataException("A completed battle did not produce a slot 2 save.");
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
                if (testItems)
                {
                    string source = File.ReadAllText(bridge);
                    File.WriteAllText(bridge, source
                        .Replace("local function onFrame()", WildBattleFixture + "\nlocal function onFrame()", StringComparison.Ordinal)
                        .Replace("    receiveLocalAction()", "    receiveLocalAction()\n    if activeProfile then testWildBattle() end", StringComparison.Ordinal));
                }
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
                        while (!runtime.CanLoadState || !runtime.CanSaveState) await Task.Delay(25, timeout.Token);
                        try
                        {
                            await runtime.LoadSlotOneAsync(timeout.Token);
                            throw new InvalidDataException("mGBA loaded an empty slot.");
                        }
                        catch (IOException error) when (error.Message.StartsWith("Slot 1 ist leer", StringComparison.Ordinal)) { }
                        await runtime.SaveSlotOneAsync(timeout.Token);
                        string manualSlot = Path.ChangeExtension(path, ".ss1");
                        string autoSlot = Path.ChangeExtension(path, ".ss2");
                        if (new FileInfo(manualSlot) is not { Exists: true, Length: > 0 } manual)
                            throw new InvalidDataException("mGBA did not write the quick save to slot 1.");
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
                            foreach (var ball in Enum.GetValues<PcItem>().Where(item => item != PcItem.RareCandy))
                                if (await runtime.AddPcItemsAsync(ball, timeout.Token) != 99)
                                    throw new InvalidOperationException($"The fixture did not receive 99 {ball}.");
                            await VerifyBattleAutosavesAsync(runtime, autoSlot, timeout.Token);
                        }
                        DateTime automatic = File.GetLastWriteTimeUtc(autoSlot);
                        await runtime.LoadSlotOneAsync(timeout.Token);
                        while (!runtime.CanLoadState || runtime.Status().Snapshot is not { } loaded || loaded.Epoch == after.Epoch)
                            await Task.Delay(25, timeout.Token);
                        if (!after.InBattle)
                        {
                            while (!runtime.CanAddPcItems) await Task.Delay(25, timeout.Token);
                            if (await runtime.AddPcItemsAsync(PcItem.RareCandy, timeout.Token) != 1049)
                                throw new InvalidDataException("Quick load did not restore the PC inventory saved in slot 1.");
                        }
                        if (File.GetLastWriteTimeUtc(autoSlot) != automatic || File.GetLastWriteTimeUtc(manualSlot) != manual.LastWriteTimeUtc)
                            throw new InvalidDataException("Quick load or autosave changed a slot unexpectedly.");
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

    internal static void VerifyTestPlayers(string home)
    {
        string scope = "Local\\SoulLink.Smoke." + Guid.NewGuid().ToString("N");
        using var primary = PlayerInstance.Open(1, allowFallback: false, wait: false, scope);
        if (primary?.Slot != 1) throw new InvalidOperationException("The main player did not reserve its slot.");
        Task.Factory.StartNew(() =>
        {
            using var first = PlayerInstance.Open(1, allowFallback: true, wait: false, scope);
            if (first?.Slot != 2) throw new InvalidOperationException("The second player did not get an independent slot.");
            int? next = Task.Factory.StartNew(() =>
            {
                using var third = PlayerInstance.Open(2, allowFallback: true, wait: false, scope);
                return third?.Slot;
            },
                CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).GetAwaiter().GetResult();
            if (next != 3) throw new InvalidOperationException("The third player did not get an independent slot.");
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).GetAwaiter().GetResult();

        string source = Path.Combine(home, "Test original.gba");
        File.WriteAllBytes(source, [1, 2, 3]);
        var firstEmulator = new PortableMgba(Path.Combine(home, "TestPlayers", "2"), isolated: true);
        var secondEmulator = new PortableMgba(Path.Combine(home, "TestPlayers", "3"), isolated: true);
        string firstCopy = firstEmulator.PrepareRom(source);
        string secondCopy = secondEmulator.PrepareRom(source);
        string save = Path.ChangeExtension(firstCopy, ".sav");
        File.WriteAllText(save, "test 2");
        if (firstCopy == secondCopy || firstCopy == source || secondCopy == source ||
            !File.ReadAllBytes(source).SequenceEqual(File.ReadAllBytes(firstCopy)) ||
            File.Exists(Path.ChangeExtension(secondCopy, ".sav")) || File.Exists(Path.ChangeExtension(source, ".sav")) ||
            firstEmulator.PrepareRom(source) != firstCopy)
            throw new InvalidOperationException("The test players did not isolate their ROM and save paths.");
    }

    internal static void VerifyLinkEditor()
    {
        Guid player = Guid.NewGuid(), first = Guid.NewGuid(), second = Guid.NewGuid(), third = Guid.NewGuid();
        var snapshot = new HostLinkEditor("revision", [new(player, "Spieler", [new(first, 1, "", 5, 16, Placement.Party),
            new(second, 4, "", 5, 17, Placement.Party), new(third, 7, "", null, 17, Placement.Box)])],
            [new(16, false, new() { [player] = first }), new(17, false, new() { [player] = second })]);
        using var editor = new LinkEditorForm("Rubin", snapshot, _ => Task.CompletedTask);
        if (!editor.MovePokemon(player, first, 16, 17) || editor.Changes().Links!.Single(row => row.Area == 16).Members[player] != second)
            throw new InvalidOperationException("Dropping onto an occupied link must swap that player's Pokémon.");
        if (!editor.MovePokemon(player, first, 17, null) || !editor.MovePokemon(player, third, null, Encounter.StarterArea))
            throw new InvalidOperationException("Unlinking a Pokémon or assigning a boxed Pokémon failed.");
        var saved = editor.Changes().Links!;
        if (saved.Any(row => row.Area == 17) || saved.Single(row => row.Area == Encounter.StarterArea).Members[player] != third)
            throw new InvalidOperationException("The link editor did not produce the edited memberships.");
    }

    internal static void VerifyFirstHostConnection(string home)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        string server = $"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}";
        string adminKey = new('a', 64);
        var profiles = new PlayerProfileLibrary(home);
        using var form = new HostingForm(profiles, server);
        Exception? failure = null;
        form.Shown += async (_, _) =>
        {
            try { await VerifyHostAsync(); }
            catch (Exception error) { failure = error; }
            finally { form.Close(); }
        };
        Application.Run(form);
        if (failure is not null) throw new InvalidOperationException("The host window verification failed.", failure);

        async Task VerifyHostAsync()
        {
            SaveToolPreview(form, home, "host-sign-in");
            var status = (Label)form.Controls.Find("HostFeedback", true).Single();
            var signIn = form.Controls.Find("HostSignIn", true).Single();
            var workspace = form.Controls.Find("HostWorkspace", true).Single();
            if (!signIn.Visible || workspace.Visible || form.Session is not null)
                throw new InvalidOperationException("The host workspace was visible before authentication.");
            var key = Descendants(form).OfType<TextBox>().Single(box => box.UseSystemPasswordChar);
            key.Text = new string('b', 64);
            const string groupsPath = "/api/soullink/admin/groups";
            await ConnectAndWait(form, ReplyAsync(groupsPath, key.Text, "401 Unauthorized", ""));
            if (!signIn.Visible || workspace.Visible || status.ForeColor != DashboardTheme.Danger)
                throw new InvalidOperationException("Invalid credentials exposed the host workspace.");
            key.Text = adminKey;
            await ConnectAndWait(form, ReplyAsync(groupsPath, adminKey, "200 OK", "[]"));
            if (status.ForeColor != DashboardTheme.Accent || !status.Text.StartsWith("Verbunden.", StringComparison.Ordinal) ||
                !status.Text.Contains("Neue Gruppe", StringComparison.Ordinal) || signIn.Visible || !workspace.Visible)
                throw new InvalidOperationException("An empty groups response must confirm the connection and explain how to create the first group.");

            if (form.Session is not { } session)
                throw new InvalidOperationException("The host dialog did not retain its verified in-memory session.");
            using var nextRun = new HostingForm(profiles, "https://example.invalid", session, _ => Task.CompletedTask);
            nextRun.Show();
            VerifyRoundedWindow(nextRun);
            if (nextRun.Session is not null || nextRun.Controls.Find("HostWorkspace", true).Single().Visible)
                throw new InvalidOperationException("A remembered key must be verified before exposing host actions.");
            var group = new RunGroup(Guid.NewGuid(), "Rubin-Runde");
            var run = new HostRun(Guid.NewGuid(), "Run 5", RunPhase.Active, true, "EXAMPLE", 2, 2);
            await ConnectAndWait(nextRun, ReplyToGroupAndRunsAsync());
            VerifyRoundedWindow(nextRun);
            if (nextRun.Session?.Server != server)
                throw new InvalidOperationException("Opening the host controls in the next run changed the authenticated server.");
            string[] visibleActions = Descendants(nextRun).OfType<Button>().Where(button => button.Visible).Select(button => button.Text).ToArray();
            if (!visibleActions.Contains("Pausieren") || !visibleActions.Contains("Links bearbeiten") ||
                !visibleActions.Contains("Discord-Runs importieren") || !visibleActions.Contains("Selbst beitreten") ||
                visibleActions.Contains("Starten") || visibleActions.Contains("Fortsetzen") || visibleActions.Contains("Für mich merken"))
                throw new InvalidOperationException("The selected active run did not expose exactly its applicable actions.");
            SaveToolPreview(nextRun, home, "host");
            nextRun.Close();

            async Task ReplyToGroupAndRunsAsync()
            {
                await ReplyAsync(groupsPath, adminKey, "200 OK", Protocol.Serialize(new[] { group })).ConfigureAwait(false);
                await ReplyAsync($"{groupsPath}/{group.Id:D}/runs", adminKey, "200 OK", Protocol.Serialize(new[] { run })).ConfigureAwait(false);
            }
        }

        Task ConnectAndWait(HostingForm target, Task serverReply) => Task.WhenAll(serverReply, target.ConnectAsync()).WaitAsync(timeout.Token);

        async Task ReplyAsync(string path, string expectedKey, string reply, string data)
        {
            using var request = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
            using var stream = request.GetStream();
            using var headers = new StreamReader(stream, leaveOpen: true);
            var line = await headers.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            if (line != $"GET {path} HTTP/1.1")
                throw new InvalidOperationException("The host dialog requested an unexpected URL.");
            bool authorized = false;
            while (await headers.ReadLineAsync(timeout.Token).ConfigureAwait(false) is { Length: > 0 } header)
                authorized |= header.EndsWith(expectedKey, StringComparison.Ordinal);
            if (!authorized) throw new InvalidOperationException("The host dialog did not send the verified admin key.");
            var bytes = System.Text.Encoding.UTF8.GetBytes(data);
            string response = $"HTTP/1.1 {reply}\r\nContent-Type: application/json\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(response), timeout.Token).ConfigureAwait(false);
            await stream.WriteAsync(bytes, timeout.Token)
                .ConfigureAwait(false);
        }
    }

    private static void VerifyRoundedWindow(Form form)
    {
        int right = form.Width - 1;
        int bottom = form.Height - 1;
        if (form.Region is not { } outline ||
            outline.IsVisible(0, 0) || outline.IsVisible(right, 0) ||
            outline.IsVisible(0, bottom) || outline.IsVisible(right, bottom) ||
            !outline.IsVisible(right / 2, 0) || !outline.IsVisible(right / 2, bottom) ||
            !outline.IsVisible(0, bottom / 2) || !outline.IsVisible(right, bottom / 2) ||
            outline.IsVisible(form.Width, bottom / 2) || outline.IsVisible(right / 2, form.Height))
        {
            throw new InvalidOperationException($"The {form.Text} frame must round all four corners and match the {form.Size} window bounds.");
        }
    }

    internal static void VerifyToolWindows(ClientRuntime runtime, string home)
    {
        TraceToolPreviews(home, "Checking setup");
        using var setup = new SetupForm(runtime.Status(), "C:\\ROMs\\Pokémon Rubin.gba", true);
        setup.Show();
        if (Descendants(setup).OfType<TabControl>().Any() || !Descendants(setup).OfType<Button>().Any(button => button.Text == "ROM wechseln" && button.Visible))
            throw new InvalidOperationException("Setup did not expose the ROM action in the themed window.");
        SaveToolPreview(setup, home, "setup");

        TraceToolPreviews(home, "Checking effects");
        using var effects = new StatusEffectsForm();
        effects.Show();
        VerifyRoundedWindow(effects);
        var toxic = Descendants(effects).OfType<Button>().Single(button => button.Text == "Schwere Vergiftung");
        toxic.PerformClick();
        if (!Descendants(effects).OfType<Label>().Any(label => label.Text.Contains("überspringt schwere Vergiftung", StringComparison.Ordinal)))
            throw new InvalidOperationException("Opening the Ruby toxic effect did not show its catch-rate exception.");
        SaveToolPreview(effects, home, "effects");

        TraceToolPreviews(home, "Checking rules");
        using var rules = new RulesForm(runtime);
        rules.Show();
        if (Descendants(rules).OfType<RuleRow>().Any() || Descendants(rules).OfType<Button>().Single(button => button.Text == "Hinzufügen").Enabled)
            throw new InvalidOperationException("A player without an active run should only read the base rules.");
        SaveToolPreview(rules, home, "rules");
        using var ruleSample = new Form { ClientSize = new Size(640, 70), BackColor = DashboardTheme.Background };
        bool edited = false;
        var row = new RuleRow("Ein Fang pro Gebiet.", () => { edited = true; return Task.CompletedTask; }, () => Task.CompletedTask)
        {
            Width = 630,
            Height = 60,
        };
        ruleSample.Controls.Add(row);
        ruleSample.Show();
        var edit = Descendants(row).OfType<Button>().Single(button => button.Text == "Bearbeiten");
        row.Focus();
        Application.DoEvents();
        if (!edit.Visible || !row.ClientRectangle.Contains(row.RectangleToClient(edit.RectangleToScreen(edit.ClientRectangle))))
            throw new InvalidOperationException("Rule actions must be reachable inside the rule card.");
        SaveToolPreview(ruleSample, home, "rule-actions");
        edit.PerformClick();
        if (!edited) throw new InvalidOperationException("The rule edit control was not actionable.");
        TraceToolPreviews(home, "Rule action invoked");
    }

    private static void SaveToolPreview(Form form, string home, string name)
    {
        if (Environment.GetEnvironmentVariable("SOULLINK_SMOKE_PREVIEWS") != "1") return;
        Application.DoEvents();
        using var image = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
        form.DrawToBitmap(image, form.ClientRectangle);
        image.Save(Path.Combine(home, name + ".png"), System.Drawing.Imaging.ImageFormat.Png);
    }

    internal static void TraceToolPreviews(string home, string stage)
    {
        if (Environment.GetEnvironmentVariable("SOULLINK_SMOKE_PREVIEWS") == "1")
            File.AppendAllText(Path.Combine(home, "tool-previews.txt"), stage + Environment.NewLine);
    }

    internal static void VerifyIdleStartup(ClientRuntime runtime, PortableMgba emulator, string home)
    {
        string launcherHome = Path.Combine(home, "Launcher");
        var profiles = new PlayerProfileLibrary(launcherHome);
        VerifyChoices(null, RomSelection.ForProfile(launcherHome, null), ["Run beitreten"]);
        var saved = profiles.AddInvitation("https://example.invalid", "STARTUP", "Rubin-Runde");
        profiles.Choose(saved.Id);
        var rom = RomSelection.ForProfile(launcherHome, saved.Id);
        string cartridge = Path.Combine(launcherHome, "Resume probe.gba");
        var header = new byte[0xBD];
        "POKEMON RUBYAXVD"u8.CopyTo(header.AsSpan(0xA0));
        File.WriteAllBytes(cartridge, header);
        rom.Save(cartridge);
        VerifyChoices(saved, rom, ["Run laden", "Run beitreten"]);

        string manual = Path.ChangeExtension(cartridge, ".ss1");
        string automatic = Path.ChangeExtension(cartridge, ".ss2");
        File.WriteAllBytes(manual, []);
        VerifyChoices(saved, rom, ["Run laden", "Run beitreten"]);
        File.WriteAllText(automatic, "slot 2");
        VerifyChoices(saved, rom, ["Run fortsetzen", "Run laden", "Run beitreten"]);
        if (emulator.LatestState(cartridge) != automatic) throw new InvalidDataException("Slot 2 must be selected when slot 1 is empty.");

        var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.WriteAllText(manual, "slot 1");
        File.SetLastWriteTimeUtc(manual, time);
        File.SetLastWriteTimeUtc(automatic, time);
        if (emulator.LatestState(cartridge) != manual) throw new InvalidDataException("Slot 1 must win a tie.");
        File.SetLastWriteTimeUtc(automatic, time.AddSeconds(1));
        if (emulator.LatestState(cartridge) != automatic) throw new InvalidDataException("The newer autosave must be selected.");
        File.SetLastWriteTimeUtc(manual, time.AddSeconds(2));
        if (emulator.LatestState(cartridge) != manual) throw new InvalidDataException("The newer manual save must be selected.");

        void VerifyChoices(PlayerProfile? profile, RomSelection selection, string[] expected)
        {
            using var form = new CompanionForm(runtime, emulator, selection, Path.Combine(home, "Bridge"), null, profile, home, profiles: profiles, testSlot: 2);
            form.Show();
            Application.DoEvents();
            VerifyRoundedWindow(form);
            if (profile is null)
            {
                form.WindowState = FormWindowState.Maximized;
                if (form.Region is not null)
                {
                    throw new InvalidOperationException("The maximized player must use the full rectangular frame.");
                }

                form.WindowState = FormWindowState.Normal;
                VerifyRoundedWindow(form);
            }

            var surface = Descendants(form).OfType<EmulatorSurface>().Single();
            var buttons = Descendants(surface).OfType<Button>().Where(button => button.Visible).ToArray();
            if (runtime.ExecuteTask is not null || form.IsGameDocked || Descendants(form).OfType<RunFeed>().Single().Visible ||
                !buttons.Select(button => button.Text).SequenceEqual(expected))
            {
                throw new InvalidOperationException("The player should wait for a run choice without opening mGBA, run activity or the bridge.");
            }
            var first = buttons[0].RectangleToScreen(buttons[0].ClientRectangle);
            var last = buttons[^1].RectangleToScreen(buttons[^1].ClientRectangle);
            var bounds = surface.RectangleToScreen(surface.ClientRectangle);
            if (Math.Abs(first.Left + first.Right - bounds.Left - bounds.Right) > 4 ||
                Math.Abs(first.Top + last.Bottom - bounds.Top - bounds.Bottom) > 4)
            {
                throw new InvalidOperationException("Run choices should be centered in the emulator area.");
            }
            form.Close();
            Application.DoEvents();
        }
    }

    internal static void VerifyRosterAndTypeArtwork(string home)
    {
        using var icons = new DiscordIcons();
        foreach (string icon in new[] { "storage-box", "death", "shiny", "unknown-pokemon" })
        {
            if (icons.Get(icon) is not { Width: > 0, Height: > 0 })
                throw new InvalidDataException($"The player package is missing {icon} artwork.");
        }

        using var sprites = new RubySprites(home);
        using var chart = new TypeChartForm(sprites);
        chart.Show();
        VerifyRoundedWindow(chart);
        var selector = Descendants(chart).OfType<FlowLayoutPanel>().Single(panel => panel.AccessibleName == "Pokémon-Typen");
        if (selector.Controls.Count != 17 || selector.Controls.OfType<Button>().Count(button => button.Text == "Feuer") != 1)
            throw new InvalidOperationException("The type chart should expose all 17 types as buttons.");
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
