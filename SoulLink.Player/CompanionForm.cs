using System.Diagnostics;
using SoulLink;
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
    private readonly EmulatorSurface surface = new();
    private readonly EmulatorDock dock;
    private readonly RunDashboard dashboard;
    private readonly DashboardCards feed;
    private readonly Button checkCatch;
    private readonly Button pendingDeathButton;
    private readonly Button startGame;
    private readonly Button toggleDock;
    private readonly Button joinRun;
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 500 };
    private bool started;
    private bool launching;
    private bool dockingEnabled = true;
    private bool startupScriptingClosed;
    private DateTimeOffset dockUntil;
    private Process? game;
    private string? gameError;
    private string? joinError;
    private readonly HashSet<Guid> shownDeaths = [];
    private bool deathDialogOpen;
    private bool actionRunning;
    private Guid? displayedRunId;

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
        this.ClientSize = new Size(1420, 790);
        this.MinimumSize = new Size(1070, 620);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = DashboardTheme.Background;
        this.ForeColor = DashboardTheme.Text;
        this.Font = new Font("Segoe UI", 10);
        this.AutoScaleMode = AutoScaleMode.Dpi;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 3, Padding = new Padding(12) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 17));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 51));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 59));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 6, RowCount = 1, Margin = new Padding(0, 0, 0, 8) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 148));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
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
        this.checkCatch = Button("Catch-Check", (_, _) =>
        {
            if (this.displayedRunId is not Guid runId) return;
            using var dialog = new CatchCheckForm(this.runtime, runId);
            dialog.ShowDialog(this);
        });
        this.pendingDeathButton = Button("Todesfall prüfen", async (_, _) => await this.ReviewDeathAsync());
        bar.Controls.Add(brand, 0, 0);
        bar.Controls.Add(this.status, 1, 0);
        bar.Controls.Add(this.pendingDeathButton, 2, 0);
        bar.Controls.Add(this.checkCatch, 3, 0);
        bar.Controls.Add(this.joinRun, 4, 0);
        bar.Controls.Add(Button("Einrichtung", this.OpenSetup), 5, 0);

        var gamePane = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Margin = new Padding(0, 0, 9, 0) };
        gamePane.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        gamePane.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var gameBar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        gameBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        gameBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 122));
        gameBar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        var caption = new Label { Text = "SPIEL", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = DashboardTheme.Muted };
        this.startGame = Button("Spiel starten", async (_, _) => await this.StartGameAsync());
        this.toggleDock = Button("mGBA lösen", (_, _) => this.ChangeDock());
        gameBar.Controls.Add(caption, 0, 0);
        gameBar.Controls.Add(this.startGame, 1, 0);
        gameBar.Controls.Add(this.toggleDock, 2, 0);

        this.surface.Dock = DockStyle.Fill;
        this.surface.BackColor = Color.FromArgb(8, 14, 21);
        this.gameStatus.Dock = DockStyle.Fill;
        this.gameStatus.TextAlign = ContentAlignment.MiddleCenter;
        this.gameStatus.ForeColor = DashboardTheme.Muted;
        this.gameStatus.Padding = new Padding(24);
        this.surface.Controls.Add(this.gameStatus);
        this.surface.SizeChanged += (_, _) => this.dock.Resize();
        this.surface.GameClicked += () => this.dock.FocusGame();
        this.dashboard = new RunDashboard(sprites);
        this.dashboard.ArenaChanged += async (runId, number, complete) =>
            await this.RunActionAsync(() => this.runtime.ChangeArenaAsync(runId, number, complete));
        var news = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            BackColor = DashboardTheme.Surface,
            Padding = new Padding(6),
            Margin = new Padding(0, 0, 9, 0),
        };
        news.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        news.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        news.Controls.Add(new Label
        {
            Text = "Newsfeed",
            Dock = DockStyle.Fill,
            ForeColor = DashboardTheme.Text,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 0);
        this.feed = new DashboardCards { Dock = DockStyle.Fill, ItemHeight = 100 };
        news.Controls.Add(this.feed, 0, 1);
        gamePane.Controls.Add(gameBar, 0, 0);
        gamePane.Controls.Add(this.surface, 0, 1);
        layout.Controls.Add(bar, 0, 0);
        layout.SetColumnSpan(bar, 3);
        layout.Controls.Add(news, 0, 1);
        layout.Controls.Add(gamePane, 1, 1);
        layout.Controls.Add(this.dashboard, 2, 1);
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
            this.runtime.ResetLocalRun();
            string bridge = BridgeInstaller.Install(this.bridgeDirectory, this.runtime.Status().BridgePort);
            this.game = await Task.Run(() => this.emulator.Start(path, bridge));
            this.dashboard.SetMapArtwork(RubyMapArtwork.Read(path), attempted: true);
            this.dockingEnabled = true;
            this.startupScriptingClosed = false;
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
        if (!this.startupScriptingClosed && this.game is { HasExited: false } game && this.runtime.Status().Snapshot is not null)
        {
            this.startupScriptingClosed = true;
            EmulatorDock.CloseStartupScriptingWindow(game);
        }
        this.Render();
        var view = this.runtime.Status();
        var pending = OwnDeaths(view);
        if (!this.deathDialogOpen && !this.actionRunning && !this.OwnedForms.Any(form => form.Visible) &&
            view.Run is { } run && pending.FirstOrDefault(d => !this.shownDeaths.Contains(d.IncidentId)) is { } incident && this.Visible)
            this.BeginInvoke((Action)(async () => await this.ReviewDeathAsync(incident, run.Id)));
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
        this.displayedRunId = view.Run?.Id;
        bool running = this.game is { HasExited: false };
        string registration = view.IsLocalRun ? (view.Registered ? "Lokaler Spieltest  ·  Run-Server nicht verbunden" : "Lokaler Spieltest")
            : view.Run is not null ? view.Run.Name + "  ·  " + view.Player
            : view.Registered ? "Verbindung zum Run wird hergestellt"
            : view.Player.Length > 0 ? view.Player + "  ·  Anmeldung läuft"
            : "Lokaler Spieltest";
        this.status.Text = this.joinError ?? registration + (!view.IsLocalRun && view.State.StartsWith("OutOfSync", StringComparison.Ordinal)
            ? "  ·  Erfassung pausiert" : view.Snapshot is null ? "  ·  Warte auf Spieldaten" : "  ·  Spieldaten verbunden");
        this.gameStatus.Visible = !this.dock.Attached;
        this.gameStatus.Text = this.gameError ?? (this.launching ? "mGBA startet …"
            : running ? "mGBA läuft separat. Du kannst das Fenster jederzeit einbetten."
            : "Wähle beim ersten Start die ROM für diesen Run.");
        this.startGame.Enabled = this.started && !running && !this.launching;
        this.toggleDock.Enabled = running;
        this.toggleDock.Text = this.dock.Attached ? "mGBA lösen" : "mGBA einbetten";
        this.joinRun.Visible = this.profile is not null && !view.Registered;
        this.checkCatch.Enabled = view.Run is not null;
        this.pendingDeathButton.Visible = OwnDeaths(view).Length > 0;
        this.dashboard.Render(view);
        var events = view.Run?.History ?? [];
        this.feed.Render(events.Length > 0
            ? events.Reverse().Select(e => new DashboardEntry(RunDashboard.EventTitle(e.Kind), e.Message,
                e.At.ToLocalTime().ToString("HH:mm"))).ToArray()
            : [new DashboardEntry("Noch keine Ereignisse", view.Run is { Phase: RunPhase.Lobby }
                ? "Der Feed startet mit dem Run." : "Fänge und Run-Ereignisse erscheinen hier.")]);
    }

    private static PendingDeath[] OwnDeaths(ClientView view) =>
        view.Run?.Players.SingleOrDefault(player => player.Id == view.LocalPlayerId)?.PendingDeaths ?? [];

    private async Task ReviewDeathAsync(PendingDeath? pending = null, Guid? expectedRunId = null)
    {
        if (this.deathDialogOpen || this.actionRunning || this.IsDisposed) return;
        var view = this.runtime.Status();
        if (view.Run is not { } run || (expectedRunId is Guid expected && expected != run.Id)) return;
        var deaths = OwnDeaths(view);
        pending ??= deaths.FirstOrDefault();
        if (pending is null || !deaths.Any(d => d.IncidentId == pending.IncidentId)) return;
        this.deathDialogOpen = true;
        this.shownDeaths.Add(pending.IncidentId);
        try
        {
            using var dialog = new DeathReasonForm(pending);
            var choice = dialog.ShowDialog(this);
            if (choice is DialogResult.Yes or DialogResult.No)
                await this.RunActionAsync(() => this.runtime.ResolveFaintAsync(run.Id, pending.PokemonId, pending.IncidentId,
                    choice == DialogResult.Yes, dialog.Reason));
        }
        finally
        {
            this.deathDialogOpen = false;
            if (this.dock.Attached) this.dock.FocusGame();
        }
    }

    private async Task RunActionAsync(Func<Task> action)
    {
        if (this.actionRunning) return;
        this.actionRunning = true;
        try { await action(); }
        catch (Exception error) when (RunDialogs.IsActionError(error))
        {
            MessageBox.Show(this, "Die Änderung konnte gerade nicht gespeichert werden.", "SoulLink");
        }
        finally
        {
            this.actionRunning = false;
            if (!this.IsDisposed) this.Render();
        }
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
