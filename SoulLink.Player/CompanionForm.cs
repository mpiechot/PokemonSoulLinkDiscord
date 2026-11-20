using System.Diagnostics;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class CompanionForm : Form
{
    private readonly ClientRuntime runtime;
    private readonly PortableMgba emulator;
    private readonly RomSelection rom;
    private readonly string bridgeDirectory;
    private readonly string? requestedRom;
    private readonly PlayerProfile? profile;
    private readonly Label status = new();
    private readonly Label gameStatus = new();
    private readonly Panel surface = new();
    private readonly EmulatorDock dock;
    private readonly RunDashboard dashboard;
    private readonly Button startGame;
    private readonly Button toggleDock;
    private readonly Button focusGame;
    private readonly Button joinRun;
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 500 };
    private bool started;
    private bool launching;
    private bool dockingEnabled = true;
    private DateTimeOffset dockUntil;
    private Process? game;
    private string? gameError;
    private string? joinError;

    public CompanionForm(ClientRuntime runtime, PortableMgba emulator, RomSelection rom, string bridgeDirectory,
        string? requestedRom, PlayerProfile? profile, string sprites)
    {
        this.runtime = runtime;
        this.emulator = emulator;
        this.rom = rom;
        this.bridgeDirectory = bridgeDirectory;
        this.requestedRom = requestedRom;
        this.profile = profile;
        this.dock = new EmulatorDock(this.surface);
        this.Text = "SoulLink";
        this.ClientSize = new Size(1215, 745);
        this.MinimumSize = new Size(870, 575);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = DashboardTheme.Background;
        this.ForeColor = DashboardTheme.Text;
        this.Font = new Font("Segoe UI", 10);
        this.AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 63));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 37));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 59));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 0, 0, 8) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 110));
        var brand = new Label
        {
            Text = "SoulLink",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 21, FontStyle.Bold),
            ForeColor = DashboardTheme.Accent,
        };
        this.status.Dock = DockStyle.Fill;
        this.status.ForeColor = DashboardTheme.Muted;
        this.status.TextAlign = ContentAlignment.MiddleLeft;
        this.status.AutoEllipsis = true;
        this.joinRun = Button("Beitreten", async (_, _) => await this.OpenIdentityAsync());
        bar.Controls.Add(brand, 0, 0);
        bar.Controls.Add(this.status, 1, 0);
        bar.Controls.Add(this.joinRun, 2, 0);
        bar.Controls.Add(Button("Einrichtung", this.OpenSetup), 3, 0);

        var gamePane = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 0, 9, 0) };
        gamePane.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        gamePane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var gameBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        gameBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        gameBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
        gameBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        gameBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 133));
        var caption = new Label { Text = "SPIEL", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = DashboardTheme.Muted };
        this.startGame = Button("Spiel starten", async (_, _) => await this.StartGameAsync());
        this.toggleDock = Button("mGBA lösen", (_, _) => this.ChangeDock());
        this.focusGame = Button("Spiel fokussieren", (_, _) => this.dock.FocusGame());
        gameBar.Controls.Add(caption, 0, 0);
        gameBar.Controls.Add(this.startGame, 1, 0);
        gameBar.Controls.Add(this.toggleDock, 2, 0);
        gameBar.Controls.Add(this.focusGame, 3, 0);

        this.surface.Dock = DockStyle.Fill;
        this.surface.BackColor = Color.FromArgb(8, 14, 21);
        this.gameStatus.Dock = DockStyle.Fill;
        this.gameStatus.TextAlign = ContentAlignment.MiddleCenter;
        this.gameStatus.ForeColor = DashboardTheme.Muted;
        this.gameStatus.Padding = new Padding(24);
        this.surface.Controls.Add(this.gameStatus);
        this.surface.SizeChanged += (_, _) => this.dock.Resize();
        this.dashboard = new RunDashboard(sprites);
        gamePane.Controls.Add(gameBar, 0, 0);
        gamePane.Controls.Add(this.surface, 0, 1);
        layout.Controls.Add(bar, 0, 0);
        layout.SetColumnSpan(bar, 2);
        layout.Controls.Add(gamePane, 0, 1);
        layout.Controls.Add(this.dashboard, 1, 1);
        this.Controls.Add(layout);

        this.refresh.Tick += (_, _) => this.RefreshGame();
        this.Shown += async (_, _) => await this.StartAsync();
        this.Render();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (!e.Cancel) this.dock.Dispose();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.refresh.Dispose();
        this.game?.Dispose();
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { this.runtime.StopAsync(stop.Token).GetAwaiter().GetResult(); }
        catch (Exception exception) when (exception is OperationCanceledException or System.Net.Sockets.SocketException) { }
        base.OnFormClosed(e);
    }

    private static Button Button(string text, EventHandler clicked)
    {
        var button = new Button
        {
            Text = text,
            Dock = DockStyle.Fill,
            FlatStyle = FlatStyle.Flat,
            BackColor = DashboardTheme.Panel,
            ForeColor = DashboardTheme.Text,
            Margin = new Padding(3),
        };
        button.FlatAppearance.BorderColor = DashboardTheme.Border;
        button.Click += clicked;
        return button;
    }

    private async Task StartAsync()
    {
        try
        {
            await this.runtime.StartAsync(CancellationToken.None);
            this.started = true;
            this.refresh.Start();
            var view = this.runtime.Status();
            if (this.profile is not null && !view.Registered && view.Player.Length == 0) await this.OpenIdentityAsync();
            await this.StartGameAsync(path: this.requestedRom);
        }
        catch (Exception exception)
        {
            this.gameError = exception.Message;
            this.Render();
        }
    }

    private async Task StartGameAsync(bool chooseRom = false, string? path = null)
    {
        if (!this.started || this.launching || this.game is { HasExited: false }) return;
        this.dock.Dispose();
        this.game?.Dispose();
        this.game = null;
        this.gameError = null;
        this.dashboard.SetMapArtwork(null);
        this.launching = true;
        this.Render();
        try
        {
            if (PortableMgba.IsAlreadyRunning())
                throw new InvalidOperationException("mGBA ist bereits geöffnet. Schließe es und klicke auf „Spiel starten“.");

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
            this.dashboard.SetMapArtwork(RubyMapArtwork.Read(path), attempted: true);
            this.dockingEnabled = true;
            this.dockUntil = DateTimeOffset.UtcNow.AddSeconds(12);
            this.RefreshGame();
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

    private void RefreshGame()
    {
        if (this.dockingEnabled && this.game is { HasExited: false } && !this.dock.Attached && DateTimeOffset.UtcNow < this.dockUntil)
        {
            if (this.dock.TryAttach(this.game)) this.dock.FocusGame();
        }
        this.Render();
    }

    private void ChangeDock()
    {
        if (this.dock.Attached)
        {
            this.dockingEnabled = false;
            this.dock.Dispose();
        }
        else
        {
            this.dockingEnabled = true;
            this.dockUntil = DateTimeOffset.UtcNow.AddSeconds(5);
        }
        this.RefreshGame();
    }

    private void Render()
    {
        var view = this.runtime.Status();
        bool running = this.game is { HasExited: false };
        string registration = view.Run is not null ? view.Run.Name + "  ·  " + view.Player
            : view.Registered ? "Verbindung zum Run wird hergestellt"
            : view.Player.Length > 0 ? view.Player + "  ·  Anmeldung läuft"
            : "Lokaler Spieltest";
        this.status.Text = this.joinError ?? registration + (view.State.StartsWith("OutOfSync", StringComparison.Ordinal)
            ? "  ·  Erfassung pausiert" : view.Snapshot is null ? "  ·  Warte auf Spieldaten" : "  ·  Spieldaten verbunden");
        this.gameStatus.Visible = !this.dock.Attached;
        this.gameStatus.Text = this.gameError ?? (this.launching ? "mGBA startet …"
            : running ? "mGBA läuft separat. Du kannst das Fenster jederzeit einbetten."
            : "Wähle beim ersten Start die ROM für diesen Run.");
        this.startGame.Enabled = this.started && !running && !this.launching;
        this.toggleDock.Enabled = running;
        this.toggleDock.Text = this.dock.Attached ? "mGBA lösen" : "mGBA einbetten";
        this.focusGame.Visible = this.dock.Attached;
        this.joinRun.Visible = this.profile is not null && !view.Registered;
        this.dashboard.Render(view);
    }

    private async Task OpenIdentityAsync()
    {
        if (this.profile is null || this.runtime.Status().Registered) return;
        var view = this.runtime.Status();
        using var identity = new PlayerIdentityForm(view.Player.Length == 0 ? this.profile.Name : view.Player, view.Marker);
        if (identity.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await this.runtime.QueueJoinAsync(new JoinForm(this.profile.Server, this.profile.Invitation, identity.PlayerName, identity.Marker));
            this.joinError = null;
        }
        catch (InvalidDataException exception)
        {
            this.joinError = exception.Message;
        }
        this.Render();
    }

    private async void OpenSetup(object? sender, EventArgs e)
    {
        using var dialog = new SetupForm(this.runtime, !this.launching && this.game is not { HasExited: false }, this.profile);
        var result = dialog.ShowDialog(this);
        if (result == DialogResult.Retry) await this.StartGameAsync(chooseRom: true);
        else if (result == DialogResult.Yes) await this.OpenIdentityAsync();
        this.Render();
    }
}
