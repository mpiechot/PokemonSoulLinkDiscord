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
                if (args.Length != 5) throw new InvalidDataException("Expected destination, server, invitation and name.");
                PlayerBundle.Create(executable, args[1], new PlayerProfile(Guid.NewGuid(), args[2], args[3], args[4]));
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
        bool smokeGame = args.Length is 3 or 4 && args[0] == "--smoke-game";
        using var instance = new Mutex(true, "Local\\SoulLink.Player", out bool firstInstance);
        if (!smokeTest && !smokeGame && !firstInstance)
        {
            MessageBox.Show("SoulLink läuft bereits. Öffne das vorhandene SoulLink-Fenster.", "SoulLink");
            return;
        }
        string home = smokeTest || smokeGame || (args.Length == 2 && args[0] == "--data-directory") ? Path.GetFullPath(args[1]) :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoulLink", "Player");
        Directory.CreateDirectory(home);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SettingsPath"] = Path.Combine(home, profile is null ? "player.json" : $"player-{profile.Id:N}.json"),
            ["AllowSyntheticFrames"] = "false",
            ["BridgePort"] = "0",
        }).Build();
        using var runtime = new ClientRuntime(configuration, autoJoin: smokeGame || profile is null ? null : new JoinForm(profile.Server, profile.Invitation, profile.Name));
        var emulator = new PortableMgba(home);
        string profileKey = profile is null ? "local" : profile.Id.ToString("N");
        var rom = new RomSelection(Path.Combine(home, $"rom-{profileKey}.txt"));
        if (smokeTest)
        {
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
            RunGameSmokeTestAsync(runtime, emulator, rom, home, args[2], args.Length == 4 ? Path.GetFullPath(args[3]) : null).GetAwaiter().GetResult();
            return;
        }
        string? requestedRom = args.Length == 1 ? Path.GetFullPath(args[0]) : null;
        Application.Run(new CompanionForm(runtime, emulator, rom, Path.Combine(home, "Bridge", profileKey), requestedRom));
    }

    private static async Task RunGameSmokeTestAsync(ClientRuntime runtime, PortableMgba emulator, RomSelection rom, string home, string path, string? testState)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            rom.Save(path);
            await runtime.StartAsync(timeout.Token);
            string bridge = BridgeInstaller.Install(Path.Combine(home, "Bridge"), runtime.Status().BridgePort);
            using var process = emulator.Start(rom.Read()!, bridge, testState);
            try
            {
                GameSnapshot? snapshot;
                while ((snapshot = runtime.Status().Snapshot) is null)
                {
                    if (process.HasExited) throw new InvalidOperationException($"mGBA exited with code {process.ExitCode}.");
                    await Task.Delay(100, timeout.Token);
                }
                File.WriteAllText(Path.Combine(home, "game-result.json"), Protocol.Serialize(snapshot));
            }
            finally
            {
                if (!process.HasExited)
                {
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
        }
    }
}
