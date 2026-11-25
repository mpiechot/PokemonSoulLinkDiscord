using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed record HostingSession(string Server, string Key, Guid? GroupId = null, Guid? RunId = null)
{
    public override string ToString() => nameof(HostingSession);
}

internal sealed class HostingForm : ToolWindow
{
    private const string WindowTitle = "SoulLink – Gastgeber";
    private const string Connecting = "Verbinde mit dem Run-Server …";
    private readonly PlayerProfileLibrary profiles;
    private readonly Func<Guid, Guid?>? playerIdForRun;
    private readonly TextBox server = RunDialogs.TextInput("HTTPS-Serveradresse", 2048);
    private readonly TextBox key = RunDialogs.TextInput("Admin-Schlüssel", 2048);
    private readonly DashboardComboBox groups = new() { Dock = DockStyle.Fill, AccessibleName = "Gruppe" };
    private readonly ListBox runs = new() { Dock = DockStyle.Fill, AccessibleName = "Runs", BorderStyle = BorderStyle.None };
    private readonly Label feedback = new() { Name = "HostFeedback", Dock = DockStyle.Bottom, Height = 42, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label serverName = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = DashboardTheme.Accent, AutoEllipsis = true };
    private readonly Label runName = Heading("");
    private readonly Label runStatus = new() { Dock = DockStyle.Top, Height = 44, ForeColor = DashboardTheme.Muted };
    private readonly Label emptyRuns = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = DashboardTheme.Muted };
    private readonly Button connect = Action("Verbinden", DashboardTheme.Accent);
    private readonly Button addGroup = Action("Neue Gruppe");
    private readonly Button refresh = Action("Aktualisieren");
    private readonly Button groupStatistics = Action("Statistik");
    private readonly ToolCard signIn;
    private readonly TableLayoutPanel workspace;
    private readonly Control invitations;
    private readonly Control management;
    private readonly Control lifecycle;
    private readonly Button createRun;
    private readonly Button importDiscordRuns;
    private readonly Button primary;
    private readonly Button end;
    private readonly Button invite;
    private readonly Button join;
    private HostingSession? session;
    private HostingSession? restoreSession;
    private GroupStatisticsForm? statistics;
    private readonly bool canJoin;
    private readonly Button players;
    private readonly Button editLinks;
    private bool busy;

    public HostingForm(PlayerProfileLibrary profiles, string currentServer, HostingSession? session = null,
        Func<Guid, Task>? joinRun = null, Func<Guid, Guid?>? playerIdForRun = null)
        : base("Gastgeber", new Size(484, 332))
    {
        this.profiles = profiles;
        this.playerIdForRun = playerIdForRun;
        this.restoreSession = session;
        this.canJoin = joinRun is not null;
        this.server.Text = session?.Server ?? currentServer;
        this.server.Name = "HostServer";
        this.key.Name = "HostAdminKey";
        this.key.UseSystemPasswordChar = true;
        this.key.Text = session?.Key ?? "";
        this.groups.DisplayMember = nameof(RunGroup.Name);
        this.runs.DisplayMember = nameof(Row.Label);
        this.runs.BackColor = DashboardTheme.Background;
        this.runs.ForeColor = DashboardTheme.Text;
        this.runs.DrawMode = DrawMode.OwnerDrawFixed;
        this.runs.ItemHeight = 49;
        this.runs.DrawItem += this.DrawRun;

        this.connect.Click += async (_, _) => await this.ConnectAsync();
        this.refresh.Click += async (_, _) => await this.ConnectAsync();
        this.groupStatistics.Click += (_, _) => this.OpenGroupStatistics();
        this.addGroup.Click += async (_, _) =>
        {
            if (Prompt(this, "Neue Gruppe", "Gruppenname", 64) is not { } name) return;
            await this.UseServerAsync(async client =>
            {
                var group = await client.CreateGroupAsync(name);
                await this.LoadGroupsAsync(client, group.Id);
            });
        };
        this.createRun = Action("Neuer Run", DashboardTheme.Accent);
        this.createRun.Click += async (_, _) =>
        {
            if (this.Group is not { } group) return;
            if (Prompt(this, "Neuer Run", "Run-Name", 64) is not { } name) return;
            await this.UseServerAsync(async client =>
            {
                var run = await client.CreateRunAsync(group.Id, name);
                await this.LoadRunsAsync(client, run.Id);
            });
        };
        this.importDiscordRuns = Action("Discord-Runs importieren");
        this.importDiscordRuns.Click += async (_, _) => await this.ImportDiscordRunsAsync();
        this.primary = Action("Starten", DashboardTheme.Accent);
        this.primary.Click += async (_, _) =>
        {
            if (PrimaryAction(this.Run) is { } action) await this.ChangeRunAsync(action.Command);
        };
        this.end = Action("Abschließen", DashboardTheme.Danger);
        this.end.Click += async (_, _) =>
        {
            if (ToolWindow.Confirm(this, "Run abschließen", "Diesen Run abschließen?", "Run abschließen"))
                await this.ChangeRunAsync("end");
        };
        this.invite = Action("Einladung kopieren");
        this.invite.Click += (_, _) => this.SaveInvitation(copy: true);
        this.join = Action("Selbst beitreten", DashboardTheme.Accent);
        this.join.Click += async (_, _) =>
        {
            if (joinRun is not null && this.SaveInvitation(copy: false) is { } profile)
                await joinRun(profile.Id);
        };
        this.players = Action("Spieler und Spielstände");
        this.players.Click += async (_, _) => await this.UseServerAsync(this.ShowPlayersAsync);
        this.editLinks = Action("Links bearbeiten");
        this.editLinks.Click += async (_, _) => await this.UseServerAsync(this.ShowLinkEditorAsync);
        this.groups.SelectedIndexChanged += async (_, _) =>
        {
            if (this.busy || this.IsDisposed || this.Disposing) return;
            this.runs.Items.Clear();
            if (this.Group is not null) await this.UseServerAsync(client => this.LoadRunsAsync(client));
        };
        this.runs.SelectedIndexChanged += (_, _) => this.UpdateActions();
        this.lifecycle = ActionGroup("Run-Fortschritt", this.primary, this.end);
        this.invitations = ActionGroup("Einladung", this.invite, this.join);
        this.management = ActionGroup("Run verwalten", this.players, this.editLinks);
        this.signIn = this.SignInCard();
        this.workspace = this.BuildWorkspace();
        this.Body.Controls.Add(this.workspace);
        this.Body.Controls.Add(this.signIn);
        this.Body.Controls.Add(this.feedback);
        this.server.TextChanged += (_, _) =>
        {
            string previousTitle = this.Text;
            this.UpdateWindowTitle();
            if (this.Text != previousTitle) this.key.Clear();
            this.UpdateActions();
        };
        this.key.TextChanged += (_, _) => this.UpdateActions();
        this.ClearServerView();
        this.Shown += (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(this.server.Text)) this.server.Focus();
            else
            {
                this.key.Focus();
                this.key.SelectAll();
            }
        };
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        base.OnFormClosing(e);
        if (e.CloseReason == CloseReason.UserClosing && this.Owner is not null)
        {
            e.Cancel = true;
            this.Hide();
        }
    }

    private RunGroup? Group => this.groups.SelectedItem as RunGroup;

    private HostRun? Run => (this.runs.SelectedItem as Row)?.Run;

    internal HostingSession? Session => this.session is { } verified
        ? verified with { GroupId = this.Group?.Id ?? verified.GroupId, RunId = this.Run?.Id ?? verified.RunId }
        : null;

    internal Task ConnectAsync() => this.UseServerAsync(client => this.LoadGroupsAsync(client, this.Group?.Id ?? this.restoreSession?.GroupId,
        this.Run?.Id ?? this.restoreSession?.RunId));

    private ToolCard SignInCard()
    {
        var card = new ToolCard { Dock = DockStyle.Fill, Name = "HostSignIn" };
        var fields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        foreach (int height in new[] { 24, 41, 24, 44, 42 }) fields.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        this.server.Dock = this.key.Dock = DockStyle.Fill;
        this.connect.Anchor = AnchorStyles.Left;
        var serverLabel = Heading("Serveradresse");
        serverLabel.Dock = DockStyle.Fill;
        var keyLabel = Heading("Admin-Schlüssel");
        keyLabel.Dock = DockStyle.Fill;
        serverLabel.TabIndex = 0;
        this.server.TabIndex = 1;
        keyLabel.TabIndex = 2;
        this.key.TabIndex = 3;
        this.connect.TabIndex = 4;
        fields.Controls.Add(serverLabel, 0, 0);
        fields.Controls.Add(this.server, 0, 1);
        fields.Controls.Add(keyLabel, 0, 2);
        fields.Controls.Add(this.key, 0, 3);
        fields.Controls.Add(this.connect, 0, 4);
        card.Controls.Add(fields);
        return card;
    }

    private TableLayoutPanel BuildWorkspace()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Name = "HostWorkspace" };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 103));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var connection = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8) };
        var switchServer = Action("Server wechseln");
        switchServer.Dock = DockStyle.Right;
        switchServer.Click += (_, _) =>
        {
            if (this.busy) return;
            this.key.Clear();
            this.ClearServerView();
            this.server.Focus();
        };
        connection.Controls.Add(this.serverName);
        connection.Controls.Add(switchServer);
        layout.Controls.Add(connection, 0, 0);
        layout.SetColumnSpan(connection, 2);

        var groupCard = new ToolCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
        var groupTitle = Heading("Gruppe");
        groupTitle.Dock = DockStyle.Top;
        groupTitle.Height = 23;
        var groupActions = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1 };
        groupActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        groupActions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        groupActions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        groupActions.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        this.groups.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        this.groups.Dock = DockStyle.None;
        groupActions.Controls.Add(this.groups, 0, 0);
        groupActions.Controls.Add(this.addGroup, 1, 0);
        groupActions.Controls.Add(this.importDiscordRuns, 2, 0);
        groupActions.Controls.Add(this.groupStatistics, 3, 0);
        groupCard.Controls.Add(groupActions);
        groupCard.Controls.Add(groupTitle);
        layout.Controls.Add(groupCard, 0, 1);
        layout.SetColumnSpan(groupCard, 2);

        var listCard = new ToolCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 5, 0) };
        var listTitle = Heading("Runs");
        listTitle.Dock = DockStyle.Top;
        listTitle.Height = 26;
        var listActions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, WrapContents = false };
        listActions.Controls.AddRange([this.createRun, this.refresh]);
        listCard.Controls.Add(this.emptyRuns);
        listCard.Controls.Add(this.runs);
        listCard.Controls.Add(listActions);
        listCard.Controls.Add(listTitle);
        layout.Controls.Add(listCard, 0, 2);

        var details = new ToolCard { Dock = DockStyle.Fill, Margin = new Padding(5, 0, 0, 0) };
        this.runName.Dock = DockStyle.Top;
        this.runName.Height = 31;
        this.runName.AutoEllipsis = true;
        var detailsActions = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        detailsActions.Controls.AddRange([this.lifecycle, this.invitations, this.management]);
        detailsActions.SizeChanged += (_, _) =>
        {
            foreach (Control section in detailsActions.Controls) section.Width = detailsActions.ClientSize.Width;
        };
        details.Controls.Add(detailsActions);
        details.Controls.Add(this.runStatus);
        details.Controls.Add(this.runName);
        layout.Controls.Add(details, 1, 2);
        return layout;
    }

    private static Control ActionGroup(string title, params Button[] actions)
    {
        var section = new Panel { Height = 75, Margin = new Padding(0) };
        var label = Heading(title);
        label.Dock = DockStyle.Top;
        label.Height = 26;
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        row.Controls.AddRange(actions);
        section.Controls.Add(row);
        section.Controls.Add(label);
        return section;
    }

    private void DrawRun(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || this.runs.Items[e.Index] is not Row row) return;
        bool selected = e.State.HasFlag(DrawItemState.Selected);
        using var fill = new SolidBrush(selected ? DashboardTheme.Panel : DashboardTheme.Background);
        e.Graphics.FillRectangle(fill, e.Bounds);
        var name = new Rectangle(e.Bounds.X + 9, e.Bounds.Y + 5, e.Bounds.Width - 18, 21);
        TextRenderer.DrawText(e.Graphics, row.Run.Name, this.Font, name, selected ? DashboardTheme.Accent : DashboardTheme.Text,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        var status = new Rectangle(name.X, name.Bottom, name.Width, 19);
        TextRenderer.DrawText(e.Graphics, Phase(row.Run), this.Font, status, DashboardTheme.Muted,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        e.DrawFocusRectangle();
    }

    private static (string Label, string Command, bool Enabled)? PrimaryAction(HostRun? run) => run switch
    {
        { Phase: RunPhase.Lobby, Selected: true } => ("Starten", "start", run.Players > 0 && run.Ready == run.Players),
        { Phase: RunPhase.Lobby } => ("Lobby aktivieren", "load", true),
        { Phase: RunPhase.Active } => ("Pausieren", "pause", true),
        { Phase: RunPhase.Paused } => ("Fortsetzen", "load", true),
        _ => null,
    };

    private Task ChangeRunAsync(string action) => this.UseServerAsync(async client =>
    {
        if (this.Group is not { } group || this.Run is not { } run) return;
        await client.ChangeRunAsync(group.Id, run.Id, action);
        await this.LoadRunsAsync(client, run.Id);
    });

    private async Task UseServerAsync(Func<HostAdminClient, Task> action)
    {
        if (this.busy) return;
        this.busy = true;
        this.feedback.ForeColor = DashboardTheme.Muted;
        this.feedback.Text = Connecting;
        this.UpdateActions();
        this.groups.Enabled = this.server.Enabled = this.key.Enabled = false;
        try
        {
            using var client = new HostAdminClient(this.server.Text, this.key.Text);
            await action(client);
            if (!this.IsDisposed && this.feedback.Text == Connecting)
            {
                this.feedback.ForeColor = DashboardTheme.Accent;
                this.feedback.Text = this.Group is null ? "Verbunden. Lege mit „Neue Gruppe“ deine erste Gruppe an." : "";
            }
        }
        catch (Exception error) when (RunDialogs.IsActionError(error) || error is HttpRequestException or System.Text.Json.JsonException)
        {
            if (!this.IsDisposed)
            {
                if (error is UnauthorizedAccessException) this.ClearServerView();
                this.feedback.ForeColor = DashboardTheme.Danger;
                this.feedback.Text = error.Message;
            }
        }
        finally
        {
            this.busy = false;
            if (!this.IsDisposed)
            {
                this.groups.Enabled = this.server.Enabled = this.key.Enabled = true;
                this.UpdateActions();
            }
        }
    }

    private async Task LoadGroupsAsync(HostAdminClient client, Guid? preferred = null, Guid? preferredRun = null)
    {
        preferred ??= this.Group?.Id;
        var available = await client.GroupsAsync();
        if (this.IsDisposed) return;
        this.session = new HostingSession(this.server.Text.Trim(), this.key.Text);
        this.restoreSession = null;
        this.ShowWorkspace(true);
        this.groups.DataSource = available;
        int index = Array.FindIndex(available, group => group.Id == preferred);
        if (index >= 0) this.groups.SelectedIndex = index;
        await this.LoadRunsAsync(client, preferredRun);
    }

    private async Task LoadRunsAsync(HostAdminClient client, Guid? preferred = null)
    {
        preferred ??= this.Run?.Id;
        var rows = this.Group is { } group ? (await client.RunsAsync(group.Id)).Select(run => new Row(run)).ToArray() : [];
        if (this.IsDisposed) return;
        this.runs.BeginUpdate();
        this.runs.Items.Clear();
        this.runs.Items.AddRange(rows);
        int index = Array.FindIndex(rows, row => row.Run.Id == preferred);
        if (rows.Length > 0) this.runs.SelectedIndex = Math.Max(0, index);
        this.runs.EndUpdate();
        this.UpdateActions();
    }

    private void ClearServerView()
    {
        this.statistics?.Close();
        this.session = null;
        this.groups.DataSource = null;
        this.runs.Items.Clear();
        this.ShowWorkspace(false);
        this.feedback.ForeColor = DashboardTheme.Muted;
        this.feedback.Text = "";
        this.UpdateActions();
    }

    private void ShowWorkspace(bool connected)
    {
        this.serverName.Text = connected ? $"Verbunden  ·  {this.session!.Server}" : "";
        this.signIn.Visible = !connected;
        this.workspace.Visible = connected;
        this.AcceptButton = connected ? null : this.connect;
        this.UpdateWindowTitle();
        var size = this.LogicalToDeviceUnits(connected ? new Size(824, 626) : new Size(484, 332));
        if (size == this.ClientSize) return;
        var center = new Point(this.Left + this.Width / 2, this.Top + this.Height / 2);
        this.ClientSize = size;
        if (this.IsHandleCreated)
        {
            var area = Screen.FromPoint(center).WorkingArea;
            this.Location = new Point(Math.Clamp(center.X - size.Width / 2, area.Left, Math.Max(area.Left, area.Right - size.Width)),
                Math.Clamp(center.Y - size.Height / 2, area.Top, Math.Max(area.Top, area.Bottom - size.Height)));
        }
    }

    private void UpdateWindowTitle()
    {
        // Native password managers can match the active sign-in window to the configured origin.
        if (this.session is null && Uri.TryCreate(this.server.Text.Trim(), UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback) && uri.UserInfo.Length == 0)
        {
            var origin = new UriBuilder(uri.Scheme, uri.IdnHost, uri.IsDefaultPort ? -1 : uri.Port).Uri;
            this.Text = $"{WindowTitle} | {origin.GetLeftPart(UriPartial.Authority)}";
        }
        else
        {
            this.Text = WindowTitle;
        }
    }

    private void UpdateActions()
    {
        if (this.IsDisposed || this.Disposing) return;
        var run = this.Run;
        this.connect.Enabled = !this.busy && !string.IsNullOrWhiteSpace(this.server.Text) && this.key.Text.Trim().Length >= 32;
        this.refresh.Enabled = this.addGroup.Enabled = !this.busy && this.session is not null;
        this.createRun.Enabled = this.importDiscordRuns.Enabled = !this.busy && this.Group is not null;
        this.groupStatistics.Enabled = !this.busy && this.Group is not null && this.session is not null;
        this.emptyRuns.Visible = run is null;
        this.runs.TabStop = run is not null;
        this.emptyRuns.Text = this.Group is null ? "Wähle eine Gruppe." : "In dieser Gruppe gibt es noch keine Runs.";
        this.runName.Text = run?.Name ?? "Run-Details";
        this.runStatus.Height = this.LogicalToDeviceUnits(run?.DiscordArchive is null ? 44 : 100);
        this.runStatus.Text = RunDetails(run);
        var action = PrimaryAction(run);
        this.primary.Visible = action.HasValue;
        this.primary.Enabled = !this.busy && action is { Enabled: true };
        this.primary.Text = action?.Label ?? "";
        this.end.Visible = this.lifecycle.Visible = run is { Phase: not RunPhase.Finished };
        this.end.Enabled = !this.busy;
        this.players.Visible = this.management.Visible = run is { Players: > 0, DiscordArchive: null };
        this.editLinks.Visible = run is { Players: > 0, Phase: RunPhase.Active or RunPhase.Paused };
        this.players.Enabled = this.editLinks.Enabled = !this.busy;
        this.invitations.Visible = run is { Selected: true, Invitation.Length: > 0 };
        this.join.Visible = this.canJoin;
        this.join.Enabled = this.invite.Enabled = !this.busy;
    }

    private async Task ImportDiscordRunsAsync()
    {
        if (this.Group is not { } group) return;
        using var picker = new OpenFileDialog
        {
            Title = $"Discord-Runs für „{group.Name}“ auswählen",
            Filter = "Discord-Run-Archiv (*.json;*.json.bak)|*.json;*.json.bak",
            CheckFileExists = true,
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        await this.UseServerAsync(async client =>
        {
            var result = await client.ImportDiscordRunsAsync(group.Id, picker.FileName);
            await this.LoadRunsAsync(client);
            if (this.IsDisposed) return;
            this.feedback.ForeColor = DashboardTheme.Accent;
            this.feedback.Text = $"„{group.Name}“ · {result.Imported} importiert, {result.AlreadyImported} bereits vorhanden.";
        });
    }

    private void OpenGroupStatistics()
    {
        if (this.Group is not { } group || this.session is not { } session) return;
        this.statistics?.Close();
        this.statistics = new GroupStatisticsForm(
            async cancellationToken =>
            {
                using var client = new HostAdminClient(session.Server, session.Key);
                return await client.GroupStatisticsAsync(group.Id, cancellationToken);
            },
            async cancellationToken =>
            {
                using var client = new HostAdminClient(session.Server, session.Key);
                return await client.GroupTimelineAsync(group.Id, cancellationToken);
            });
        this.statistics.Show(this);
    }

    private async Task ShowLinkEditorAsync(HostAdminClient client)
    {
        if (this.Group is not { } group || this.Run is not { } run) return;
        var editor = await client.LinkEditorAsync(group.Id, run.Id);
        if (this.IsDisposed) return;
        using var dialog = new LinkEditorForm(run.Name, editor, changes => client.SaveLinksAsync(group.Id, run.Id, changes),
            this.playerIdForRun?.Invoke(run.Id));
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            this.feedback.ForeColor = DashboardTheme.Accent;
            this.feedback.Text = "Links gespeichert.";
        }
    }

    private async Task ShowPlayersAsync(HostAdminClient client)
    {
        if (this.Group is not { } group || this.Run is not { } run) return;
        var players = await client.PlayersAsync(group.Id, run.Id);
        if (this.IsDisposed) return;
        using var dialog = new ToolWindow($"Spieler in {run.Name}", new Size(660, 410));
        var list = new ListBox
        {
            Dock = DockStyle.Fill,
            BackColor = DashboardTheme.Background,
            ForeColor = DashboardTheme.Text,
            DataSource = players.Select(player => new PlayerRow(player)).ToArray(),
            DisplayMember = nameof(PlayerRow.Label),
        };
        var reset = DashboardTheme.Button("Spielstand neu abgleichen");
        reset.Dock = DockStyle.Bottom;
        reset.Height = 36;
        reset.Enabled = run.Phase != RunPhase.Finished;
        reset.Click += (_, _) =>
        {
            if (list.SelectedItem is PlayerRow row &&
                ToolWindow.Confirm(dialog, "Spielstand abgleichen", $"Den aktuellen Spielstand von {row.Player.Name} für einen neuen Abgleich zulassen?", "Abgleichen"))
                dialog.DialogResult = DialogResult.OK;
        };
        dialog.Body.Controls.Add(list);
        dialog.Body.Controls.Add(reset);
        if (dialog.ShowDialog(this) != DialogResult.OK || list.SelectedItem is not PlayerRow selected) return;
        await client.RebaselineAsync(group.Id, run.Id, selected.Player.Id);
        ToolWindow.Notice(this, "Neuer Abgleich", "Der Spielstand ist freigegeben. Die aktuelle SoulLink-Version gleicht ihn automatisch neu ab.");
    }

    private PlayerProfile? SaveInvitation(bool copy)
    {
        if (this.Group is not { } group || this.Run is not { Invitation.Length: > 0 } run) return null;
        try
        {
            var profile = PlayerProfileLibrary.ForHostedRun(this.server.Text, group, run);
            var saved = this.profiles.AddInvitation(profile.Server, profile.Invitation, profile.Label!);
            if (copy) Clipboard.SetText(PlayerInvitation.Encode(profile));
            this.feedback.ForeColor = DashboardTheme.Accent;
            this.feedback.Text = copy ? "Einladung kopiert. Deine Mitspieler können sie in ihrer EXE einfügen."
                : "Die Einladung wurde gespeichert.";
            return saved;
        }
        catch (Exception error) when (RunDialogs.IsActionError(error) || error is FormatException or System.Runtime.InteropServices.ExternalException)
        {
            this.feedback.ForeColor = DashboardTheme.Danger;
            this.feedback.Text = error.Message;
            return null;
        }
    }

    private sealed record Row(HostRun Run)
    {
        public string Label => $"{this.Run.Name}  ·  {Phase(this.Run)}";
    }

    private static string RunDetails(HostRun? run)
    {
        if (run is null) return "Wähle links einen Run aus.";
        if (run.DiscordArchive is not { } archive) return $"{Phase(run)}  ·  {run.Ready}/{run.Players} Spielstände bereit";

        string game = string.IsNullOrWhiteSpace(archive.Game) ? "" : $"{archive.Game}  ·  ";
        string dates = $"Start: {archive.StartedAtUtc.ToLocalTime():d}";
        if (archive.EndedAtUtc is DateTime end) dates += $"  ·  Ende: {end.ToLocalTime():d}";
        string reason = string.IsNullOrWhiteSpace(archive.EndReason) ? "" : $"\nEndgrund: {archive.EndReason}";
        string players = run.Players == 1 ? "1 Spieler" : $"{run.Players} Spieler";
        return $"{Phase(run)}\n{game}{players}\n{dates}{reason}";
    }

    private static string Phase(HostRun run) => run switch
    {
        { DiscordArchive.EndedAtUtc: not null } => "Discord-Archiv  ·  Abgeschlossen",
        { DiscordArchive: not null } => "Discord-Archiv  ·  Offen beim Export",
        { Phase: RunPhase.Lobby, Selected: false } => "Inaktive Lobby",
        { Phase: RunPhase.Lobby } => "Lobby",
        { Phase: RunPhase.Active } => "Aktiv",
        { Phase: RunPhase.Paused } => "Pausiert",
        _ => "Abgeschlossen",
    };

    private sealed record PlayerRow(HostPlayer Player)
    {
        public string Label => $"{this.Player.Name}  |  {this.Player.Connection}  |  {this.Player.Diagnostic}";
    }
}

