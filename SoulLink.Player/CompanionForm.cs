using System.Diagnostics;
using System.Runtime.InteropServices;
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
    private readonly RunFeed feed;
    private readonly RosterGrid team;
    private readonly RubySprites spriteCache;
    private readonly DiscordIcons discordIcons = new();
    private readonly ToolTip hints = new();
    private readonly Label runConnection = ConnectionLabel("Run-Server");
    private readonly Label gameConnection = ConnectionLabel("Spieldaten");
    private readonly Button checkCatch;
    private readonly Button cheats;
    private readonly Button pendingDeathButton;
    private readonly ToolStripMenuItem startGame;
    private readonly ToolStripMenuItem toggleDock;
    private readonly ToolStripMenuItem joinRun;
    private readonly ContextMenuStrip programMenu = new();
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 500 };
    private bool started;
    private bool launching;
    private Task? gameLaunch;
    private bool closing;
    private bool closeApproved;
    private bool dockingEnabled = true;
    private bool startupScriptingClosed;
    private DateTimeOffset dockUntil;
    private Process? game;
    private string? gameError;
    private string? joinError;
    private string? shutdownError;
    private readonly HashSet<Guid> shownDeaths = [];
    private bool deathDialogOpen;
    private DeathReasonForm? activeDeath;
    private RosterGrid? openRoster;
    private RosterView openRosterFilter;
    private bool actionRunning;
    private Guid? displayedRunId;

    public CompanionForm(ClientRuntime runtime, PortableMgba emulator, RomSelection rom, string bridgeDirectory,
        string? requestedRom, PlayerProfile? profile, string sprites, string? artworkCache = null)
    {
        this.runtime = runtime;
        this.emulator = emulator;
        this.rom = rom;
        this.bridgeDirectory = bridgeDirectory;
        this.requestedRom = requestedRom;
        this.profile = profile;
        this.dock = new EmulatorDock(this.surface);
        this.Text = "SoulLink";
        this.FormBorderStyle = FormBorderStyle.None;
        var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        this.ClientSize = new Size(Math.Min(1720, workArea.Width), Math.Min(930, workArea.Height));
        this.MinimumSize = new Size(1060, 630);
        this.StartPosition = FormStartPosition.CenterScreen;
        this.BackColor = DashboardTheme.Background;
        this.ForeColor = DashboardTheme.Text;
        this.Font = new Font("Segoe UI", 10);
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.spriteCache = new RubySprites(sprites, artworkCache);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 3, Padding = new Padding(8) };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 19));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 49));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));

        var bar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1, Margin = new Padding(0, 0, 0, 5) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bar.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var brand = DashboardTheme.Button("SoulLink");
        brand.Name = "ProgramMenu";
        brand.AutoSize = false;
        brand.ForeColor = DashboardTheme.Accent;
        brand.Font = new Font("Segoe UI Semibold", 12);
        brand.Dock = DockStyle.Fill;
        brand.Click += (_, _) => this.programMenu.Show(brand, new Point(0, brand.Height));
        this.programMenu.BackColor = DashboardTheme.Panel;
        this.programMenu.ForeColor = DashboardTheme.Text;
        this.programMenu.Renderer = new ToolStripProfessionalRenderer(new ProgramMenuColors());
        this.startGame = new ToolStripMenuItem("Spiel starten", null, async (_, _) => await this.StartGameAsync());
        this.toggleDock = new ToolStripMenuItem("mGBA lösen", null, (_, _) => this.ChangeDock());
        this.joinRun = new ToolStripMenuItem("Run beitreten", null, async (_, _) => await this.OpenIdentityAsync());
        this.programMenu.Items.AddRange([this.startGame, this.toggleDock, this.joinRun, new ToolStripMenuItem("Einrichtung", null, this.OpenSetup)]);
        this.status.Dock = DockStyle.Fill;
        this.status.ForeColor = DashboardTheme.Muted;
        this.status.TextAlign = ContentAlignment.MiddleLeft;
        this.status.AutoEllipsis = true;
        this.status.Padding = new Padding(9, 0, 0, 0);
        this.status.MouseDown += (_, args) => { if (args.Button == MouseButtons.Left) this.DragWindow(); };
        this.status.DoubleClick += (_, _) => this.ToggleMaximize();
        var navigation = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        this.checkCatch = Button("Catch-Check", (_, _) =>
        {
            if (this.closing || this.displayedRunId is not Guid runId) return;
            using var dialog = new CatchCheckForm(this.runtime, runId);
            dialog.ShowDialog(this);
        });
        this.pendingDeathButton = Button("Todesfall prüfen", (_, _) => this.ReviewDeath());
        this.cheats = Button("Cheats", (_, _) =>
        {
            if (this.closing || this.game is not { HasExited: false } || !this.runtime.CanAddPcItems) return;
            using var dialog = new ItemMenuForm(this.runtime, this.spriteCache);
            dialog.ShowDialog(this);
        });
        this.hints.SetToolTip(this.cheats, "Je 99 Items ins lokale PC-Lager legen");
        navigation.Controls.AddRange([
            Button("Box", (_, _) => this.ShowRoster("Box", RosterView.Box)),
            Button("Tot", (_, _) => this.ShowRoster("Tot", RosterView.Dead)),
            Button("Shinies", (_, _) => this.ShowRoster("Shinies", RosterView.Shiny)),
            Button("Fangorte", (_, _) => this.ShowPlaces()),
            Button("Regeln", (_, _) =>
            {
                using var dialog = new RulesForm(this.runtime);
                dialog.ShowDialog(this);
            }),
            this.cheats,
            this.checkCatch, this.pendingDeathButton,
        ]);
        var chrome = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(3, 0, 0, 0) };
        var minimize = DashboardTheme.IconButton(DashboardIcon.Minimize, "Minimieren", DashboardTheme.Panel, DashboardTheme.Border, DashboardTheme.Text);
        var maximize = DashboardTheme.IconButton(DashboardIcon.Maximize, "Maximieren oder wiederherstellen", DashboardTheme.Panel, DashboardTheme.Border, DashboardTheme.Text);
        var close = DashboardTheme.IconButton(DashboardIcon.Close, "Schließen", Color.FromArgb(96, 38, 51), Color.FromArgb(155, 47, 65), DashboardTheme.Text);
        minimize.Click += (_, _) => this.WindowState = FormWindowState.Minimized;
        maximize.Click += (_, _) => this.ToggleMaximize();
        close.Click += (_, _) => this.Close();
        foreach (var button in new[] { minimize, maximize, close }) this.hints.SetToolTip(button, button.AccessibleName);
        chrome.Controls.AddRange([minimize, maximize, close]);
        bar.Controls.Add(brand, 0, 0);
        bar.Controls.Add(this.status, 1, 0);
        bar.Controls.Add(navigation, 2, 0);
        bar.Controls.Add(chrome, 3, 0);

        this.surface.Dock = DockStyle.Fill;
        this.surface.BackColor = Color.FromArgb(8, 14, 21);
        this.gameStatus.Dock = DockStyle.Fill;
        this.gameStatus.TextAlign = ContentAlignment.MiddleCenter;
        this.gameStatus.ForeColor = DashboardTheme.Muted;
        this.gameStatus.Padding = new Padding(24);
        this.surface.Controls.Add(this.gameStatus);
        this.surface.SizeChanged += (_, _) => this.dock.Resize();
        this.surface.GameClicked += () => this.dock.FocusGame();
        this.dashboard = new RunDashboard(this.discordIcons);
        this.dashboard.ArenaChanged += async (runId, number, complete) =>
            await this.RunActionAsync(() => this.runtime.ChangeArenaAsync(runId, number, complete));
        this.feed = new RunFeed(this.discordIcons, this.spriteCache) { Dock = DockStyle.Fill, BackColor = DashboardTheme.Surface };
        this.team = new RosterGrid(this.spriteCache) { Dock = DockStyle.Fill };
        var news = Section("Newsfeed", this.feed);
        var teamPanel = Section("Aktuelles Team", this.team);
        var sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        sidebar.Controls.Add(this.dashboard, 0, 0);
        sidebar.Controls.Add(teamPanel, 0, 1);

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(7, 4, 0, 0) };
        footer.Controls.AddRange([this.runConnection, this.gameConnection]);
        layout.Controls.Add(bar, 0, 0);
        layout.SetColumnSpan(bar, 3);
        layout.Controls.Add(news, 0, 1);
        layout.Controls.Add(this.surface, 1, 1);
        layout.Controls.Add(sidebar, 2, 1);
        layout.Controls.Add(footer, 0, 2);
        layout.SetColumnSpan(footer, 3);
        this.Controls.Add(layout);


        this.refresh.Tick += (_, _) => this.RefreshGame();
        this.Shown += async (_, _) => await this.StartAsync();
        this.Render();
    }

    internal bool IsGameDocked => this.dock.Attached;
    internal string ConnectionStatus => this.status.Text;

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.Cancel || this.closeApproved) return;
        if (e.CloseReason == CloseReason.WindowsShutDown)
        {
            if (this.game is { } shuttingDown) this.dock.RequestGameClose(shuttingDown);
            return;
        }
        e.Cancel = true;
        if (this.closing) return;
        this.closing = true;
        this.refresh.Stop();
        this.status.Text = "mGBA wird beendet …";
        this.BeginInvoke((Action)(async () => await this.ClosePlayerAsync()));
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        this.refresh.Dispose();
        this.dock.Dispose();
        this.game?.Dispose();
        this.programMenu.Dispose();
        this.hints.Dispose();
        this.spriteCache.Dispose();
        this.discordIcons.Dispose();
        base.OnFormClosed(e);
    }

    private async Task ClosePlayerAsync()
    {
        try
        {
            if (this.gameLaunch is not null) await this.gameLaunch;
            if (this.game is { HasExited: false } game)
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
                await this.dock.CloseGameAsync(game, timeout.Token);
            }
            this.status.Text = "Serververbindung und Lesebrücke werden beendet …";
            await this.runtime.StopAsync(CancellationToken.None);
            this.closeApproved = true;
            this.Close();
        }
        catch (Exception error) when (error is OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            this.closing = false;
            this.shutdownError = "Beenden noch nicht abgeschlossen. Prüfe mGBA und versuche es erneut.";
            this.Render();
            if (this.started) this.refresh.Start();
        }
    }

    private static Button Button(string text, EventHandler clicked)
    {
        var button = DashboardTheme.Button(text);
        button.Height = 32;
        button.Margin = new Padding(2, 4, 2, 0);
        button.Click += clicked;
        return button;
    }

    private static Control Section(string name, Control contents)
    {
        var panel = new RoundedSection { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = DashboardTheme.Surface, Padding = new Padding(6) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = DashboardTheme.Text }, 0, 0);
        panel.Controls.Add(contents, 0, 1);
        return panel;
    }

    private static Label ConnectionLabel(string name) => new()
    {
        Text = name,
        AutoSize = true,
        Tag = name,
        Margin = new Padding(0, 0, 16, 0),
    };

    private void SetConnection(Label label, bool online, string help)
    {
        string name = (string)label.Tag!;
        label.Text = name + ": ●";
        label.ForeColor = online ? DashboardTheme.Accent : DashboardTheme.Danger;
        string tooltip = online ? name + " verbunden." : help;
        label.AccessibleDescription = tooltip;
        if (this.hints.GetToolTip(label) != tooltip) this.hints.SetToolTip(label, tooltip);
    }

    private void ShowRoster(string title, RosterView filter)
    {
        if (this.closing) return;
        using var dialog = new Form();
        RunDialogs.Configure(dialog, title, new Size(Math.Clamp(this.ClientSize.Width - 150, 680, 1320), 560));
        var roster = new RosterGrid(this.spriteCache) { Dock = DockStyle.Fill };
        dialog.Controls.Add(roster);
        this.openRoster = roster;
        this.openRosterFilter = filter;
        roster.Render(this.runtime.Status(), filter);
        try { dialog.ShowDialog(this); }
        finally { this.openRoster = null; }
    }

    private void ShowPlaces()
    {
        if (this.closing) return;
        using var dialog = new Form();
        RunDialogs.Configure(dialog, "Fangorte", new Size(540, 330));
        var tags = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(4) };
        var places = (this.runtime.Status().Run?.CaughtAreas ?? []).Select(RubyNames.Area).Distinct().ToArray();
        if (places.Length == 0)
            tags.Controls.Add(new Label { Text = "Noch keine Fänge erfasst.", AutoSize = true, ForeColor = DashboardTheme.Muted });
        foreach (string place in places)
        {
            var tag = DashboardTheme.Button(place);
            tag.TabStop = false;
            tag.AccessibleRole = AccessibleRole.StaticText;
            tag.ForeColor = DashboardTheme.Accent;
            tags.Controls.Add(tag);
        }
        dialog.Controls.Add(tags);
        dialog.ShowDialog(this);
    }

    private void ToggleMaximize()
    {
        this.MaximizedBounds = Screen.FromControl(this).WorkingArea;
        this.WindowState = this.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
    }

    private void DragWindow()
    {
        ReleaseCapture();
        SendMessage(this.Handle, 0x00A1, 2, 0);
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg != 0x0084 || message.Result != 1 || this.WindowState != FormWindowState.Normal) return;
        long coordinates = message.LParam.ToInt64();
        var point = this.PointToClient(new Point((short)(coordinates & 0xFFFF), (short)((coordinates >> 16) & 0xFFFF)));
        int side = point.X < 7 ? -1 : point.X >= this.ClientSize.Width - 7 ? 1 : 0;
        if (point.Y < 7) message.Result = side switch { -1 => 13, 1 => 14, _ => 12 };
        else if (point.Y >= this.ClientSize.Height - 7) message.Result = side switch { -1 => 16, 1 => 17, _ => 15 };
        else if (side != 0) message.Result = side < 0 ? 10 : 11;
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);

    private sealed class ProgramMenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => DashboardTheme.Panel;
        public override Color ImageMarginGradientBegin => DashboardTheme.Panel;
        public override Color ImageMarginGradientMiddle => DashboardTheme.Panel;
        public override Color ImageMarginGradientEnd => DashboardTheme.Panel;
        public override Color MenuBorder => DashboardTheme.Border;
        public override Color MenuItemBorder => DashboardTheme.Border;
        public override Color MenuItemSelected => DashboardTheme.Border;
    }

    private sealed class RoundedSection : TableLayoutPanel
    {
        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.Clear(this.Parent?.BackColor ?? DashboardTheme.Background);
            DashboardTheme.Fill(e.Graphics, this.ClientRectangle, this.BackColor, 12);
        }
    }

    private async Task StartAsync()
    {
        try
        {
            await this.runtime.StartAsync(CancellationToken.None);
            if (this.closing || this.IsDisposed) return;
            this.started = true;
            this.refresh.Start();
            var view = this.runtime.Status();
            if (this.profile is not null && !view.Registered && view.Player.Length == 0) await this.OpenIdentityAsync();
            await this.StartGameAsync(path: this.requestedRom);
        }
        catch (Exception exception)
        {
            this.gameError = exception.Message;
            if (!this.closing && !this.IsDisposed) this.Render();
        }
    }

    private Task StartGameAsync(bool chooseRom = false, string? path = null)
    {
        if (this.closing || !this.started || this.game is { HasExited: false }) return Task.CompletedTask;
        if (this.launching) return this.gameLaunch ?? Task.CompletedTask;
        return this.gameLaunch = this.LaunchGameAsync(chooseRom, path);
    }

    private async Task LaunchGameAsync(bool chooseRom, string? path)
    {
        this.dock.Dispose();
        this.game?.Dispose();
        this.game = null;
        this.gameError = null;
        this.shutdownError = null;
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
            await this.runtime.SelectLocalGameAsync(path);
            if (this.closing) return;
            string bridge = BridgeInstaller.Install(this.bridgeDirectory, this.runtime.Status().BridgePort, this.runtime.BeginLocalItemSession());
            this.game = await Task.Run(() => this.emulator.Start(path, bridge));
            if (this.closing) return;
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
            if (!this.closing && !this.IsDisposed) this.Render();
        }
    }

    private void RefreshGame()
    {
        if (this.closing) return;
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
        if (this.activeDeath is { } popup && pending.FirstOrDefault(d => d.IncidentId == popup.IncidentId) is { } update)
            popup.UpdateTrace(update);
        if (!this.deathDialogOpen && !this.actionRunning && !this.OwnedForms.Any(form => form.Visible) &&
            view.Run is { } run && pending.FirstOrDefault(d => !this.shownDeaths.Contains(d.IncidentId)) is { } incident && this.Visible)
            this.BeginInvoke((Action)(() => this.ReviewDeath(incident, run.Id)));
    }

    internal void ChangeDock()
    {
        if (this.closing) return;
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
        this.cheats.Enabled = running && this.runtime.CanAddPcItems;
        string registration = view.IsLocalRun ? "Lokaler Spieltest"
            : view.Run is { } run ? run.Name + ": " + view.Player + " (" + (run.Phase switch
            {
                RunPhase.Lobby => "Lobby",
                RunPhase.Active => "Aktiv",
                RunPhase.Paused => "Pausiert",
                _ => "Abgeschlossen",
            }) + ")"
            : view.Registered ? view.Player : this.profile is null ? "Lokaler Spieltest" : "Run-Anmeldung";
        this.status.Text = this.shutdownError ?? this.joinError ?? registration;
        this.SetConnection(this.runConnection, view.Run is not null && !view.IsLocalRun,
            view.IsLocalRun && !view.Registered ? "Lokal. Für einen Run beitreten." : "Offline. Run-Server prüfen.");
        this.SetConnection(this.gameConnection, view.Snapshot is not null, "Offline. Spielstand in mGBA laden.");
        this.gameStatus.Visible = !this.dock.Attached;
        this.gameStatus.Text = this.gameError ?? (this.launching ? "mGBA startet …"
            : running ? "mGBA läuft separat. Im SoulLink-Menü einbetten."
            : "Wähle beim ersten Start die ROM für diesen Run.");
        this.startGame.Enabled = this.started && !running && !this.launching;
        this.toggleDock.Enabled = running;
        this.toggleDock.Text = this.dock.Attached ? "mGBA lösen" : "mGBA einbetten";
        this.joinRun.Available = this.profile is not null && !view.Registered;
        this.checkCatch.Enabled = view.Run is not null;
        this.pendingDeathButton.Visible = OwnDeaths(view).Length > 0;
        this.dashboard.Render(view);
        this.team.Render(view, RosterView.Team);
        this.openRoster?.Render(view, this.openRosterFilter);
        this.feed.Render(view.Run?.History ?? [], view.Run is { Phase: RunPhase.Lobby }
            ? "Der Feed startet mit dem Run." : "Noch keine Ereignisse.");
    }

    private static PendingDeath[] OwnDeaths(ClientView view) =>
        view.Run?.Players.SingleOrDefault(player => player.Id == view.LocalPlayerId)?.PendingDeaths ?? [];

    private void ReviewDeath(PendingDeath? pending = null, Guid? expectedRunId = null)
    {
        if (this.closing || this.deathDialogOpen || this.actionRunning || this.IsDisposed) return;
        var view = this.runtime.Status();
        if (view.Run is not { } run || (expectedRunId is Guid expected && expected != run.Id)) return;
        var deaths = OwnDeaths(view);
        pending ??= deaths.FirstOrDefault();
        if (pending is null || !deaths.Any(d => d.IncidentId == pending.IncidentId)) return;
        this.deathDialogOpen = true;
        this.shownDeaths.Add(pending.IncidentId);
        var dialog = new DeathReasonForm(pending);
        this.activeDeath = dialog;
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = this.PointToScreen(new Point(Math.Max(0, (this.ClientSize.Width - dialog.Width) / 2), 63));
        dialog.FormClosed += async (_, _) =>
        {
            this.activeDeath = null;
            try
            {
                if (dialog.Decision is DialogResult.Yes or DialogResult.No)
                    await this.RunActionAsync(() => this.runtime.ResolveFaintAsync(run.Id, pending.PokemonId, pending.IncidentId,
                        dialog.Decision == DialogResult.Yes, dialog.Reason));
            }
            finally { this.deathDialogOpen = false; }
        };
        dialog.Show(this);
    }

    private async Task RunActionAsync(Func<Task> action)
    {
        if (this.closing || this.actionRunning) return;
        this.actionRunning = true;
        try { await action(); }
        catch (Exception error) when (RunDialogs.IsActionError(error))
        {
            if (!this.closing) MessageBox.Show(this, "Die Änderung konnte gerade nicht gespeichert werden.", "SoulLink");
        }
        finally
        {
            this.actionRunning = false;
            if (!this.closing && !this.IsDisposed) this.Render();
        }
    }

    private async Task OpenIdentityAsync()
    {
        if (this.closing || this.profile is null || this.runtime.Status().Registered) return;
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
        if (this.closing) return;
        using var dialog = new SetupForm(this.runtime, !this.launching && this.game is not { HasExited: false }, this.profile);
        var result = dialog.ShowDialog(this);
        if (result == DialogResult.Retry) await this.StartGameAsync(chooseRom: true);
        else if (result == DialogResult.Yes) await this.OpenIdentityAsync();
        this.Render();
    }
}
