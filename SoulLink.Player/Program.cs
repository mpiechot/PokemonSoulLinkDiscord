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
        using var instance = new Mutex(true, "Local\\SoulLink.Player", out bool firstInstance);
        if (!smokeTest && !smokeGame && !smokeClose && !firstInstance)
        {
            MessageBox.Show("SoulLink läuft bereits. Öffne das vorhandene SoulLink-Fenster.", "SoulLink");
            return;
        }
        bool customHome = args.Length is 2 or 3 && args[0] == "--data-directory";
        string home = smokeTest || smokeGame || smokeClose || customHome ? Path.GetFullPath(args[1]) :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoulLink", "Player");
        Directory.CreateDirectory(home);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SettingsPath"] = Path.Combine(home, profile is null ? "player.json" : $"player-{profile.Id:N}.json"),
            ["AllowSyntheticFrames"] = "false",
            ["EnableLocalRun"] = (profile is null && !smokeTest).ToString(),
            ["LocalRunDirectory"] = Path.Combine(home, "LocalRuns"),
            ["BridgePort"] = "0",
        }).Build();
        using var runtime = new ClientRuntime(configuration);
        var emulator = new PortableMgba(home, preferSoftwareDisplay: smokeClose);
        string profileKey = profile is null ? "local" : profile.Id.ToString("N");
        var rom = new RomSelection(Path.Combine(home, $"rom-{profileKey}.txt"));
        if (smokeTest)
        {
            try
            {
                using var artworkTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                DockSmokeTest.VerifyArtworkAsync(home, artworkTimeout.Token).GetAwaiter().GetResult();
                DockSmokeTest.VerifyScrollDesign();
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
            DockSmokeTest.RunClose(runtime, emulator, rom, home, args[2], detached: args.Length == 4 && args[3] == "detached");
            return;
        }
        string? requestedRom = args.Length == 1 ? Path.GetFullPath(args[0]) : customHome && args.Length == 3 ? Path.GetFullPath(args[2]) : null;
        string sprites = Path.Combine(Path.GetDirectoryName(executable)!, "sprites");
        Application.Run(new CompanionForm(runtime, emulator, rom, Path.Combine(home, "Bridge", profileKey), requestedRom, profile, sprites,
            Path.Combine(home, "Artwork")));
    }

}
