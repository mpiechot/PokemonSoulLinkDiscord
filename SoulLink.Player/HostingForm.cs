using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed record HostingSession(string Server, string Key, Guid? GroupId = null, Guid? RunId = null)
{
    public override string ToString() => nameof(HostingSession);
}

internal sealed class HostingForm : Form
{
    private const string Connecting = "Verbinde mit dem Run-Server …";
    private readonly PlayerProfileLibrary profiles;
    private readonly TextBox server = Input("HTTPS-Serveradresse");
    private readonly TextBox key = Input("Admin-Schlüssel");
    private readonly ComboBox groups = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280, AccessibleName = "Gruppe" };
    private readonly ListBox runs = new() { Dock = DockStyle.Fill, AccessibleName = "Runs" };
    private readonly TextBox groupName = Input("Neue Gruppe");
    private readonly TextBox runName = Input("Name des neuen Runs");
    private readonly Label feedback = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button createRun;
    private readonly Button importDiscordRuns;
    private readonly Button start;
    private readonly Button pause;
    private readonly Button resume;
    private readonly Button end;
    private readonly Button saveProfile;
    private readonly Button invite;
    private readonly Button join;
    private HostingSession? session;
    private readonly Button players;
    private readonly Button editLinks;
    private bool busy;

    public HostingForm(PlayerProfileLibrary profiles, string currentServer, HostingSession? session = null, Func<Guid, Task>? joinRun = null)
    {
        this.profiles = profiles;
        RunDialogs.Configure(this, "Gruppen und Runs verwalten", new Size(742, 525));
        this.session = session;
        this.server.Text = session?.Server ?? currentServer;
        this.key.UseSystemPasswordChar = true;
        this.key.Text = session?.Key ?? "";
        this.groupName.MaxLength = this.runName.MaxLength = 64;
        this.groups.DisplayMember = nameof(RunGroup.Name);
        DashboardTheme.StyleComboBox(this.groups);
        this.runs.DisplayMember = nameof(Row.Label);
        this.runs.BackColor = DashboardTheme.Background;
        this.runs.ForeColor = DashboardTheme.Text;

        var connect = DashboardTheme.Button("Verbinden / aktualisieren");
        connect.Click += async (_, _) => await this.ConnectAsync();
        var addGroup = DashboardTheme.Button("Gruppe anlegen");
        addGroup.Click += async (_, _) => await this.UseServerAsync(async client =>
        {
            var group = await client.CreateGroupAsync(this.groupName.Text);
            this.groupName.Clear();
            await this.LoadGroupsAsync(client, group.Id);
        });
        this.createRun = DashboardTheme.Button("Run anlegen");
        this.createRun.Click += async (_, _) => await this.UseServerAsync(async client =>
        {
            if (this.Group is not { } group) return;
            var run = await client.CreateRunAsync(group.Id, this.runName.Text);
            this.runName.Clear();
            await this.LoadRunsAsync(client, run.Id);
        });
        this.importDiscordRuns = DashboardTheme.Button("Discord-Runs importieren");
        this.importDiscordRuns.Click += async (_, _) => await this.ImportDiscordRunsAsync();
        this.start = this.ActionButton("Starten", "start");
        this.pause = this.ActionButton("Pausieren", "pause");
        this.resume = this.ActionButton("Auswählen / fortsetzen", "load");
        this.end = this.ActionButton("Abschließen", "end");
        this.saveProfile = DashboardTheme.Button("Für mich merken");
        this.saveProfile.Click += (_, _) => this.SaveInvitation(copy: false);
        this.invite = DashboardTheme.Button("Einladung kopieren");
        this.invite.Click += (_, _) => this.SaveInvitation(copy: true);
        this.join = DashboardTheme.Button("Selbst beitreten");
        this.join.Visible = joinRun is not null;
        this.join.Click += async (_, _) =>
        {
            if (joinRun is not null && this.SaveInvitation(copy: false) is { } profile)
                await joinRun(profile.Id);
        };
        this.players = DashboardTheme.Button("Spieler und Spielstände");
        this.players.Click += async (_, _) => await this.UseServerAsync(this.ShowPlayersAsync);
        this.editLinks = DashboardTheme.Button("Links bearbeiten");
        this.editLinks.Click += async (_, _) => await this.UseServerAsync(this.ShowLinkEditorAsync);
        this.groups.SelectedIndexChanged += async (_, _) =>
        {
            if (this.busy) return;
            this.runs.DataSource = null;
            if (this.Group is not null) await this.UseServerAsync(client => this.LoadRunsAsync(client));
        };
        this.runs.SelectedIndexChanged += (_, _) => this.UpdateActions();
        this.server.TextChanged += (_, _) => this.ClearServerView();
        this.key.TextChanged += (_, _) => this.ClearServerView();

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 8 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 8));
        layout.Controls.Add(Line(this.server, this.key, connect), 0, 0);
        layout.Controls.Add(Line(this.groups, this.groupName, addGroup), 0, 1);
        layout.Controls.Add(Line(this.runName, this.createRun, this.importDiscordRuns), 0, 2);
        layout.Controls.Add(this.runs, 0, 3);
        layout.Controls.Add(Line(this.start, this.pause, this.resume, this.end, this.players), 0, 4);
        layout.Controls.Add(Line(this.invite, this.join, this.saveProfile, this.editLinks), 0, 5);
        layout.Controls.Add(this.feedback, 0, 6);
        this.Controls.Add(layout);
        this.ClearServerView();
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

    internal Task ConnectAsync() => this.UseServerAsync(client => this.LoadGroupsAsync(client, this.Group?.Id ?? this.session?.GroupId,
        this.Run?.Id ?? this.session?.RunId));

    private static TextBox Input(string label) => new() { PlaceholderText = label, AccessibleName = label, Width = 200 };

    private static FlowLayoutPanel Line(params Control[] controls)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, 5, 0, 0) };
        row.Controls.AddRange(controls);
        return row;
    }

    private Button ActionButton(string label, string action)
    {
        var button = DashboardTheme.Button(label);
        button.Click += async (_, _) =>
        {
            if (action == "end" && MessageBox.Show(this, "Diesen Run abschließen?", "Run abschließen", MessageBoxButtons.YesNo) != DialogResult.Yes)
                return;
            await this.UseServerAsync(async client =>
            {
                if (this.Group is not { } group || this.Run is not { } run) return;
                await client.ChangeRunAsync(group.Id, run.Id, action);
                await this.LoadRunsAsync(client, run.Id);
            });
        };
        return button;
    }

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
                this.feedback.Text = this.Group is null
                    ? "Verbunden. Gib einen Gruppennamen ein und klicke auf „Gruppe anlegen“."
                    : this.Run is null
                        ? "Verbunden. Gib einen Run-Namen ein und klicke auf „Run anlegen“."
                        : "Mit dem Run-Server verbunden.";
            }
        }
        catch (Exception error) when (RunDialogs.IsActionError(error) || error is HttpRequestException or System.Text.Json.JsonException)
        {
            if (!this.IsDisposed)
            {
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
        this.runs.DataSource = rows;
        int index = Array.FindIndex(rows, row => row.Run.Id == preferred);
        if (index >= 0) this.runs.SelectedIndex = index;
        this.UpdateActions();
    }

    private void ClearServerView()
    {
        this.groups.DataSource = null;
        this.runs.DataSource = null;
        this.feedback.ForeColor = DashboardTheme.Muted;
        this.feedback.Text = "Gib die Serveradresse und den Admin-Schlüssel ein und klicke auf „Verbinden / aktualisieren“.";
        this.UpdateActions();
    }

    private void UpdateActions()
    {
        var run = this.Run;
        this.createRun.Enabled = !this.busy && this.Group is not null;
        this.importDiscordRuns.Enabled = !this.busy && this.Group is not null;
        this.start.Enabled = !this.busy && run is { Phase: RunPhase.Lobby, Selected: true, Players: > 0 } && run.Ready == run.Players;
        this.pause.Enabled = !this.busy && run is { Phase: RunPhase.Active };
        this.resume.Enabled = !this.busy && (run is { Phase: RunPhase.Paused } or { Phase: RunPhase.Lobby, Selected: false });
        this.end.Enabled = !this.busy && run is { Phase: not RunPhase.Finished };
        this.players.Enabled = !this.busy && run is { Players: > 0 };
        this.editLinks.Enabled = !this.busy && run is { Players: > 0, Phase: RunPhase.Active or RunPhase.Paused };
        this.join.Enabled = this.saveProfile.Enabled = this.invite.Enabled = !this.busy && run is { Selected: true, Invitation.Length: > 0 };
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
            if (this.IsDisposed) return;
            this.feedback.ForeColor = DashboardTheme.Accent;
            this.feedback.Text = $"„{group.Name}“ · {result.Imported} importiert, {result.AlreadyImported} bereits vorhanden.";
        });
    }

    private async Task ShowLinkEditorAsync(HostAdminClient client)
    {
        if (this.Group is not { } group || this.Run is not { } run) return;
        var editor = await client.LinkEditorAsync(group.Id, run.Id);
        if (this.IsDisposed) return;
        using var dialog = new LinkEditorForm(run.Name, editor, changes => client.SaveLinksAsync(group.Id, run.Id, changes));
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
        using var dialog = new Form();
        RunDialogs.Configure(dialog, $"Spieler in {run.Name}", new Size(660, 360));
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
                MessageBox.Show(dialog, $"Den aktuellen Spielstand von {row.Player.Name} für einen neuen Abgleich zulassen?",
                    "Spielstand abgleichen", MessageBoxButtons.YesNo) == DialogResult.Yes)
                dialog.DialogResult = DialogResult.OK;
        };
        dialog.Controls.Add(list);
        dialog.Controls.Add(reset);
        if (dialog.ShowDialog(this) != DialogResult.OK || list.SelectedItem is not PlayerRow selected) return;
        await client.RebaselineAsync(group.Id, run.Id, selected.Player.Id);
        MessageBox.Show(this, "Der Spielstand ist freigegeben. Die aktuelle SoulLink-Version gleicht ihn automatisch neu ab.", "Neuer Abgleich");
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
        public string Label => $"{this.Run.Name}   |   {this.Run.Phase switch
        {
            RunPhase.Lobby => "Lobby",
            RunPhase.Active => "Aktiv",
            RunPhase.Paused => "Pausiert",
            _ => "Abgeschlossen",
        }}   |   Spielstände: {this.Run.Ready}/{this.Run.Players}";
    }

    private sealed record PlayerRow(HostPlayer Player)
    {
        public string Label => $"{this.Player.Name}  |  {this.Player.Connection}  |  {this.Player.Diagnostic}";
    }
}

