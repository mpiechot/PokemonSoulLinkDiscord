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
                    var player = new ClientPlayerView("Fixture", Guid.NewGuid(), "", "Ready");
                    var lobby = new ClientRunView("Fixture", RunPhase.Lobby, Guid.NewGuid(), [player], [], []);
                    var ready = runtime.Status() with { Run = lobby, Registered = true, LocalPlayerId = player.Id, IsLocalRun = false };
                    string slot = emulator.SlotOnePath(path);
                    await form.TrySaveInitialCheckpointAsync(ready);
                    if (new FileInfo(slot) is not { Exists: true, Length: > 0 })
                        throw new InvalidOperationException("A synchronized player without a starter must receive a slot 1 checkpoint.");
                    var existing = File.GetLastWriteTimeUtc(slot);
                    await form.TrySaveInitialCheckpointAsync(ready with { Run = lobby with { Phase = RunPhase.Active } });
                    if (File.GetLastWriteTimeUtc(slot) != existing)
                        throw new InvalidOperationException("Starting the run must retain the lobby checkpoint.");
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
        var roster = Descendants(layout).OfType<RosterGrid>().Single();
        var gameBounds = game.RectangleToScreen(game.ClientRectangle);
        var rosterBounds = roster.RectangleToScreen(roster.ClientRectangle);
        if (Descendants(layout).OfType<HoennMap>().Any() || gameBounds.Right > rosterBounds.Left ||
            gameBounds.Bottom < rosterBounds.Bottom || rosterBounds.Height < gameBounds.Height / 2)
            throw new InvalidOperationException($"The team must fill the sidebar beside the game. Game: {gameBounds}, team: {rosterBounds}.");
        var mapButton = Descendants(layout).OfType<Button>().Single(button => button.Text == "Karte");
        mapButton.PerformClick();
        using var map = form.OwnedForms.OfType<RubyMapForm>().Single();
        if (!map.Visible || !map.TopMost || map.ContainsFocus || SendMessage(map.Handle, 0x0021, 0, 0) != 3)
            throw new InvalidOperationException("Opening or clicking the map must not activate the popup.");
        map.Close();
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
        local function read16(address) return readCore():read16(address) end
        local function read32(address) return readCore():read32(address) end
        local function testWildBattle()
            -- The last PC grant lets the test start after all existing item assertions.
            if testFrame == 0 then
                for slot = 0, 49 do
                    if read16(0x02025BCC + 4 * slot) == 12 then testFrame = 1; break end
                end
                if testFrame == 0 then return end
                local bagCandies = 0
                for slot = 0, 19 do
                    local address = 0x02025C94 + 4 * slot
                    if read16(address) == 68 then bagCandies = bagCandies + read16(address + 2) end
                end
                assert(bagCandies == 1976, "Bag capacity failure changed the item pocket")
                assert(read32(0x02025D34) == 0x00630004, "Poké Balls were not added to the bag ball pocket")
                readCore():write32(0x02025D34, 0)
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
            elseif testFrame == 390 then
                readCore():write32(0x02025D38, 0x00010003)
                startBattle()
                readCore():write32(0x020239F8, 0x0200) -- Wally tutorial.
                readCore():write32(0x02024AD8, 392) -- Ralts.
            elseif testFrame == 450 then
                readCore():write32(0x02024D24, 0x00070000)
                readCore():write32(0x03001BAC, 0)
            elseif testFrame == 480 then
                startBattle()
            elseif testFrame == 540 then
                readCore():write32(0x02024D24, 0x00070000)
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
        DateTime trainerSave = SaveTime();
        if (trainerSave <= secondSave) throw new InvalidDataException("Trainer victory did not update slot 2.");

        var tutorial = await WaitFor("Wally tutorial", snapshot => snapshot.InBattle && snapshot.Battle?.Key != trainer.Battle.Key);
        if (tutorial.Battle is not null || tutorial.WildBattles is not [{ Outcome: 4 }])
            throw new InvalidDataException("The tutorial exposed a battle or changed the recorded wild encounters.");
        await WaitFor("Wally tutorial ending", snapshot => !snapshot.InBattle && snapshot.Frame > tutorial.Frame);
        if (SaveTime() != trainerSave) throw new InvalidDataException("The watched catch triggered a battle autosave.");

        var nextEncounter = await WaitFor("normal encounter after the tutorial", snapshot => snapshot.Battle is { CatchEncounter: true });
        var caught = await WaitFor("normal catch after the tutorial", snapshot => snapshot.Frame > nextEncounter.Frame && !snapshot.InBattle);
        if (caught.WildBattles is not [{ Outcome: 4 }, { Species: 280, Outcome: 7 }])
            throw new InvalidDataException("The tutorial changed the normal wild catch result.");

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
                        .Replace("    receiveLocalAction()", "    receiveLocalAction()\n    if activeGame then testWildBattle() end", StringComparison.Ordinal));
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
                    using (var sprites = new RubySprites(home))
                    {
                        using var trade = new PokemonTradeForm(runtime, sprites);
                        using var offer = new PokemonTradeOfferForm(new TradeView(Guid.NewGuid(), "Route 101", PokemonTradePhase.Offered,
                            new TradePlayer(Guid.NewGuid(), "Marco", new PokemonState { Species = 25 }),
                            new TradePlayer(Guid.NewGuid(), "Misty", new PokemonState { Species = 26 })), sprites, _ => Task.CompletedTask);
                        foreach (var tool in new Form[] { trade, offer })
                        {
                            if (!focusOutsideGame.Focus()) throw new InvalidOperationException("Could not focus the trade launcher.");
                            dock.RestoreFocusAfterClosing(tool);
                            tool.Show(window);
                            tool.Activate();
                            await Task.Yield();
                            if (dock.IsFocused || !tool.ContainsFocus)
                                throw new InvalidOperationException($"{tool.GetType().Name} did not receive focus.");
                            tool.Close();
                            await Task.Delay(75, timeout.Token);
                            if (!dock.IsFocused)
                                throw new InvalidOperationException($"Closing {tool.GetType().Name} did not return focus to mGBA (active form: {Form.ActiveForm?.Text}, owner contains focus: {window.ContainsFocus}).");
                        }
                    }
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
                            for (int grants = 1; grants <= 19; grants++)
                                if (await runtime.AddItemsAsync(PcItem.RareCandy, ItemDestination.Bag, timeout.Token) != 95 + 99 * grants)
                                    throw new InvalidDataException("Bag items did not use Ruby's 99-item stacks.");
                            try
                            {
                                await runtime.AddItemsAsync(PcItem.RareCandy, ItemDestination.Bag, timeout.Token);
                                throw new InvalidDataException("The almost-full bag accepted another 99 items.");
                            }
                            catch (InvalidOperationException error) when (error.Message.Contains("im Rucksackfach zu wenig Platz", StringComparison.Ordinal)) { }
                            if (await runtime.AddItemsAsync(PcItem.PokeBall, ItemDestination.Bag, timeout.Token) != 99)
                                throw new InvalidDataException("The bag did not receive 99 Poké Balls.");
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
                    if (emulator.Bundled && !after.InBattle && (!await dock.OpenInputSettingsAsync(process).WaitAsync(timeout.Token) ||
                        !EmulatorDock.CloseInputSettingsWindow(process)))
                        throw new InvalidOperationException("The input action did not open mGBA's native settings while docked.");
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
        using var sprites = new RubySprites(Path.Combine(home, "ArtworkInput"), cache, client);
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

    internal static void VerifyEmulatorProcessCheck(string home)
    {
        string fixture = Path.Combine(home, "EmulatorProcessProbe");
        var emulator = new PortableMgba(fixture);
        string unrelated = CreateProcessFixture(Path.Combine(fixture, "Other", "mGBA.exe"));
        string configured = CreateProcessFixture(emulator.ExecutablePath ?? Path.Combine(fixture, "Selected", "mGBA.exe"));
        if (!emulator.Bundled) emulator.SelectExternal(configured);

        VerifyWhileRunning(unrelated, expected: false);
        VerifyWhileRunning(configured, expected: true);
        if (emulator.IsAlreadyRunning()) throw new InvalidOperationException("An exited mGBA still blocked launch.");

        static string CreateProcessFixture(string path)
        {
            string directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), path, overwrite: true);
            File.WriteAllText(Path.Combine(directory, "portable.ini"), "");
            return path;
        }

        void VerifyWhileRunning(string path, bool expected)
        {
            using var process = Process.Start(new ProcessStartInfo(path)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                ArgumentList = { "-t", "127.0.0.1" },
            }) ?? throw new InvalidOperationException("The mGBA process fixture could not start.");
            try
            {
                if (process.HasExited) throw new InvalidOperationException("The mGBA process fixture exited early.");
                if (emulator.IsAlreadyRunning() != expected)
                    throw new InvalidOperationException(expected ? "The configured mGBA did not block a duplicate launch."
                        : "An unrelated mGBA triggered the already-running warning.");
            }
            finally
            {
                if (!process.HasExited) process.Kill();
                if (!process.WaitForExit(5000)) throw new InvalidOperationException("The mGBA process fixture did not stop.");
            }
        }
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
        var snapshot = new HostLinkEditor("revision", [new(Guid.NewGuid(), "Mitspieler", []),
            new(player, "Spieler", [new(first, 1, "", 5, 16, Placement.Party),
            new(second, 4, "", 5, 17, Placement.Party), new(third, 7, "", null, 17, Placement.Box)])],
            [new(16, false, new() { [player] = first }), new(17, false, new() { [player] = second })]);
        using var editor = new LinkEditorForm("Rubin", snapshot, _ => Task.CompletedTask, player);
        var grid = Descendants(editor).OfType<DataGridView>().Single();
        if (grid.Columns[1].HeaderText != "Spieler" || grid.Columns[2].HeaderText != "Mitspieler" ||
            !grid.Rows.Cast<DataGridViewRow>().Any(row => Equals(row.Cells[0].Value, "Route 101") &&
                Equals(row.Cells[1].Value, "Bisasam (Lv. 5)")))
            throw new InvalidOperationException("The first player column must show the local player's name and Pokémon.");
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
            var serverInput = (TextBox)form.Controls.Find("HostServer", true).Single();
            var key = (TextBox)form.Controls.Find("HostAdminKey", true).Single();
            if (!key.UseSystemPasswordChar || form.ActiveControl != key || form.Text != $"SoulLink – Gastgeber | {server}" ||
                !serverInput.Parent!.SelectNextControl(serverInput, forward: true, tabStopOnly: true, nested: true, wrap: false) ||
                form.ActiveControl != key)
            {
                throw new InvalidOperationException("The host sign-in must expose a masked, focusable field in a server-specific window.");
            }
            key.SelectedText = new string('b', 64);
            serverInput.Text = "https://example.invalid";
            if (key.Text.Length != 0 || form.Text != "SoulLink – Gastgeber | https://example.invalid")
                throw new InvalidOperationException("Changing the server must clear the entered key and update the password manager target.");
            serverInput.Text = server;
            key.SelectedText = new string('b', 64);
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
            var archived = new HostRun(Guid.NewGuid(), "Alter Discord-Run", RunPhase.Finished, false, "", 2, 0)
            {
                DiscordArchive = new HostDiscordArchive("Rubin", new DateTime(2025, 2, 2, 16, 0, 0, DateTimeKind.Utc),
                    new DateTime(2025, 2, 8, 20, 0, 0, DateTimeKind.Utc), "Wipe"),
            };
            await ConnectAndWait(nextRun, ReplyToGroupAndRunsAsync());
            VerifyRoundedWindow(nextRun);
            if (nextRun.Session?.Server != server)
                throw new InvalidOperationException("Opening the host controls in the next run changed the authenticated server.");
            string[] visibleActions = Descendants(nextRun).OfType<Button>().Where(button => button.Visible).Select(button => button.Text).ToArray();
            if (!visibleActions.Contains("Pausieren") || !visibleActions.Contains("Links bearbeiten") ||
                !visibleActions.Contains("Discord-Runs importieren") || !visibleActions.Contains("Selbst beitreten") ||
                !visibleActions.Contains("Statistik") ||
                visibleActions.Contains("Starten") || visibleActions.Contains("Fortsetzen") || visibleActions.Contains("Für mich merken"))
                throw new InvalidOperationException("The selected active run did not expose exactly its applicable actions.");
            SaveToolPreview(nextRun, home, "host");
            var runChoices = Descendants(nextRun).OfType<ListBox>().Single(list => list.AccessibleName == "Runs");
            if (runChoices.Items.Count != 2)
                throw new InvalidOperationException("The host run list must show the live run and the imported Discord run.");
            runChoices.SelectedIndex = 1;
            if (!Descendants(nextRun).OfType<Label>().Any(label => label.Visible &&
                label.Text.Contains("Discord-Archiv", StringComparison.Ordinal) && label.Text.Contains("Abgeschlossen", StringComparison.Ordinal) &&
                label.Text.Contains("Wipe", StringComparison.Ordinal)) ||
                Descendants(nextRun).OfType<Button>().Any(button => button.Visible &&
                    button.Text is "Abschließen" or "Pausieren" or "Links bearbeiten" or "Selbst beitreten" or "Einladung kopieren" or "Spieler und Spielstände"))
            {
                throw new InvalidOperationException("Selecting the archived run must show its details without live controls.");
            }

            nextRun.Close();

            async Task ReplyToGroupAndRunsAsync()
            {
                await ReplyAsync(groupsPath, adminKey, "200 OK", Protocol.Serialize(new[] { group })).ConfigureAwait(false);
                await ReplyAsync($"{groupsPath}/{group.Id:D}/runs?includeArchived=true", adminKey, "200 OK", Protocol.Serialize(new[] { run, archived })).ConfigureAwait(false);
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

    internal static void VerifyRunConfirmation()
    {
        const string title = "Run abschließen";
        using var owner = new Form();
        owner.Show();
        Verify(confirmed: false);
        Verify(confirmed: true);

        void Verify(bool confirmed)
        {
            bool cancelIsDefault = false;
            using var poll = new System.Windows.Forms.Timer { Interval = 25 };
            poll.Tick += (_, _) =>
            {
                if (Application.OpenForms.OfType<ToolWindow>().SingleOrDefault(form => form.Modal && form.Text == title) is not { } dialog)
                    return;
                poll.Stop();

                var cancel = dialog.CancelButton as Button;
                cancelIsDefault = cancel is { Visible: true, Enabled: true } && dialog.ActiveControl == cancel && dialog.AcceptButton == cancel;
                var action = confirmed ? Descendants(dialog).OfType<Button>().FirstOrDefault(button => button.Text == title) : cancel;
                if (action is null) dialog.DialogResult = DialogResult.Abort;
                else action.PerformClick();
            };
            poll.Start();
            bool result = ToolWindow.Confirm(owner, title, "Diesen Run abschließen?", title);
            if (!cancelIsDefault || result != confirmed)
                throw new InvalidOperationException("Run confirmation must default to cancel and require an explicit confirmation.");
        }
    }

    internal static void VerifyPlayerDiagnostics(string home)
    {
        var diagnostics = new PlayerDiagnostics(home);
        const string secret = "Private invitation, token, and ROM path.";
        diagnostics.Write("test.failure", new InvalidOperationException(secret));
        string log = Path.Combine(diagnostics.DirectoryPath, "player.jsonl");
        string capture = File.ReadAllText(log);
        if (!capture.Contains("test.failure", StringComparison.Ordinal) ||
            !capture.Contains(nameof(InvalidOperationException), StringComparison.Ordinal) ||
            capture.Contains(secret, StringComparison.Ordinal))
            throw new InvalidOperationException("Diagnostics must record failure types without saving raw exception messages.");
        File.WriteAllBytes(log, new byte[512 * 1024]);
        diagnostics.Write("test.rotation");
        if (!File.Exists(Path.Combine(diagnostics.DirectoryPath, "player.previous.jsonl")) ||
            new FileInfo(log).Length >= 512 * 1024)
            throw new InvalidOperationException("The diagnostics log did not rotate at its size limit.");
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
        var pendingEnd = new PendingRunEnd(Guid.NewGuid(), Guid.NewGuid());
        var participant = new ClientPlayerView("Mitspieler", Guid.NewGuid(), "", "Offline");
        var run = new ClientRunView("Test", RunPhase.Active, Guid.NewGuid(), [participant], [], [], PendingEnd: pendingEnd);
        var view = runtime.Status() with { Run = run, Registered = true, LocalPlayerId = participant.Id, Snapshot = null };
        if (CompanionForm.PendingEndForParticipant(view) != pendingEnd ||
            CompanionForm.PendingEndForParticipant(view with { LocalPlayerId = Guid.NewGuid() }) is not null)
            throw new InvalidOperationException("A registered participant must see the pending run end even after exiting mGBA.");

        if (Environment.GetEnvironmentVariable("SOULLINK_SMOKE_MAP_ROM") is { Length: > 0 } romPath)
            VerifyMapHover(romPath, home);
        using (var end = new RunEndForm(Guid.NewGuid(), _ => Task.CompletedTask))
        {
            end.Show();
            Application.DoEvents();
            VerifyRoundedWindow(end);
            if (end.FormBorderStyle != FormBorderStyle.None || Descendants(end).OfType<ToolCard>().Count() != 1 ||
                !Descendants(end).OfType<Button>().Any(button => button.Text == "Run abschließen" && button.ForeColor == DashboardTheme.Danger))
                throw new InvalidOperationException("The run-end notice must use the themed window and destructive action color.");
            SaveToolPreview(end, home, "run-end");
        }
        using (var profile = new PlayerIdentityForm("Testspieler", PlayerMarker.Heart, 0x49DDE4, editing: true))
        {
            profile.Show();
            Application.DoEvents();
            var name = Descendants(profile).OfType<TextBox>().Single(input => input.AccessibleName == "Spielername");
            if (profile.SelectedColor != 0x49DDE4 || profile.Marker != PlayerMarker.Heart || profile.PlayerName != "Testspieler" || name.ReadOnly)
                throw new InvalidOperationException("The profile must restore the selected appearance.");
            name.Text = "Neuer Name";
            if (profile.PlayerName != "Neuer Name")
                throw new InvalidOperationException("The player name must be editable in the existing profile.");
            SaveToolPreview(profile, home, "player-appearance");
        }
        VerifyTradeWindows(home);
        VerifyItemMenu(runtime, home);

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
        VerifyGroupStatistics(home);
    }

    private static void VerifyMapHover(string path, string home)
    {
        var labels = RubyMapArtwork.ReadLabels(path) ?? throw new InvalidDataException("The Ruby map labels could not be read.");
        if (labels.Names.GetValueOrDefault(0) != "WURZELHEIM" ||
            !labels.LandmarkNames(19, 1, null).SequenceEqual(["BLÜTENBURGWALD"]))
            throw new InvalidDataException("The tooltip did not read the PokéNav town and route text from the ROM.");
        byte[] flags = new byte[13];
        flags[7] = 1 << 5; // FLAG_LANDMARK_MR_BRINEY_HOUSE, 0x83D.
        if (labels.LandmarkNames(19, 1, flags).Count() != 2)
            throw new InvalidDataException("The PokéNav tooltip did not reveal the unlocked landmark.");

        var tile = (from y in Enumerable.Range(0, 15)
                    from x in Enumerable.Range(0, 28)
                    where RubyDashboardCatalog.AreaAt(x, y) == 19 && RubyDashboardCatalog.LandmarkCellAt(x, y) == 1
                    select new Point(x, y)).Single();
        using var map = new HoennMap { Size = new Size(454, 264) };
        var artwork = RubyMapArtwork.Read(path) ?? throw new InvalidDataException("The Ruby map artwork could not be read.");
        map.SetArtwork(artwork, labels);
        map.UpdatePlayers([], flags);
        int mouseX = 3 + tile.X * 16 + 8;
        int mouseY = 22 + tile.Y * 16 + 8;
        SendMessage(map.Handle, 0x0200, 0, mouseX | (mouseY << 16));
        using var bitmap = new Bitmap(map.Width, map.Height);
        map.DrawToBitmap(bitmap, map.ClientRectangle);
        bitmap.Save(Path.Combine(home, "map-hover.png"));
    }

    private static void VerifyGroupStatistics(string home)
    {
        var date = new DateTimeOffset(2025, 2, 2, 16, 0, 0, TimeSpan.Zero);
        var live = new RunStatistics(Guid.NewGuid(), "Aktiver Run", false, RunPhase.Active, date, null, null, 12, 3, 4);
        var archived = new RunStatistics(Guid.NewGuid(), "Alter Run", true, RunPhase.Finished, date.AddDays(-10), date.AddDays(-2), "Wipe", null, 5, 3);
        var oldest = archived with { Id = Guid.NewGuid(), Name = "Erster Run", Date = date.AddDays(-30), Catches = 0, Deaths = null, Arenas = null };
        var timeline = new GroupTimeline("Rubin-Runde",
        [
            new RunTimeline("Alter Run", true, date.AddDays(-10),
                [new(TimelineEventKind.Start, date.AddDays(-10), "Archiv gestartet"), new(TimelineEventKind.Death, null, "Archiv-Tod")]),
            new RunTimeline("Aktiver Run", false, date,
                [new(TimelineEventKind.Start, date, "Run gestartet"), new(TimelineEventKind.Catch, date.AddMinutes(10), "Run-Fang"),
                    new(TimelineEventKind.Arena, date.AddHours(1), "Arena erledigt")]),
        ]);
        using var statistics = new GroupStatisticsForm(
            _ => Task.FromResult(new GroupStatistics("Rubin-Runde", [live, archived, oldest])), _ => Task.FromResult(timeline));
        statistics.Show();
        Application.DoEvents();
        var grid = Descendants(statistics).OfType<DataGridView>().Single();
        var labels = Descendants(statistics).OfType<Label>().Select(label => label.Text).ToArray();
        if (grid.Rows.Count != 3 || !labels.Contains("Rubin-Runde") || !labels.Contains("3 / 2") ||
            !labels.Contains("12") || !labels.Contains("8") || !labels.Contains("4") ||
            !Equals(grid.Rows[1].Cells[1].Value, "Discord") || !Equals(grid.Rows[1].Cells[4].FormattedValue, "–"))
        {
            throw new InvalidOperationException("Group statistics must show live and archived runs, known totals and unknown values.");
        }

        grid.CurrentCell = grid.Rows[1].Cells[0];
        if (!Descendants(statistics).OfType<Label>().Any(label => label.Text.Contains("Wipe", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("Selecting an archived run must show its end reason.");
        }

        var charts = Descendants(statistics).OfType<RunMetricChart>().ToArray();
        if (charts.Length != 3 || charts.Any(chart => !chart.Visible || chart.ClientSize.Height < 60))
        {
            throw new InvalidOperationException("The window must display all three run charts.");
        }

        var catches = charts.Single(chart => chart.AccessibleName == "Fänge je Run");
        VerifyChartHover(catches, live);
        SaveToolPreview(statistics, home, "statistics");

        Descendants(statistics).OfType<Button>().Single(button => button.Text == "Timeline").PerformClick();
        Application.DoEvents();
        var replay = statistics.OwnedForms.OfType<GroupTimelineForm>().Single();
        var lanes = Descendants(replay).OfType<TimelineLanes>().Single();
        var progress = Descendants(replay).OfType<TrackBar>().Single();
        if (progress.Maximum != 3 || lanes.StepCount != 3)
            throw new InvalidOperationException("Timeline playback must use the longest lane, not concatenate the runs.");
        Descendants(replay).OfType<Button>().Single(button => button.Text == "Abspielen").PerformClick();
        var wait = System.Diagnostics.Stopwatch.StartNew();
        while (progress.Value == 0 && wait.ElapsedMilliseconds < 1500)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }

        var pause = Descendants(replay).OfType<Button>().SingleOrDefault(button => button.Text == "Pause");
        if (progress.Value != 1 || pause is null || lanes.AccessibleDescription is not { } started ||
            !started.Contains("Archiv gestartet", StringComparison.Ordinal) || !started.Contains("Run gestartet", StringComparison.Ordinal))
            throw new InvalidOperationException("One playback tick must advance every run at the same time.");
        pause.PerformClick();
        Descendants(replay).OfType<Button>().Single(button => button.Text == "Weiter").PerformClick();
        if (lanes.AccessibleDescription is not { } second || !second.Contains("Zeitpunkt unbekannt · Archiv-Tod", StringComparison.Ordinal) ||
            !second.Contains("Run-Fang", StringComparison.Ordinal))
            throw new InvalidOperationException("Step controls must preserve concurrent run events and unknown dates.");
        SaveToolPreview(replay, home, "timeline");
    }

    private static void VerifyChartHover(RunMetricChart chart, RunStatistics latest)
    {
        using var image = new Bitmap(chart.Width, chart.Height);
        chart.DrawToBitmap(image, chart.ClientRectangle);
        int accent = DashboardTheme.Accent.ToArgb();
        var dots = new List<Point>();
        for (int y = 0; y < image.Height; y++)
        {
            for (int x = 0; x < image.Width; x++)
            {
                if (image.GetPixel(x, y).ToArgb() == accent) dots.Add(new Point(x, y));
            }
        }

        if (!dots.Any(dot => dot.X < chart.Width / 3) || !dots.Any(dot => dot.X > chart.Width * 2 / 3) ||
            dots.Any(dot => dot.X >= chart.Width / 3 && dot.X <= chart.Width * 2 / 3))
        {
            throw new InvalidOperationException("The chart must plot zero values and leave a gap at missing values.");
        }

        Point hit = dots.OrderByDescending(dot => dot.X).First();
        SendMessage(chart.Handle, 0x0200, 0, (hit.Y << 16) | hit.X);
        string detail = chart.AccessibleDescription ?? "";
        if (!detail.Contains(latest.Name, StringComparison.Ordinal) || !detail.Contains("Fänge  12", StringComparison.Ordinal) ||
            !detail.Contains(latest.Date.LocalDateTime.ToString("d"), StringComparison.Ordinal) ||
            !detail.Contains("SoulLink", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Hovering a plotted point must expose the matching run, date, source and metric.");
        }

        SendMessage(chart.Handle, 0x0200, 0, (hit.Y << 16) | (chart.Width / 2));
        if (string.Equals(chart.AccessibleDescription, detail, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Leaving the plotted point must remove its hover details.");
        }
    }

    private static void VerifyTradeWindows(string home)
    {
        var offer = new TradeView(Guid.NewGuid(), "Route 101", PokemonTradePhase.Offered,
            new TradePlayer(Guid.NewGuid(), "Alex", new PokemonState { Species = 67, Level = 27, Hp = 79, MaxHp = 83, MetArea = 16 }),
            new TradePlayer(Guid.NewGuid(), "Sam", new PokemonState { Species = 25, Container = Placement.Box, Box = 0, Slot = 1, MetArea = 16 }));
        using var sprites = new RubySprites(Environment.GetEnvironmentVariable("SOULLINK_SMOKE_SPRITES") ?? Path.Combine(home, "sprites"));
        using (var trade = new PokemonTradeOfferForm(offer, sprites, _ => Task.CompletedTask))
        {
            trade.Show();
            Application.DoEvents();
            VerifyRoundedWindow(trade);
            var options = Descendants(trade).OfType<Button>().ToArray();
            var accept = options.Single(button => button.Text == "Akzeptieren");
            var decline = options.Single(button => button.Text == "Ablehnen");
            var labels = Descendants(trade).OfType<Label>().ToArray();
            var offerGrid = Descendants(trade).OfType<RosterGrid>().Single();
            if (accept.ForeColor != DashboardTheme.Accent || decline.ForeColor != DashboardTheme.Danger ||
                !labels.Any(label => label.Text.Contains("Route 101", StringComparison.Ordinal)) ||
                !labels.Any(label => label.Text.Contains("Alex", StringComparison.Ordinal)) ||
                offerGrid.ContentSize.IsEmpty || offerGrid.ContentSize.Height > offerGrid.Viewport.Height)
                throw new InvalidOperationException("The offer must show the link and both Pokémon cards without scrolling.");
            var buttonBounds = Rectangle.Union(accept.RectangleToScreen(accept.ClientRectangle),
                decline.RectangleToScreen(decline.ClientRectangle));
            var dialogBounds = trade.RectangleToScreen(trade.ClientRectangle);
            if (accept.Top != decline.Top || Math.Abs(buttonBounds.Left + buttonBounds.Right - dialogBounds.Left - dialogBounds.Right) > 2)
                throw new InvalidOperationException("The trade buttons must sit together at the horizontal center of the dialog.");
            SaveToolPreview(trade, home, "pokemon-trade");
        }

        using var selection = new ToolWindow("Poké-Tausch", new Size(880, 274));
        var grid = new RosterGrid(sprites, null) { Dock = DockStyle.Fill };
        selection.Body.Controls.Add(grid);
        Guid row = Guid.NewGuid();
        var own = new DashboardPokemon(offer.To.Pokemon, false, true, Guid.NewGuid());
        var partner = new DashboardPokemon(offer.From.Pokemon, false, true, Guid.NewGuid());
        grid.Render(new RosterGridData(
            [new RosterColumn(offer.To.Id, "Sam"), new RosterColumn(offer.From.Id, "Alex"), new RosterColumn(Guid.NewGuid(), "Robin")],
            [new RosterRow(true, [own, partner, null], offer.Link, LinkId: row)], ShowRouteColumn: true));
        grid.HighlightTrade(row, own.Id, partner.Id);
        selection.Show();
        Application.DoEvents();
        using var bitmap = new Bitmap(grid.Width, grid.Height);
        grid.DrawToBitmap(bitmap, grid.ClientRectangle);
        int cardWidth = (grid.ContentSize.Width - 154 - 36) / 3;
        int ownX = 154;
        int targetX = ownX + cardWidth + 18;
        var rowColor = bitmap.GetPixel(145, 134);
        var targetColor = bitmap.GetPixel(targetX + 18, 134);
        var ownColor = bitmap.GetPixel(ownX + 18, 134);
        if (rowColor == DashboardTheme.Background || ownColor.G <= targetColor.G || targetColor.G <= rowColor.G ||
            bitmap.GetPixel(targetX + 3, 121).ToArgb() != DashboardTheme.Accent.ToArgb() ||
            bitmap.GetPixel(ownX + 3, 121).ToArgb() == DashboardTheme.Accent.ToArgb())
            throw new InvalidOperationException("Only the partner card should have an outline. The selected row and own card use fills.");
        SaveToolPreview(selection, home, "pokemon-trade-selection");
    }

    private static void VerifyItemMenu(ClientRuntime runtime, string home)
    {
        using var sprites = new RubySprites(Environment.GetEnvironmentVariable("SOULLINK_SMOKE_SPRITES") ?? Path.Combine(home, "sprites"));
        using var items = new ItemMenuForm(runtime, sprites);
        var destination = Descendants(items).OfType<DashboardComboBox>().Single();
        if (!destination.Items.Cast<string>().SequenceEqual(["Item-Lager", "Rucksack"]) || destination.SelectedIndex != 0)
            throw new InvalidOperationException("The cheat menu must default to item storage and offer the bag.");
        items.Show();
        SaveToolPreview(items, home, "item-destination");
        destination.SelectedIndex = 1;
        using var reopened = new ItemMenuForm(runtime, sprites);
        if (Descendants(reopened).OfType<DashboardComboBox>().Single().SelectedIndex != 0)
            throw new InvalidOperationException("Reopening the cheat menu must select item storage again.");
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
        using (var picker = new ProfilePicker(profiles, saved.Id, runtime.Status(), ProfilePickerMode.Both))
        {
            picker.Show();
            var runs = Descendants(picker).OfType<DashboardComboBox>().Single();
            if (runs.Items.Count != 2 || runs.SelectedIndex != 1 || runs.GetItemText(runs.SelectedItem) != "Rubin-Runde")
                throw new InvalidOperationException("The run picker must select the saved run after populating the choices.");
            Descendants(picker).OfType<Button>().Single(button => button.Text == "Run laden").PerformClick();
            if (picker.DialogResult != DialogResult.OK || picker.SelectedProfile != saved.Id)
                throw new InvalidOperationException("The run picker did not open the selected run.");
        }
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
        string tradeCheckpoint = emulator.TradeCheckpointPrefix(cartridge) + Guid.NewGuid().ToString("N") + "-after.ss";
        File.WriteAllText(tradeCheckpoint, "completed trade");
        File.SetLastWriteTimeUtc(tradeCheckpoint, time.AddSeconds(3));
        if (emulator.LatestState(cartridge) != tradeCheckpoint) throw new InvalidDataException("The newer trade checkpoint must be selected.");

        void VerifyChoices(PlayerProfile? profile, RomSelection selection, string[] expected)
        {
            using var form = new CompanionForm(runtime, emulator, selection, Path.Combine(home, "Bridge"), null, profile, home, profiles: profiles, testSlot: 2);
            form.Show();
            Application.DoEvents();
            VerifyRoundedWindow(form);
            var wiki = Descendants(form).OfType<Button>().Single(button => button.Text == "PokéWiki");
            if (!wiki.Visible || !wiki.Enabled || !form.ClientRectangle.Contains(form.RectangleToClient(wiki.RectangleToScreen(wiki.ClientRectangle))))
                throw new InvalidOperationException("PokéWiki must be reachable from the launcher toolbar.");
            if (profile is null)
            {
                VerifyReusablePokemonDetails(form);
                VerifyPlayerIcon(form);
                var screen = Screen.FromControl(form);
                form.MaximizeAtTopEdge(new Point(screen.Bounds.Left, screen.Bounds.Top + 100), screen);
                if (form.WindowState != FormWindowState.Normal)
                    throw new InvalidOperationException("Dragging away from the top edge must retain the normal window.");
                form.MaximizeAtTopEdge(screen.Bounds.Location, screen);
                if (form.WindowState != FormWindowState.Maximized || form.Region is not null)
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

    private static void VerifyReusablePokemonDetails(CompanionForm player)
    {
        var first = new PokemonState { Species = 25, Hp = 15, MaxHp = 20 };
        var second = new PokemonState { Species = 1, Hp = 17, MaxHp = 25 };
        player.ShowPokemonAsync(player, Guid.Empty, first).GetAwaiter().GetResult();
        var details = player.OwnedForms.OfType<PokemonDetailsForm>().Single();
        var originalBounds = details.Bounds;
        if (!details.Visible || details.Modal || details.Text != RubyNames.PokemonWithNickname(first))
            throw new InvalidOperationException("The first team Pokémon must open a modeless detail window.");

        var older = new TaskCompletionSource<PokemonRomDetails?>();
        var loading = details.DisplayAsync(first, null, _ => older.Task);
        player.ShowPokemonAsync(player, Guid.Empty, second).GetAwaiter().GetResult();
        older.SetResult(null);
        Application.DoEvents();
        var content = Descendants(details).OfType<DashboardScrollView>().Single();
        if (!loading.IsCompletedSuccessfully || player.OwnedForms.OfType<PokemonDetailsForm>().Single() != details ||
            details.Bounds != originalBounds || details.Text != RubyNames.PokemonWithNickname(second) ||
            content.AccessibleName?.Contains("KP: 17/25", StringComparison.Ordinal) != true)
            throw new InvalidOperationException("Selecting another Pokémon must reuse the window and discard the previous lookup.");

        Descendants(details).OfType<Button>().Single(button => button.Text == "Schließen").PerformClick();
        if (!details.IsDisposed) throw new InvalidOperationException("The modeless detail window must close from its button.");
    }

    private static void VerifyPlayerIcon(Form form)
    {
        const uint getIconMessage = 0x007F;
        const int smallIcon = 0;
        nint handle = SendMessage(form.Handle, getIconMessage, smallIcon, 0);
        using var windowIcon = handle == 0 ? null : Icon.FromHandle(handle);
        using var shellIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        if (!HasLinkArtwork(windowIcon) || !HasLinkArtwork(shellIcon))
        {
            throw new InvalidOperationException("The window and packaged executable must display the SoulLink icon.");
        }

        static bool HasLinkArtwork(Icon? icon)
        {
            if (icon is null) return false;
            using var bitmap = icon.ToBitmap();
            bool blue = false;
            bool green = false;
            for (int y = 0; y < bitmap.Height; y++)
            {
                for (int x = 0; x < bitmap.Width; x++)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (pixel.A < 220 || pixel.R > 160 || pixel.G < 175) continue;
                    blue |= pixel.B > pixel.G + 20;
                    green |= pixel.G > pixel.B + 20;
                }
            }

            return blue && green;
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
        Application.DoEvents();
        VerifyRoundedWindow(chart);
        if (chart.FormBorderStyle != FormBorderStyle.None || Descendants(chart).OfType<ToolCard>().Count(card => card.Visible) != 7 ||
            !Descendants(chart).OfType<Button>().Any(button => button.AccessibleName == "Schließen"))
            throw new InvalidOperationException("The type chart must use the themed tool frame and matchup cards.");

        var selector = Descendants(chart).OfType<FlowLayoutPanel>().Single(panel => panel.AccessibleName == "Pokémon-Typen");
        var types = selector.Controls.OfType<Button>().ToDictionary(button => button.Text);
        if (types.Count != 17 || !types.ContainsKey("Feuer"))
            throw new InvalidOperationException("The type chart should expose all 17 types as buttons.");

        var matchups = Descendants(chart).OfType<FlowLayoutPanel>().Where(panel => panel != selector)
            .ToDictionary(panel => panel.AccessibleName!);
        var immunities = matchups["0×  Immun gegen"];
        if (types["Normal"].AccessibleDescription != "Ausgewählt" ||
            immunities.Controls.OfType<Button>().Single().Text != "Geist")
            throw new InvalidOperationException("The initial Normal selection must show its immunity to Ghost.");

        types["Feuer"].PerformClick();
        var strong = matchups["2×  Sehr effektiv gegen"].Controls.OfType<Button>().Select(button => button.Text).ToHashSet();
        var weak = matchups["2×  Schwach gegen"].Controls.OfType<Button>().Select(button => button.Text).ToHashSet();
        if (types["Feuer"].AccessibleDescription != "Ausgewählt" || types["Normal"].AccessibleDescription is not null ||
            !strong.SetEquals(["Eis", "Käfer", "Pflanze", "Stahl"]) || !weak.SetEquals(["Boden", "Gestein", "Wasser"]))
            throw new InvalidOperationException("Changing the selected type must refresh both directions of the Ruby matchups.");

        var secondType = Descendants(chart).OfType<DashboardComboBox>().Single();
        secondType.SelectedIndex = secondType.FindStringExact("Flug");
        if (!matchups["4×  Sehr schwach gegen"].Controls.OfType<Button>().Select(button => button.Text).SequenceEqual(["Gestein"]) ||
            !immunities.Controls.OfType<Button>().Select(button => button.Text).SequenceEqual(["Boden"]) ||
            !matchups["¼×  Stark resistent gegen"].Controls.OfType<Button>().Select(button => button.Text).ToHashSet().SetEquals(["Käfer", "Pflanze"]))
            throw new InvalidOperationException("Fire and Flying must combine weaknesses, resistances and immunities.");
        SaveToolPreview(chart, home, "type-chart");

        chart.Width = Math.Min(chart.Width, chart.LogicalToDeviceUnits(550));
        types["Stahl"].PerformClick();
        Application.DoEvents();
        foreach (var row in matchups.Values.Append(selector).Where(row => row.Visible))
        {
            if (row.Controls.Cast<Control>().Any(control => !row.ClientRectangle.Contains(control.Bounds)))
                throw new InvalidOperationException($"The narrow type chart clipped a type in {row.AccessibleName}.");
        }
        secondType.SelectedIndex = 0;
        if (matchups["4×  Sehr schwach gegen"].Visible || matchups["¼×  Stark resistent gegen"].Visible)
            throw new InvalidOperationException("Removing the second type must restore the single-type chart.");
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