internal enum ProfilePickerMode { Both, Load, Join }

internal sealed class ProfilePicker : ToolWindow
{
    private readonly DashboardComboBox choices = new() { Dock = DockStyle.Top, DisplayMember = nameof(Row.Name) };

    public ProfilePicker(PlayerProfileLibrary profiles, Guid current, ClientView view, ProfilePickerMode mode)
        : base(mode switch
        {
            ProfilePickerMode.Join => "Run beitreten",
            ProfilePickerMode.Load => "Run laden",
            _ => "Run beitreten / wechseln",
        }, new Size(540, mode == ProfilePickerMode.Both ? 360 : 260))
    {
        bool showInvitation = mode != ProfilePickerMode.Load;
        bool showProfiles = mode != ProfilePickerMode.Join;
        var invite = RunDialogs.TextInput("Einladung einfügen", 4096);
        invite.Dock = DockStyle.Top;
        var server = RunDialogs.TextInput("Serveradresse für einen alten Code", 2048);
        server.Dock = DockStyle.Top;
        server.Text = view.Server;
        server.Visible = false;
        var join = DashboardTheme.Button("Beitreten");
        var open = DashboardTheme.Button("Run laden");
        var feedback = new Label { Dock = DockStyle.Bottom, Height = 35, ForeColor = DashboardTheme.Danger };
        join.Enabled = false;
        invite.TextChanged += (_, _) =>
        {
            join.Enabled = !string.IsNullOrWhiteSpace(invite.Text);
            server.Visible = join.Enabled && !PlayerInvitation.ContainsServer(invite.Text);
            this.AcceptButton = join.Enabled || !showProfiles ? join : open;
            feedback.Text = "";
        };
        join.Click += (_, _) =>
        {
            try
            {
                var details = PlayerInvitation.Read(invite.Text, server.Text);
                var added = profiles.AddInvitation(details.Server, details.Invitation, details.Label ?? "");
                if (added.Name is null && !string.IsNullOrWhiteSpace(view.Player))
                    profiles.Remember(added with { Name = view.Player, Marker = view.Marker, Color = view.Color });
                this.SelectedProfile = added.Id;
                this.DialogResult = DialogResult.OK;
            }
            catch (Exception error) when (RunDialogs.IsActionError(error) || error is FormatException) { feedback.Text = error.Message; }
        };
        open.Click += (_, _) =>
        {
            if (this.choices.SelectedItem is not Row row) return;
            this.SelectedProfile = row.Id;
            this.LoadSavedProfile = true;
            this.DialogResult = DialogResult.OK;
        };
        var card = new ToolCard { Dock = DockStyle.Fill };
        card.Controls.Add(feedback);
        if (showProfiles)
        {
            var browseGroups = DashboardTheme.Button("Alle Runs der Gruppe");
            browseGroups.Click += (_, _) =>
            {
                this.OpenGroupRuns = true;
                this.DialogResult = DialogResult.OK;
            };
            card.Controls.Add(ButtonRow(open, browseGroups));
            card.Controls.Add(this.choices);
            card.Controls.Add(new Label { Text = "Gespeicherte Runs", Dock = DockStyle.Top, Height = 30 });
            this.LoadProfiles(profiles, current, includeLocal: mode == ProfilePickerMode.Both);
            open.Enabled = this.choices.Items.Count > 0;
        }
        if (showInvitation)
        {
            card.Controls.Add(ButtonRow(join));
            card.Controls.Add(server);
            card.Controls.Add(invite);
            card.Controls.Add(new Label { Text = "Neue Einladung", Dock = DockStyle.Top, Height = 30 });
        }
        this.Body.Controls.Add(card);
        this.AcceptButton = showProfiles ? open : join;
        this.Shown += (_, _) => { if (showInvitation) invite.Focus(); else this.choices.Focus(); };
    }

    internal Guid SelectedProfile { get; private set; }

    internal bool LoadSavedProfile { get; private set; }

    internal bool OpenGroupRuns { get; private set; }

    private void LoadProfiles(PlayerProfileLibrary profiles, Guid selected, bool includeLocal)
    {
        var saved = profiles.List().Select(p => new Row(p.Id, p.Label ?? p.Server));
        Row[] rows = includeLocal ? [new(Guid.Empty, "Lokaler Spieltest"), .. saved] : [.. saved];
        this.choices.Items.AddRange(rows);
        if (rows.Length > 0) this.choices.SelectedIndex = Math.Max(0, Array.FindIndex(rows, row => row.Id == selected));
    }

    private static FlowLayoutPanel ButtonRow(params Button[] buttons)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        row.Controls.AddRange(buttons);
        return row;
    }

    private sealed record Row(Guid Id, string Name);
}