internal enum ProfilePickerMode { Both, Load, Join }

internal sealed class ProfilePicker : Form
{
    private readonly ComboBox choices = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(Row.Name) };

    public ProfilePicker(PlayerProfileLibrary profiles, Guid current, ClientView view, ProfilePickerMode mode)
    {
        bool showInvitation = mode != ProfilePickerMode.Load;
        bool showProfiles = mode != ProfilePickerMode.Join;
        string title = mode switch
        {
            ProfilePickerMode.Join => "Run beitreten",
            ProfilePickerMode.Load => "Run laden",
            _ => "Run beitreten / wechseln",
        };
        RunDialogs.Configure(this, title, new Size(540, mode == ProfilePickerMode.Both ? 310 : 210));
        DashboardTheme.StyleComboBox(this.choices);
        var invite = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Einladung einfügen", AccessibleName = "Einladung", MaxLength = 4096 };
        var server = new TextBox { Dock = DockStyle.Top, Text = view.Server, PlaceholderText = "Serveradresse", AccessibleName = "Serveradresse für einen alten Code", Visible = false };
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
                    profiles.Remember(added with { Name = view.Player, Marker = view.Marker });
                this.SelectedProfile = added.Id;
                this.DialogResult = DialogResult.OK;
            }
            catch (Exception error) when (RunDialogs.IsActionError(error) || error is FormatException) { feedback.Text = error.Message; }
        };
        open.Click += (_, _) =>
        {
            if (this.choices.SelectedItem is not Row row) return;
            this.SelectedProfile = row.Id;
            this.DialogResult = DialogResult.OK;
        };
        this.Controls.Add(feedback);
        if (showProfiles)
        {
            this.Controls.Add(ButtonRow(open));
            this.Controls.Add(this.choices);
            this.Controls.Add(new Label { Text = "Gespeicherte Runs", Dock = DockStyle.Top, Height = 30 });
            this.LoadProfiles(profiles, current, includeLocal: mode == ProfilePickerMode.Both);
            open.Enabled = this.choices.Items.Count > 0;
        }
        if (showInvitation)
        {
            this.Controls.Add(ButtonRow(join));
            this.Controls.Add(server);
            this.Controls.Add(invite);
            this.Controls.Add(new Label { Text = "Neue Einladung", Dock = DockStyle.Top, Height = 30 });
        }
        this.AcceptButton = showProfiles ? open : join;
        this.Shown += (_, _) => { if (showInvitation) invite.Focus(); else this.choices.Focus(); };
    }

    internal Guid SelectedProfile { get; private set; }

    private void LoadProfiles(PlayerProfileLibrary profiles, Guid selected, bool includeLocal)
    {
        var saved = profiles.List().Select(p => new Row(p.Id, p.Label ?? p.Server));
        Row[] rows = includeLocal ? [new(Guid.Empty, "Lokaler Spieltest"), .. saved] : [.. saved];
        this.choices.DataSource = rows;
        if (rows.Length > 0) this.choices.SelectedIndex = Math.Max(0, Array.FindIndex(rows, row => row.Id == selected));
    }

    private static FlowLayoutPanel ButtonRow(Button button)
    {
        var row = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        row.Controls.Add(button);
        return row;
    }

    private sealed record Row(Guid Id, string Name);
}
