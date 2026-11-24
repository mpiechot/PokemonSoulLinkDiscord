using System.Diagnostics;
using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class CompanionForm : PlayerWindow
{
    private readonly ClientRuntime runtime;
    private readonly PortableMgba emulator;
    private readonly RomSelection rom;
    private readonly string bridgeDirectory;
    private readonly string? requestedRom;
    private readonly PlayerProfile? profile;
    private readonly PlayerProfileLibrary? profiles;
    private readonly Label status = new();
    private readonly Label gameStatus = new();
    private readonly TableLayoutPanel launcher = new() { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 3 };
    private readonly Button resumeRun;
    private readonly Button loadRun;
    private readonly Button joinFromLauncher;
    private readonly Control news;
    private readonly Control sidebar;
    private readonly EmulatorSurface surface = new();
    private readonly EmulatorDock dock;
    private readonly RunDashboard dashboard;
    private readonly RunFeed feed;
    private readonly BattlePanel battles;
    private readonly RowStyle battleRow = new(SizeType.Absolute, 0);
    private readonly RowStyle catchRow = new(SizeType.Absolute, 0);
    private readonly CatchVerdictPanel encounterCheck = new() { AccessibleName = "Automatischer Catch-Check" };
    private readonly RosterGrid team;
    private readonly RubySprites spriteCache;
    private readonly DiscordIcons discordIcons = new();
    private readonly ToolTip hints = new();
    private readonly Label runConnection = ConnectionLabel("Run-Server");
    private readonly Label gameConnection = ConnectionLabel("Spieldaten");
    private readonly Button checkCatch;
    private readonly Button cheats;
    private readonly Button pendingDeathButton;
    private readonly Button pendingEndButton;
    private readonly ToolStripMenuItem saveGame;
    private readonly ToolStripMenuItem loadGame;
    private readonly ToolStripMenuItem startGame;
    private readonly ToolStripMenuItem toggleDock;
    private readonly ToolStripMenuItem joinRun;
    private readonly ToolStripMenuItem updateMenu;
    private readonly PlayerUpdater? updater;
    private readonly string? instanceLabel;
    private readonly CancellationTokenSource updateLifetime = new();
    private readonly ContextMenuStrip programMenu = new();
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 200 };
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
    private string? updateStatus;
    private PlayerUpdateManifest? availableUpdate;
    private bool checkingUpdate;
    private readonly HashSet<Guid> shownDeaths = [];
    private bool deathDialogOpen;
    private DeathReasonForm? activeDeath;
    private RosterGrid? openRoster;
    private RosterView openRosterFilter;
    private HostingForm? hosting;
    private readonly HostingSession? hostingSession;
    private RulesForm? rules;
    private TypeChartForm? typeChart;
    private StatusEffectsForm? statusEffects;
    private RunEndForm? runEnd;
    private Guid? shownRunEnd;
    private string? stateFeedback;
    private DateTimeOffset stateFeedbackUntil;
    private bool actionRunning;
    private Guid? displayedRunId;

    public CompanionForm(ClientRuntime runtime, PortableMgba emulator, RomSelection rom, string bridgeDirectory,
        string? requestedRom, PlayerProfile? profile, string sprites, string? artworkCache = null, PlayerProfileLibrary? profiles = null,
        int? testSlot = null, HostingSession? hostingSession = null, bool restoreHosting = false,
        bool launchOnOpen = false, bool resumeOnOpen = false)
    {
        this.runtime = runtime;
        this.emulator = emulator;
        this.rom = rom;
        this.bridgeDirectory = bridgeDirectory;
        this.requestedRom = requestedRom;
        this.profile = profile;
        this.profiles = profiles;
        this.hostingSession = hostingSession;
        this.updater = testSlot.HasValue ? null : PlayerUpdater.Create();
        this.instanceLabel = testSlot is int slot ? $"Testspieler {slot}" : null;
        this.dock = new EmulatorDock(this.surface);
        this.Text = this.instanceLabel is { } label ? $"SoulLink – {label}" : "SoulLink";
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
        this.startGame = new ToolStripMenuItem(profile is null ? "Lokalen Spieltest starten" : "Spiel starten", null, async (_, _) => await this.StartGameAsync());
        this.toggleDock = new ToolStripMenuItem("mGBA lösen", null, (_, _) => this.ChangeDock());
        this.joinRun = new ToolStripMenuItem(profiles is null ? "Run beitreten" : "Run beitreten / wechseln", null, async (_, _) => await this.JoinOrSwitchAsync());
        this.saveGame = new ToolStripMenuItem("Speichern (Slot 1)", null, async (_, _) => await this.ChangeGameStateAsync(load: false));
        this.loadGame = new ToolStripMenuItem("Schnellladen (Slot 1)", null, async (_, _) => await this.ChangeGameStateAsync(load: true));
        this.programMenu.Items.AddRange([this.joinRun, this.startGame, this.toggleDock, this.saveGame, this.loadGame,
            new ToolStripMenuItem("Einrichtung", null, this.OpenSetup)]);
        this.programMenu.Items.Add(new ToolStripMenuItem("Testspieler öffnen", null, (_, _) =>
            this.RunPlayerTestInstance()));
        this.updateMenu = new ToolStripMenuItem("Nach Updates suchen", null, async (_, _) => await this.CheckUpdatesAsync(interactive: true));
        if (this.updater is not null) this.programMenu.Items.Add(this.updateMenu);
        if (profiles is not null)
        {
            this.programMenu.Items.Insert(2, new ToolStripMenuItem("Gastgeber", null, async (_, _) => await this.OpenHostingAsync()));
            this.programMenu.Items.Insert(3, new ToolStripMenuItem("Run-Server neu verbinden", null, async (_, _) =>
            {
                if (!this.closing) await this.RunActionAsync(() => this.runtime.ReconnectAsync());
            }));
        }
        this.status.Dock = DockStyle.Fill;
        this.status.ForeColor = DashboardTheme.Muted;
        this.status.TextAlign = ContentAlignment.MiddleLeft;
        this.status.AutoEllipsis = true;
        this.status.Padding = new Padding(9, 0, 0, 0);
        this.status.MouseDown += (_, args) => { if (args.Button == MouseButtons.Left) ToolWindow.DragWindow(this.Handle); };
        this.status.DoubleClick += (_, _) => this.ToggleMaximize();
        var navigation = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
        this.checkCatch = Button("Catch-Check", (_, _) =>
        {
            if (this.closing || this.displayedRunId is not Guid runId) return;
            using var dialog = new CatchCheckForm(this.runtime, runId);
            dialog.ShowDialog(this);
        });
        this.pendingDeathButton = Button("Todesfall prüfen", (_, _) => this.ReviewDeath());
        this.pendingEndButton = Button("Run abschließen", (_, _) => this.ReviewRunEnd());
        this.cheats = Button("Cheats", (_, _) =>
        {
            if (this.closing || this.game is not { HasExited: false } || !this.runtime.CanAddPcItems) return;
            using var dialog = new ItemMenuForm(this.runtime, this.spriteCache);
            dialog.ShowDialog(this);
        });
        this.hints.SetToolTip(this.cheats, "Je 99 Items ins lokale PC-Lager legen");
        navigation.Controls.AddRange([
            Button("Regeln", (_, _) =>
            {
                if (this.rules is null || this.rules.IsDisposed) this.rules = new RulesForm(this.runtime);
                ShowTool(this.rules);
            }),
            Button("Typen", (_, _) =>
            {
                if (this.typeChart is null || this.typeChart.IsDisposed) this.typeChart = new TypeChartForm(this.spriteCache);
                ShowTool(this.typeChart);
            }),
            Button("Effekte", (_, _) =>
            {
                if (this.statusEffects is null || this.statusEffects.IsDisposed) this.statusEffects = new StatusEffectsForm();
                ShowTool(this.statusEffects);
            }),
            this.cheats, this.checkCatch, this.pendingDeathButton, this.pendingEndButton,
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
        this.resumeRun = Button("Run fortsetzen", async (_, _) =>
        {
            if (this.LastRunWithSave() is not Guid last) { this.Render(); return; }
            if (this.profiles is null) await this.StartGameAsync(resume: true);
            else await this.SwitchProfileAsync(last, resume: true);
        });
        this.resumeRun.ForeColor = DashboardTheme.Accent;
        this.loadRun = Button("Run laden", async (_, _) => await this.JoinOrSwitchAsync(ProfilePickerMode.Load));
        this.joinFromLauncher = Button("Run beitreten", async (_, _) => await this.JoinOrSwitchAsync(ProfilePickerMode.Join));
        var launchActions = new FlowLayoutPanel
        {
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Anchor = AnchorStyles.None, Margin = new Padding(0),
        };
        foreach (var button in new[] { this.resumeRun, this.loadRun, this.joinFromLauncher })
        {
            button.AutoSize = false;
            button.Size = new Size(190, 42);
            button.Margin = new Padding(0, 4, 0, 4);
            launchActions.Controls.Add(button);
        }
        this.launcher.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        this.launcher.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        this.launcher.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        this.launcher.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        this.launcher.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        this.launcher.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        this.launcher.Controls.Add(launchActions, 1, 1);
        this.surface.Controls.Add(this.launcher);
        this.launcher.BringToFront();
        this.surface.SizeChanged += (_, _) => this.dock.Resize();
        this.surface.GameClicked += () => this.dock.FocusGame();
        this.dashboard = new RunDashboard(this.discordIcons);
        this.dashboard.ArenaChanged += async (runId, number, complete) =>
            await this.RunActionAsync(() => this.runtime.ChangeArenaAsync(runId, number, complete));
        this.feed = new RunFeed(this.discordIcons, this.spriteCache) { Dock = DockStyle.Fill, BackColor = DashboardTheme.Surface };
        this.battles = new BattlePanel(this.spriteCache, this.discordIcons.Get("unknown-pokemon")) { Dock = DockStyle.Fill };
        this.battles.PokemonOpened += (player, pokemon) => this.ShowPokemon(this, player, pokemon);
        this.team = new RosterGrid(this.spriteCache, this.discordIcons.Get("unknown-pokemon")) { Dock = DockStyle.Fill };
        this.team.PokemonOpened += (player, pokemon) => this.ShowPokemon(this, player, pokemon);
        var places = Button("Fangorte", (_, _) => this.ShowPlaces());
        places.Font = new Font("Segoe UI", 8.5f);
        places.Margin = new Padding(0);
        this.news = Section("Newsfeed", this.feed, places);
        var teamPanel = Section("Aktuelles Team", this.team,
            this.RosterButton(DashboardIcon.Box, "Box", RosterView.Box, DashboardTheme.Accent),
            this.RosterButton(DashboardIcon.Skull, "Tot", RosterView.Dead, DashboardTheme.Text),
            this.RosterButton(DashboardIcon.Sparkle, "Shinies", RosterView.Shiny, DashboardTheme.Gold));
        var sidebar = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(0) };
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        sidebar.RowStyles.Add(this.catchRow);
        sidebar.RowStyles.Add(this.battleRow);
        sidebar.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        sidebar.Controls.Add(this.dashboard, 0, 0);
        sidebar.Controls.Add(this.encounterCheck, 0, 1);
        sidebar.Controls.Add(Section("Live-Kämpfe", this.battles), 0, 2);
        sidebar.Controls.Add(teamPanel, 0, 3);
        this.sidebar = sidebar;

        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0), Padding = new Padding(7, 4, 0, 0) };
        footer.Controls.AddRange([this.runConnection, this.gameConnection]);
        layout.Controls.Add(bar, 0, 0);
        layout.SetColumnSpan(bar, 3);
        layout.Controls.Add(this.news, 0, 1);
        layout.Controls.Add(this.surface, 1, 1);
        layout.Controls.Add(sidebar, 2, 1);
        layout.Controls.Add(footer, 0, 2);
        layout.SetColumnSpan(footer, 3);
        this.Controls.Add(layout);


        this.refresh.Tick += (_, _) => this.RefreshGame();
        this.Activated += (_, _) =>
        {
            if (!this.closing && !this.launching && this.game is not { HasExited: false }) this.Render();
        };
        this.Shown += async (_, _) =>
        {
            if (launchOnOpen || this.requestedRom is not null) await this.StartGameAsync(path: this.requestedRom, resume: resumeOnOpen);
            if (restoreHosting) await this.OpenHostingAsync();
            await this.CheckUpdatesAsync(interactive: false);
        };
        this.Render();
    }

    internal bool IsGameDocked => this.dock.Attached;
    internal string ConnectionStatus => this.status.Text;

    internal Guid? RequestedProfile { get; private set; }
    internal bool RequestedResume { get; private set; }

    internal HostingSession? NextHostingSession { get; private set; }

    internal bool RestoreHosting { get; private set; }
    internal StagedPlayerUpdate? PendingUpdate { get; private set; }

    private async Task CheckUpdatesAsync(bool interactive)
    {
        if (this.updater is not { } service || this.closing || this.checkingUpdate) return;
        this.checkingUpdate = true;
        this.updateMenu.Enabled = false;
        try
        {
            using var check = CancellationTokenSource.CreateLinkedTokenSource(this.updateLifetime.Token);
            check.CancelAfter(TimeSpan.FromSeconds(8));
            this.availableUpdate ??= await service.CheckAsync(check.Token);
            if (this.closing || this.IsDisposed) return;
            if (this.availableUpdate is not { } offer)
            {
                if (interactive) MessageBox.Show(this, "Deine SoulLink-Version ist aktuell.", "SoulLink-Update");
                return;
            }
            this.updateMenu.Text = $"Update {offer.Version} installieren";
            if (!interactive) return;
            if (PlayerInstance.HasTestPlayers())
            {
                MessageBox.Show(this, "Schließe die SoulLink-Testfenster vor dem Update.", "SoulLink-Update");
                return;
            }
            if (MessageBox.Show(this,
                $"Version {offer.Version} ist verfügbar. Speichere zuerst im Spiel. SoulLink beendet mGBA und startet nach der Installation neu.",
                "SoulLink aktualisieren", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK) return;

            this.updateStatus = $"Update {offer.Version} wird heruntergeladen …";
            this.Render();
            using var download = CancellationTokenSource.CreateLinkedTokenSource(this.updateLifetime.Token);
            download.CancelAfter(TimeSpan.FromMinutes(12));
            var staged = await service.StageAsync(offer, download.Token);
            if (this.closing || this.IsDisposed || PlayerInstance.HasTestPlayers())
            {
                PlayerUpdater.CleanStage(staged.Directory);
                if (!this.closing && !this.IsDisposed)
                    MessageBox.Show(this, "Schließe die SoulLink-Testfenster vor dem Update.", "SoulLink-Update");
                return;
            }
            this.PendingUpdate = staged;
            this.Close();
        }
        catch (Exception error) when (error is HttpRequestException or IOException or InvalidDataException or
            System.Text.Json.JsonException or OperationCanceledException or UnauthorizedAccessException)
        {
            this.availableUpdate = null;
            if (interactive && !this.closing && !this.IsDisposed)
                MessageBox.Show(this, error is OperationCanceledException ? "Zeitüberschreitung beim Update." : error.Message,
                    "SoulLink-Update", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            this.checkingUpdate = false;
            this.updateStatus = null;
            if (!this.IsDisposed && !this.closing)
            {
                this.updateMenu.Enabled = true;
                if (this.availableUpdate is null) this.updateMenu.Text = "Nach Updates suchen";
                this.Render();
            }
        }
    }

    private async Task JoinOrSwitchAsync(ProfilePickerMode mode = ProfilePickerMode.Both)
    {
        if (this.closing) return;
        if (this.profiles is null)
        {
            await this.OpenIdentityAsync();
            return;
        }
        Guid current = this.profile?.Id ?? Guid.Empty;
        var view = this.runtime.Status();
        using var dialog = new ProfilePicker(this.profiles, current, this.profile is { } profile ? view with { Server = profile.Server } : view, mode);
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (dialog.OpenGroupRuns)
        {
            await this.OpenHostingAsync();
            return;
        }

        await this.SwitchProfileAsync(dialog.SelectedProfile);
    }

    private async Task OpenHostingAsync()
    {
        if (this.closing || this.profiles is null) return;
        bool firstOpen = this.hosting is null;
        this.hosting ??= new HostingForm(this.profiles, this.profile?.Server ?? this.runtime.Status().Server, this.hostingSession, id => this.SwitchProfileAsync(id));
        this.ShowTool(this.hosting);
        if (firstOpen && this.hostingSession is not null) await this.hosting.ConnectAsync();
    }

    private async Task SwitchProfileAsync(Guid selection, bool resume = false)
    {
        if (this.closing) return;
        Guid current = this.profile?.Id ?? Guid.Empty;
        if (selection == current)
        {
            if (this.game is not { HasExited: false }) await this.StartGameAsync(resume: resume);
            else await this.OpenIdentityAsync();
            return;
        }
        if (this.game is { HasExited: false } &&
            MessageBox.Show(this, "Hast du im Spiel gespeichert? Beim Run-Wechsel wird mGBA geschlossen.",
                "Run wechseln", MessageBoxButtons.YesNo) != DialogResult.Yes) return;
        this.RequestedProfile = selection;
        this.RequestedResume = resume;
        this.NextHostingSession = this.hosting?.Session ?? this.hostingSession;
        this.RestoreHosting = this.hosting?.Visible == true;
        this.Close();
    }

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
        this.updateLifetime.Cancel();
        this.updateLifetime.Dispose();
        if (this.updater is null) this.updateMenu.Dispose();
        this.updater?.Dispose();
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
            this.RequestedProfile = null;
            this.closing = false;
            this.updateMenu.Enabled = true;
            if (this.PendingUpdate is { } update)
            {
                PlayerUpdater.CleanStage(update.Directory);
                this.PendingUpdate = null;
            }
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

    private Button RosterButton(DashboardIcon icon, string title, RosterView filter, Color foreground)
    {
        var button = DashboardTheme.IconButton(icon, title, DashboardTheme.Panel, DashboardTheme.Border, foreground);
        button.Size = new Size(30, 24);
        button.Margin = new Padding(2, 0, 0, 0);
        button.Click += (_, _) => this.ShowRoster(title, filter);
        this.hints.SetToolTip(button, title);
        return button;
    }

    private static Control Section(string name, Control contents, params Button[] actions)
    {
        var panel = new RoundedSection { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, BackColor = DashboardTheme.Surface, Padding = new Padding(6) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0) };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        heading.Controls.Add(new Label { Text = name, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = DashboardTheme.Text }, 0, 0);
        var buttons = new FlowLayoutPanel { AutoSize = true, Anchor = AnchorStyles.Right, WrapContents = false, Margin = new Padding(0) };
        buttons.Controls.AddRange(actions);
        heading.Controls.Add(buttons, 1, 0);
        panel.Controls.Add(heading, 0, 0);
        panel.Controls.Add(contents, 0, 1);
        return panel;
    }

    private void ShowTool(Form tool)
    {
        if (this.closing) return;
        if (!tool.Visible) tool.Show(this);
        tool.Activate();
    }

    private async Task ChangeGameStateAsync(bool load)
    {
        if (this.closing) return;
        try
        {
            this.dock.FocusGame();
            if (load) await this.runtime.LoadSlotOneAsync(this.updateLifetime.Token);
            else await this.runtime.SaveSlotOneAsync(this.updateLifetime.Token);
            this.stateFeedback = load ? "Spielstand aus Slot 1 geladen." : "Spielstand in mGBA auf Slot 1 gespeichert.";
        }
        catch (Exception error) when (RunDialogs.IsActionError(error))
        {
            this.stateFeedback = error.Message;
        }
        if (this.closing) return;
        this.stateFeedbackUntil = DateTimeOffset.UtcNow.AddSeconds(5);
        this.dock.FocusGame();
        this.Render();
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
        using var dialog = new PlayerWindow();
        RunDialogs.Configure(dialog, title, new Size(Math.Clamp(this.ClientSize.Width - 150, 680, 1320), 560));
        var roster = new RosterGrid(this.spriteCache, this.discordIcons.Get("unknown-pokemon")) { Dock = DockStyle.Fill };
        roster.PokemonOpened += (player, pokemon) => this.ShowPokemon(dialog, player, pokemon);
        dialog.Controls.Add(roster);
        this.openRoster = roster;
        this.openRosterFilter = filter;
        roster.Render(this.runtime.Status(), filter);
        try { dialog.ShowDialog(this); }
        finally { this.openRoster = null; }
    }

    private void ShowPokemon(Form owner, Guid player, PokemonState pokemon)
    {
        if (this.closing) return;
        var view = this.runtime.Status();
        bool partner = player != Guid.Empty && player != view.LocalPlayerId;
        using var dialog = new PokemonDetailsForm(pokemon, this.rom.Read(), partner && view.Run is { } run
            ? token => this.runtime.PartnerPokemonDetailsAsync(run.Id, player, pokemon.Species, token) : null);
        dialog.ShowDialog(owner);
        if (owner == this) this.dock.FocusGame();
    }

    private void ShowPlaces()
    {
        if (this.closing) return;
        using var dialog = new PlayerWindow();
        RunDialogs.Configure(dialog, "Fangorte", new Size(540, 330));
        var tags = new FlowLayoutPanel { Padding = new Padding(4), BackColor = DashboardTheme.Surface };
        var scroll = new DashboardScrollHost(tags, width => tags.GetPreferredSize(new Size(width, 0)).Height)
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Fangorte",
        };
        dialog.Controls.Add(scroll);
        (int Area, bool Failed)[]? displayed = null;
        void RenderPlaces()
        {
            var run = this.runtime.Status().Run;
            var failed = (run?.Links ?? []).Where(link => link.State == "FAILED").Select(link => link.AreaId).ToHashSet();
            var places = (run?.CaughtAreas ?? []).Select(area => (Area: area, Failed: failed.Contains(area))).ToArray();
            if (displayed is not null && displayed.SequenceEqual(places)) return;
            displayed = places;
            tags.SuspendLayout();
            foreach (var old in tags.Controls.Cast<Control>().ToArray()) old.Dispose();
            if (places.Length == 0)
                tags.Controls.Add(new Label { Text = "Noch keine Fangorte erfasst.", AutoSize = true, ForeColor = DashboardTheme.Muted });
            foreach (var place in places)
            {
                string name = RubyNames.Area(place.Area);
                var tag = DashboardTheme.Button(place.Failed ? $"{name} · Verpasst" : name);
                tag.TabStop = false;
                tag.AccessibleRole = AccessibleRole.StaticText;
                tag.ForeColor = place.Failed ? DashboardTheme.Danger : DashboardTheme.Accent;
                tags.Controls.Add(tag);
            }
            tags.ResumeLayout(performLayout: true);
            scroll.ResizeDocument();
        }

        using var refreshPlaces = new System.Windows.Forms.Timer { Interval = 350 };
        refreshPlaces.Tick += (_, _) => RenderPlaces();
        RenderPlaces();
        refreshPlaces.Start();
        dialog.ShowDialog(this);
    }

    private void ToggleMaximize()
    {
        this.MaximizedBounds = Screen.FromControl(this).WorkingArea;
        this.WindowState = this.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
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

    private Guid? LastRunWithSave()
    {
        Guid current = this.profile?.Id ?? Guid.Empty;
        Guid previous = this.profiles is null ? current : this.profiles.Current()?.Id ?? Guid.Empty;
        var previousRom = previous == current ? this.rom : this.rom.ForProfile(previous);
        return this.emulator.HasResumeState(previousRom.Read()) ? previous : null;
    }

    private Task StartGameAsync(bool chooseRom = false, string? path = null, bool resume = false)
    {
        if (this.closing || this.game is { HasExited: false }) return Task.CompletedTask;
        if (this.launching) return this.gameLaunch ?? Task.CompletedTask;
        return this.gameLaunch = this.LaunchGameAsync(chooseRom, path, resume);
    }

    private async Task LaunchGameAsync(bool chooseRom, string? path, bool resume)
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
            if (this.emulator.IsAlreadyRunning())
                throw new InvalidOperationException("mGBA ist bereits geöffnet. Schließe es und klicke auf „Spiel starten“.");

            path ??= chooseRom ? null : this.rom.Read();
            if (resume && !this.emulator.HasResumeState(path))
                throw new InvalidDataException("Für diesen Run gibt es keinen Spielstand in Slot 1 oder 2.");
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
            var view = this.runtime.Status();
            if (this.profile is not null && !view.Registered && view.Player.Length == 0)
            {
                await this.OpenIdentityAsync();
                if (this.runtime.Status().Player.Length == 0 || this.closing) return;
            }
            if (!this.started)
            {
                await this.runtime.StartAsync(this.updateLifetime.Token);
                this.started = true;
                if (this.closing) return;
            }
            string bridge = BridgeInstaller.Install(this.bridgeDirectory, this.runtime.Status().BridgePort, this.runtime.BeginLocalItemSession());
            this.game = await Task.Run(() => this.emulator.Start(path, bridge, resume: resume));
            if (this.closing) return;
            this.dashboard.SetMapArtwork(RubyMapArtwork.Read(path), attempted: true);
            this.dockingEnabled = true;
            this.startupScriptingClosed = false;
            this.dockUntil = DateTimeOffset.UtcNow.AddSeconds(12);
            this.refresh.Start();
            this.RefreshGame();
            this.profiles?.Choose(this.profile?.Id ?? Guid.Empty);
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
        if (!this.startupScriptingClosed && this.game is { HasExited: false } game && DateTimeOffset.UtcNow < this.dockUntil &&
            this.runtime.OwnedBridgeReady)
        {
            this.startupScriptingClosed = EmulatorDock.CloseStartupScriptingWindow(game);
        }
        this.Render();
        if (this.game is not { HasExited: false })
        {
            this.refresh.Stop();
            return;
        }
        var view = this.runtime.Status();
        var pending = OwnDeaths(view);
        if (this.activeDeath is { } popup && pending.FirstOrDefault(d => d.IncidentId == popup.IncidentId) is { } update)
            popup.UpdateTrace(update);
        if (!this.Visible || !this.Enabled || this.actionRunning || this.OwnedForms.Any(form => form.Visible && form.Modal)) return;
        if (OwnRunEnd(view) is { } end && this.shownRunEnd != end.Id)
            this.ReviewRunEnd(activate: false);
        else if (!this.deathDialogOpen && pending.FirstOrDefault(d => !this.shownDeaths.Contains(d.IncidentId)) is { } incident)
            this.ReviewDeath(incident, view.Run?.Id);
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
        bool running = this.game is { HasExited: false };
        this.displayedRunId = running ? view.Run?.Id : null;
        this.news.Visible = this.sidebar.Visible = running;
        bool idle = !running && !this.launching;
        this.launcher.Visible = idle;
        if (idle)
        {
            this.resumeRun.Visible = this.LastRunWithSave() is not null;
            this.loadRun.Visible = this.profiles?.List().Length > 0;
            this.joinFromLauncher.Visible = this.profiles is not null || this.profile is not null;
        }
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
        if (!running) registration = this.profile is { } profile ? profile.Label ?? "SoulLink" : "SoulLink";
        this.status.Text = this.shutdownError ?? this.joinError ?? (!running ? this.gameError : null) ??
            (DateTimeOffset.UtcNow < this.stateFeedbackUntil ? this.stateFeedback : null) ?? this.updateStatus ??
            (this.instanceLabel is { } label ? $"{label} | {registration}" : registration);
        this.SetConnection(this.runConnection, running && view.Run is not null && !view.IsLocalRun,
            !running ? "Kein Run geöffnet." : view.IsLocalRun && !view.Registered ? "Lokal. Für einen Run beitreten." : "Offline. Run-Server prüfen.");
        var sharedPlayer = view.IsLocalRun ? null : view.Run?.Players.FirstOrDefault(player => player.Id == view.LocalPlayerId);
        bool synchronized = view.Snapshot is not null && (sharedPlayer is null || sharedPlayer.Connection == "Ready");
        string gameHint = sharedPlayer?.Connection switch
        {
            "OutOfSync" => "Anderer Spielstand. Lade den Spielstand dieses Runs.",
            "Synchronizing" => "Warte auf gültige Spieldaten für den Run.",
            "Stale" => "Keine aktuellen Daten aus mGBA.",
            _ => this.runtime.OwnedBridgeReady ? "mGBA verbunden. Neues Spiel beginnen oder Spielstand laden." : "Spiel in mGBA öffnen.",
        };
        this.SetConnection(this.gameConnection, running && synchronized, gameHint);
        this.gameStatus.Visible = !idle && !this.dock.Attached;
        this.gameStatus.Text = this.gameError ?? (this.launching ? "mGBA startet …"
            : running ? "mGBA läuft separat. Im SoulLink-Menü einbetten."
            : this.profiles is not null && this.profile is null ? "Füge deine Einladung unter „SoulLink → Run beitreten / wechseln“ ein."
            : "Wähle beim ersten Start die ROM für diesen Run.");
        this.startGame.Enabled = !running && !this.launching;
        this.toggleDock.Enabled = running;
        this.saveGame.Enabled = running && this.runtime.CanSaveState;
        this.loadGame.Enabled = running && this.runtime.CanLoadState;
        this.toggleDock.Text = this.dock.Attached ? "mGBA lösen" : "mGBA einbetten";
        this.joinRun.Available = this.profiles is not null || (this.profile is not null && !view.Registered);
        this.checkCatch.Enabled = this.displayedRunId is not null;
        this.pendingDeathButton.Visible = running && OwnDeaths(view).Length > 0;
        var pendingEnd = running ? OwnRunEnd(view) : null;
        this.pendingEndButton.Visible = pendingEnd is not null;
        if (this.runEnd is { IsDisposed: false } end && end.IncidentId != pendingEnd?.Id) end.Close();
        if (!running) return;
        this.dashboard.Render(view);
        this.RenderEncounterCheck(view);
        int battleHeight = this.battles.Render(view);
        float rowHeight = battleHeight == 0 ? 0 : Math.Min(160, battleHeight + 33);
        if (this.battleRow.Height != rowHeight) this.battleRow.Height = rowHeight;
        this.team.Render(view, RosterView.Team);
        this.openRoster?.Render(view, this.openRosterFilter);
        this.feed.Render(view.Run?.History ?? [], view.Run is { Phase: RunPhase.Lobby }
            ? "Lobby. Fänge werden erfasst, sobald der Gastgeber auf „Starten“ klickt." : "Noch keine Ereignisse.");
    }

    private void RunPlayerTestInstance()
    {
        if (this.closing || Environment.ProcessPath is not { } executable) return;
        try
        {
            var options = new ProcessStartInfo(executable) { UseShellExecute = false };
            options.ArgumentList.Add("--new-test-player");
            using var process = Process.Start(options);
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            MessageBox.Show(this, error.Message, "Testspieler konnte nicht geöffnet werden.");
        }
    }

    private void RenderEncounterCheck(ClientView view)
    {
        var player = view.Run?.Players.FirstOrDefault(player => player.Id == view.LocalPlayerId);
        EncounterCatchVerdict[] checks = view.Snapshot?.Battle is { CatchEncounter: true } live && live.Key == player?.Battle?.Key
            ? player.CatchChecks ?? [] : [];
        string text = string.Join(Environment.NewLine, checks.Select(check => check.Allowed
            ? $"{RubyNames.Pokemon(check.Species)}: Fang erlaubt"
            : $"{RubyNames.Pokemon(check.Species)}: {check.Reason}"));
        this.encounterCheck.SetResult(checks.Length == 0 ? null : checks.All(check => check.Allowed), text);
        float height = checks.Length == 0 ? 0 : 82 + (checks.Length - 1) * 32;
        if (this.catchRow.Height != height) this.catchRow.Height = height;
    }

    private static PendingRunEnd? OwnRunEnd(ClientView view) =>
        view.Run is { Phase: RunPhase.Active, PendingEnd: { } end } && end.PlayerId == view.LocalPlayerId ? end : null;

    private void ReviewRunEnd(bool activate = true)
    {
        if (this.closing) return;
        var view = this.runtime.Status();
        if (OwnRunEnd(view) is not { } end || view.Run is not { } run) return;
        if (this.runEnd is { IsDisposed: false } open)
        {
            if (activate) open.Activate();
            return;
        }

        this.shownRunEnd = end.Id;
        var dialog = new RunEndForm(end.Id, reason => this.runtime.CompleteWipedRunAsync(run.Id, end.Id, reason));
        this.runEnd = dialog;
        dialog.StartPosition = FormStartPosition.Manual;
        dialog.Location = this.PointToScreen(new Point(Math.Max(0, this.ClientSize.Width - dialog.Width - 16), 63));
        dialog.Show(this);
        if (activate) dialog.Activate();
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
        using var identity = new PlayerIdentityForm(view.Player.Length == 0 ? this.profile.Name : view.Player,
            view.Player.Length == 0 ? this.profile.Marker ?? view.Marker : view.Marker);
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
        using var dialog = new SetupForm(this.runtime.Status(), this.rom.Read(), !this.launching && this.game is not { HasExited: false });
        var result = dialog.ShowDialog(this);
        if (result == DialogResult.Retry) await this.StartGameAsync(chooseRom: true);
        this.Render();
    }
}
