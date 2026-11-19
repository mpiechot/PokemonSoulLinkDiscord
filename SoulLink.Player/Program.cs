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
        using var instance = new Mutex(true, "Local\\SoulLink.Player", out bool firstInstance);
        if (!smokeTest && !firstInstance)
        {
            MessageBox.Show("SoulLink läuft bereits. Öffne das vorhandene SoulLink-Fenster.", "SoulLink");
            return;
        }
        string home = args.Length == 2 && (args[0] == "--smoke-test" || args[0] == "--data-directory") ? Path.GetFullPath(args[1]) :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoulLink", "Player");
        Directory.CreateDirectory(home);
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["SettingsPath"] = Path.Combine(home, profile is null ? "player.json" : $"player-{profile.Id:N}.json"),
            ["AllowSyntheticFrames"] = "false",
            ["BridgePort"] = "5091",
        }).Build();
        using var runtime = new ClientRuntime(configuration, autoJoin: profile is null ? null : new JoinForm(profile.Server, profile.Invitation, profile.Name));
        string bridge = BridgeInstaller.Install(Path.Combine(home, "Bridge"));
        if (smokeTest)
        {
            File.WriteAllText(Path.Combine(home, "smoke-result.json"), Protocol.Serialize(new
            {
                client = runtime.Status(), profileLoaded = profile is not null, profileName = profile?.Name,
                bridgeIncluded = File.Exists(bridge), browserStarted = false,
            }));
            return;
        }
        Application.Run(new CompanionForm(runtime, bridge));
    }
}
