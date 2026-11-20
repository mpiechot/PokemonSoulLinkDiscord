using System.Diagnostics;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class CompanionForm : Form
{
    private static readonly Color Background = Color.FromArgb(17, 24, 39);
    private static readonly Color PanelColor = Color.FromArgb(28, 40, 57);
    private static readonly Color Muted = Color.FromArgb(158, 176, 199);
    private static readonly Color Accent = Color.FromArgb(121, 224, 190);
    private readonly ClientRuntime runtime;
    private readonly PortableMgba emulator;
    private readonly RomSelection rom;
    private readonly string bridgeDirectory;
    private readonly string? requestedRom;
    private readonly Label headline = new();
    private readonly Label message = new();
    private readonly Label server = new();
    private readonly Label map = new();
    private readonly Label[] party = new Label[6];
    private readonly Panel partyPanel = new();
    private readonly Button startGame;
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 1000 };
    private bool started;
    private bool launching;
    private Process? game;
    private string? gameError;
    private string? startupError;

    public CompanionForm(ClientRuntime runtime, PortableMgba emulator, RomSelection rom, string bridgeDirectory, string? requestedRom)
    {
        this.runtime = runtime;
        this.emulator = emulator;
        this.rom = rom;
        this.bridgeDirectory = bridgeDirectory;
        this.requestedRom = requestedRom;
        this.Text = "SoulLink";
        this.ClientSize = new Size(600, 510);
        this.MinimumSize = new Size(616, 549);
        this.MaximumSize = new Size(850, 700);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = Background;
        this.ForeColor = Color.White;
        this.Font = new Font("Segoe UI", 10);
        this.AutoScaleMode = AutoScaleMode.Dpi;

        var title = this.Label("SoulLink", 24, 20, 330, 40, 25, Color.White);
        title.Font = new Font(this.Font.FontFamily, 25, FontStyle.Bold);
        this.Controls.Add(title);
        this.Controls.Add(this.Label("Automatische Erfassung für deinen SoulLink-Run", 27, 67, 470, 25, 10, Muted));
        var setup = this.Button("Einrichtung", 448, 29, 125, 36, this.OpenSetup);
        this.Controls.Add(setup);
        this.headline.SetBounds(27, 120, 545, 35);
        this.headline.Font = new Font(this.Font.FontFamily, 18, FontStyle.Bold);
        this.headline.ForeColor = Accent;
        this.Controls.Add(this.headline);
        this.message.SetBounds(28, 165, 542, 62);
        this.message.ForeColor = Muted;
        this.Controls.Add(this.message);
        this.server.SetBounds(28, 237, 542, 40);
        this.server.ForeColor = Muted;
        this.Controls.Add(this.server);
        this.map.SetBounds(28, 288, 542, 24);
        this.Controls.Add(this.map);
        this.partyPanel.SetBounds(27, 320, 545, 125);
        for (int i = 0; i < this.party.Length; i++)
        {
            var card = this.Label("", (i % 3) * 183, (i / 3) * 62, 176, 56, 10, Color.White);
            card.BackColor = PanelColor;
            card.Padding = new Padding(10, 6, 4, 4);
            this.party[i] = card;
            this.partyPanel.Controls.Add(card);
        }
        this.Controls.Add(this.partyPanel);
        this.startGame = this.Button("Spiel starten", 27, 320, 158, 40, async (_, _) => await this.StartGameAsync());
        this.Controls.Add(this.startGame);
        this.Controls.Add(this.Label("Liest dein Spiel. Verändert keine Pokémon oder Spielstände.", 28, 470, 540, 25, 9, Muted));
        this.refresh.Tick += (_, _) => this.Render();
        this.Shown += async (_, _) => await this.StartAsync();
        this.FormClosed += (_, _) =>
        {
            this.refresh.Dispose();
            this.game?.Dispose();
        };
        this.Render();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { this.runtime.StopAsync(stop.Token).GetAwaiter().GetResult(); }
        catch (Exception exception) when (exception is OperationCanceledException or System.Net.Sockets.SocketException) { }
        base.OnFormClosed(e);
    }

    private async Task StartAsync()
    {
        try
        {
            await this.runtime.StartAsync(CancellationToken.None);
            this.started = true;
            this.refresh.Start();
            this.Render();
            await this.StartGameAsync(path: this.requestedRom);
        }
        catch (Exception exception)
        {
            this.startupError = exception is System.Net.Sockets.SocketException
                ? "Der lokale Client läuft bereits oder der mGBA-Port ist belegt. Beende die ältere Client-Instanz und öffne SoulLink erneut."
                : exception.Message;
            this.Render();
            if (this.started) this.refresh.Start();
        }
    }

    private async Task StartGameAsync(bool chooseRom = false, string? path = null)
    {
        if (!this.started || this.launching || this.game is { HasExited: false }) return;
        this.game?.Dispose();
        this.game = null;
        this.gameError = null;
        this.launching = true;
        this.Render();
        try
        {
            if (PortableMgba.IsAlreadyRunning())
                throw new InvalidOperationException("mGBA ist bereits geöffnet. Schließe es zuerst und klicke dann auf „Spiel starten“.");

            path ??= chooseRom ? null : this.rom.Read();
            if (path is null || !File.Exists(path))
            {
                using var picker = new OpenFileDialog
                {
                    Title = "ROM für diesen Run auswählen",
                    Filter = "Game Boy Advance (*.gba)|*.gba",
                    InitialDirectory = path is null ? AppContext.BaseDirectory : Path.GetDirectoryName(path),
                    CheckFileExists = true,
                };
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                path = picker.FileName;
            }
            this.rom.Save(path);
            string bridge = BridgeInstaller.Install(this.bridgeDirectory, this.runtime.Status().BridgePort);
            this.game = await Task.Run(() => this.emulator.Start(path, bridge));
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            this.gameError = exception.Message;
        }
        finally
        {
            this.launching = false;
            if (!this.IsDisposed) this.Render();
        }
    }

    private void Render()
    {
        ClientView view = this.runtime.Status();
        bool running = this.game is { HasExited: false };
        this.headline.Text = view.Snapshot is not null ? "Mit deinem Spiel verbunden" : "Spiel starten";
        this.message.Text = view.Snapshot is not null
            ? "Du kannst weiterspielen. SoulLink erfasst die Änderungen im Hintergrund."
            : "Wähle beim ersten Start die ROM für diesen Run.";
        if (this.launching)
        {
            this.headline.Text = "mGBA startet …";
            this.message.Text = "SoulLink verbindet sich automatisch mit dem Spiel.";
        }
        else if (running && view.Snapshot is null)
        {
            this.headline.Text = "Warte auf Spieldaten";
            this.message.Text = "Lade deinen Spielstand oder spiele bis zum ersten Pokémon im Team.";
        }
        else if (this.gameError is not null)
        {
            this.message.Text = this.gameError;
        }
        this.startGame.Visible = this.started && !this.launching && !running && view.Snapshot is null;
        this.server.Text = view.Registered
            ? (view.Run is not null ? $"{view.Run.Name} · {view.Player}" : $"{view.Player} · Verbindung zum Run wird hergestellt …")
            : (view.Player.Length > 0 ? $"{view.Player} · Anmeldung beim Gastgeber wird automatisch hergestellt …" :
                "Noch kein Run hinterlegt. Du kannst dein Spiel bereits lokal verbinden.");
        if (view.State.StartsWith("OutOfSync", StringComparison.Ordinal))
        {
            this.headline.Text = "Run-Zustand prüfen";
            this.message.Text = "Die automatische Erfassung ist pausiert. Bitte den Gastgeber auf den abweichenden Spielstand hinweisen.";
        }
        else if (view.Snapshot is not null && view.Registered && view.Run is null)
        {
            this.headline.Text = "Spiel verbunden · Run offline";
            this.message.Text = "Dein Spiel läuft weiter. Die automatische Run-Erfassung wartet auf den Gastgeber und prüft danach den aktuellen Zustand neu.";
        }
        this.partyPanel.Visible = view.Snapshot is not null;
        this.map.Visible = view.Snapshot is not null;
        this.map.Text = view.Snapshot?.AreaName ?? "";
        for (int i = 0; i < this.party.Length; i++)
        {
            var pokemon = view.Snapshot?.Pokemon.SingleOrDefault(p => p.Container == SoulLink.Placement.Party && p.Slot == i);
            this.party[i].Text = pokemon is null ? "Freier Platz" : $"{pokemon.DisplayName}\nLv {pokemon.Level} · HP {pokemon.Hp}/{pokemon.MaxHp}";
        }
        if (this.startupError is not null)
        {
            this.headline.Text = this.started ? "Anmeldung prüfen" : "Clientstart prüfen";
            this.message.Text = this.startupError;
        }
    }

    private async void OpenSetup(object? sender, EventArgs e)
    {
        using var dialog = new SetupForm(this.runtime, !this.launching && this.game is not { HasExited: false });
        if (dialog.ShowDialog(this) == DialogResult.Retry) await this.StartGameAsync(chooseRom: true);
        if (this.runtime.Status().Registered) this.startupError = null;
        if (this.started) this.Render();
    }

    internal Label Label(string text, int x, int y, int width, int height, float size, Color color)
    {
        return new Label
        {
            Text = text,
            Bounds = new Rectangle(x, y, width, height),
            ForeColor = color,
            Font = new Font(this.Font.FontFamily, size),
            AutoEllipsis = true
        };
    }

    private Button Button(string text, int x, int y, int width, int height, EventHandler click)
    {
        var button = new Button
        {
            Text = text,
            Bounds = new Rectangle(x, y, width, height),
            FlatStyle = FlatStyle.Flat,
            BackColor = PanelColor,
            ForeColor = Color.White
        };
        button.Click += click;
        return button;
    }
}
