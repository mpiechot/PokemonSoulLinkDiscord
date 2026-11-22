using Microsoft.Extensions.Configuration;
using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string executable = Environment.ProcessPath ?? throw new InvalidOperationException("Executable path unavailable.");
        if (args.Length > 0 && args[0] == "--apply-player-update")
        {
            PlayerUpdater.Apply(executable, args);
            return;
        }
        args = PlayerUpdater.ConsumeCleanup(args);
        if (args.Length > 0 && args[0] == "--prepare-player")
        {
            try
            {
                if (args.Length != 4) throw new InvalidDataException("Expected destination, server and invitation.");
                PlayerBundle.Create(executable, args[1], new PlayerProfile(Guid.NewGuid(), args[2], args[3]));
            }
            catch (Exception exception) when (exception is IOException or InvalidDataException)
            {
                Environment.ExitCode = 1;
            }
            return;
        }

        PlayerProfile? profile;
        try { profile = PlayerBundle.Read(executable); }
        catch (Exception exception)
        {
            MessageBox.Show(exception.Message, "SoulLink – Datei prüfen", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        bool smokeTest = args.Length == 2 && args[0] == "--smoke-test";
        bool smokeItems = args.Length == 3 && args[0] == "--smoke-pc-items";
        bool smokeGame = smokeItems || (args.Length is 3 or 4 && args[0] == "--smoke-game");
        bool smokeClose = args.Length is 3 or 4 && args[0] == "--smoke-close";
        bool customHome = args.Length is 2 or 3 && args[0] == "--data-directory";
        bool newTestPlayer = args is ["--new-test-player"];
        int? testSlot = args is ["--test-player-slot", string slotArgument, ..] &&
            int.TryParse(slotArgument, out int slot) && slot is >= 2 and <= PlayerInstance.MaxSlots ? slot : null;
        if (testSlot.HasValue) args = args[2..];
        Guid? selectedProfile = args is ["--run-profile", string selection] && Guid.TryParse(selection, out Guid selected) ? selected : null;
        bool smoke = smokeTest || smokeGame || smokeClose;
        using var instance = smoke ? null : PlayerInstance.Open(testSlot ?? (newTestPlayer ? 2 : 1),
            allowFallback: !testSlot.HasValue && !selectedProfile.HasValue && !customHome, wait: selectedProfile.HasValue);
        if (!smoke && instance is null)
        {
            MessageBox.Show("Der gewünschte Spielerplatz ist belegt. Schließe bei Bedarf ein Testfenster.", "SoulLink");
            return;
        }
        if (instance is { IsTest: false }) PlayerUpdater.CleanOldStages();
        string playerHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoulLink", "Player");
        string home = smokeTest || smokeGame || smokeClose || customHome ? Path.GetFullPath(args[1]) :
            instance is { IsTest: true } ? Path.Combine(playerHome, "TestPlayers", instance.Slot.ToString()) : playerHome;
        Directory.CreateDirectory(home);
        var profiles = new PlayerProfileLibrary(home);
        if (!smokeTest && !smokeGame && !smokeClose && !customHome)
        {
            try
            {
                profile = profiles.OpenForStartup(profile, selectedProfile);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                MessageBox.Show(error.Message, "SoulLink – Run-Profil", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }
        var emulator = new PortableMgba(home, preferSoftwareDisplay: smokeClose, isolated: instance?.IsTest == true);
        if (!smoke)
        {
            string? requestedRom = args.Length == 1 && !newTestPlayer ? Path.GetFullPath(args[0])
                : customHome && args.Length == 3 ? Path.GetFullPath(args[2]) : null;
            RunPlayer(args, executable, home, profile, customHome ? null : profiles, emulator,
                instance is { IsTest: true } ? instance.Slot : null, requestedRom);
            return;
        }
        using var runtime = CreateRuntime(home, profile, localTracking: !smokeTest);
        var rom = RomFor(home, profile);
        if (smokeTest)
        {
            try
            {
                using var artworkTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                DockSmokeTest.VerifyArtworkAsync(home, artworkTimeout.Token).GetAwaiter().GetResult();
                DockSmokeTest.VerifyScrollDesign();
                DockSmokeTest.VerifyRosterAndTypeArtwork(home);
                DockSmokeTest.VerifyFirstHostConnection(home);
                DockSmokeTest.VerifyIdleStartup(runtime, emulator, home);
                DockSmokeTest.VerifyLinkEditor();
                DockSmokeTest.VerifyTestPlayers(home);
            }
            catch (Exception error)
            {
                File.WriteAllText(Path.Combine(home, "package-error.txt"), error.ToString());
                Environment.ExitCode = 1;
                return;
            }
            string bridge = BridgeInstaller.Install(Path.Combine(home, "Bridge"));
            File.WriteAllText(Path.Combine(home, "smoke-result.json"), Protocol.Serialize(new
            {
                client = runtime.Status(),
                profileLoaded = profile is not null,
                profileName = profile?.Name,
                bridgeIncluded = File.Exists(bridge),
                emulatorIncluded = emulator.Available,
                browserStarted = false,
            }));
            return;
        }
        if (smokeGame)
        {
            DockSmokeTest.Run(runtime, emulator, rom, home, args[2], args.Length == 4 ? Path.GetFullPath(args[3]) : null, smokeItems);
            return;
        }
        if (smokeClose)
        {
            DockSmokeTest.RunClose(runtime, emulator, rom, home, args[2], detached: args.Length == 4 && args[3] == "detached",
                emptyParty: args.Length == 4 && args[3] == "empty");
            return;
        }
    }

    private static void RunPlayer(string[] args, string executable, string home, PlayerProfile? profile,
        PlayerProfileLibrary? profiles, PortableMgba emulator, int? testSlot, string? requestedRom)
    {
        HostingSession? session = null;
        bool restoreHosting = false;
        bool launchOnOpen = false;
        string sprites = Path.Combine(Path.GetDirectoryName(executable)!, "sprites");
        while (true)
        {
            string profileKey = profile?.Id.ToString("N") ?? "local";
            using var runtime = CreateRuntime(home, profile, localTracking: true);
            using var form = new CompanionForm(runtime, emulator, RomFor(home, profile), Path.Combine(home, "Bridge", profileKey),
                requestedRom, profile, sprites, Path.Combine(home, "Artwork"), profiles, testSlot, session, restoreHosting, launchOnOpen);
            Application.Run(form);
            if (form.PendingUpdate is { } update)
            {
                try { PlayerUpdater.StartInstaller(update, executable, args); }
                catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException or
                    System.ComponentModel.Win32Exception or InvalidOperationException)
                {
                    PlayerUpdater.CleanStage(update.Directory);
                    MessageBox.Show(error.Message, "SoulLink-Update fehlgeschlagen", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            if (form.RequestedProfile is not Guid runProfile || profiles is null) return;
            try
            {
                profile = runProfile == Guid.Empty ? null : profiles.Read(runProfile)
                    ?? throw new InvalidDataException("Das Run-Profil fehlt.");
            }
            catch (Exception error) when (RunDialogs.IsActionError(error))
            {
                MessageBox.Show(error.Message, "SoulLink – Run-Profil", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
            session = form.NextHostingSession;
            restoreHosting = form.RestoreHosting;
            launchOnOpen = true;
            requestedRom = null;
            args = ["--run-profile", runProfile.ToString("D")];
        }
    }

    private static ClientRuntime CreateRuntime(string home, PlayerProfile? profile, bool localTracking) => new(
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SettingsPath"] = Path.Combine(home, profile is null ? "player.json" : $"player-{profile.Id:N}.json"),
            ["AllowSyntheticFrames"] = "false",
            ["EnableLocalRun"] = (localTracking && profile is null).ToString(),
            ["LocalRunDirectory"] = Path.Combine(home, "LocalRuns"),
            ["BridgePort"] = "0",
        }).Build());

    private static RomSelection RomFor(string home, PlayerProfile? profile) =>
        new(Path.Combine(home, $"rom-{profile?.Id.ToString("N") ?? "local"}.txt"));
}
