using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class HostingForm : Form
{
    private readonly PlayerProfileLibrary profiles;
    private readonly TextBox server = Input("HTTPS-Serveradresse");
    private readonly TextBox key = Input("Admin-Schlüssel");
    private readonly ComboBox groups = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280, AccessibleName = "Gruppe" };
    private readonly ListBox runs = new() { Dock = DockStyle.Fill, AccessibleName = "Runs" };
    private readonly TextBox groupName = Input("Neue Gruppe");
    private readonly TextBox runName = Input("Name des neuen Runs");
    private readonly Label feedback = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button createRun;
    private readonly Button start;
    private readonly Button pause;
    private readonly Button resume;
    private readonly Button end;
    private readonly Button saveProfile;
    private readonly Button package;
    private readonly Button invite;
    private readonly Button players;
    private bool busy;

    public HostingForm(PlayerProfileLibrary profiles, string currentServer)
    {
        this.profiles = profiles;
        RunDialogs.Configure(this, "Gruppen und Runs verwalten", new Size(742, 525));
        this.server.Text = currentServer;
        this.key.UseSystemPasswordChar = true;
        this.groupName.MaxLength = this.runName.MaxLength = 64;
        this.groups.DisplayMember = nameof(RunGroup.Name);
        this.runs.DisplayMember = nameof(Row.Label);
        this.runs.BackColor = DashboardTheme.Background;
        this.runs.ForeColor = DashboardTheme.Text;

        var connect = DashboardTheme.Button("Verbinden / aktualisieren");
        connect.Click += async (_, _) => await this.UseServerAsync(client => this.LoadGroupsAsync(client));
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
        this.start = this.ActionButton("Starten", "start");
        this.pause = this.ActionButton("Pausieren", "pause");
        this.resume = this.ActionButton("Auswählen / fortsetzen", "load");
        this.end = this.ActionButton("Abschließen", "end");
        this.saveProfile = DashboardTheme.Button("Für mich merken");
        this.saveProfile.Click += (_, _) => this.SaveInvitation(package: false);
        this.package = DashboardTheme.Button("Spielerdatei erstellen");
        this.package.Click += (_, _) => this.SaveInvitation(package: true);
        this.invite = DashboardTheme.Button("Einladung kopieren");
        this.invite.Click += (_, _) =>
        {
            if (this.Run is { Invitation.Length: > 0 } lobby) Clipboard.SetText(lobby.Invitation);
        };
        this.players = DashboardTheme.Button("Spieler und Spielstände");
        this.players.Click += async (_, _) => await this.UseServerAsync(this.ShowPlayersAsync);
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
        layout.Controls.Add(Line(this.runName, this.createRun), 0, 2);
        layout.Controls.Add(this.runs, 0, 3);
        layout.Controls.Add(Line(this.start, this.pause, this.resume, this.end, this.players), 0, 4);
        layout.Controls.Add(Line(this.saveProfile, this.package, this.invite), 0, 5);
        layout.Controls.Add(this.feedback, 0, 6);
        this.Controls.Add(layout);
        this.UpdateActions();
    }

    private RunGroup? Group => this.groups.SelectedItem as RunGroup;

    private HostRun? Run => (this.runs.SelectedItem as Row)?.Run;

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
        this.feedback.Text = "Verbinde mit dem Run-Server …";
        this.UpdateActions();
        this.groups.Enabled = this.server.Enabled = this.key.Enabled = false;
        try
        {
            using var client = new HostAdminClient(this.server.Text, this.key.Text);
            await action(client);
            if (!this.IsDisposed) this.feedback.Text = "";
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

    private async Task LoadGroupsAsync(HostAdminClient client, Guid? preferred = null)
    {
        preferred ??= this.Group?.Id;
        var available = await client.GroupsAsync();
        if (this.IsDisposed) return;
        this.groups.DataSource = available;
        int index = Array.FindIndex(available, group => group.Id == preferred);
        if (index >= 0) this.groups.SelectedIndex = index;
        await this.LoadRunsAsync(client);
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
        this.UpdateActions();
    }

    private void UpdateActions()
    {
        var run = this.Run;
        this.createRun.Enabled = !this.busy && this.Group is not null;
        this.start.Enabled = !this.busy && run is { Phase: RunPhase.Lobby, Selected: true, Players: > 0 } && run.Ready == run.Players;
        this.pause.Enabled = !this.busy && run is { Phase: RunPhase.Active };
        this.resume.Enabled = !this.busy && (run is { Phase: RunPhase.Paused } or { Phase: RunPhase.Lobby, Selected: false });
        this.end.Enabled = !this.busy && run is { Phase: not RunPhase.Finished };
        this.players.Enabled = !this.busy && run is { Players: > 0 };
        this.saveProfile.Enabled = this.package.Enabled = this.invite.Enabled = !this.busy && run is { Selected: true, Invitation.Length: > 0 };
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
        MessageBox.Show(this, "Der Spieler kann im SoulLink-Menü „Run-Server neu verbinden“ wählen.", "Neuer Abgleich");
    }

    private void SaveInvitation(bool package)
    {
        if (this.Group is not { } group || this.Run is not { Invitation.Length: > 0 } run) return;
        try
        {
            var profile = PlayerProfileLibrary.ForHostedRun(this.server.Text, group, run);
            this.profiles.Remember(profile);
            if (package)
            {
                using var save = new SaveFileDialog { FileName = "SoulLink-Spieler.exe", Filter = "SoulLink-Anwendung (*.exe)|*.exe" };
                if (save.ShowDialog(this) != DialogResult.OK) return;
                PlayerBundle.Create(Environment.ProcessPath!, save.FileName, profile);
            }
            this.feedback.ForeColor = DashboardTheme.Accent;
            this.feedback.Text = package ? "Die Spielerdatei ist bereit." : "Der Run steht jetzt unter „Run wechseln“.";
        }
        catch (Exception error) when (RunDialogs.IsActionError(error) || error is FormatException)
        {
            this.feedback.ForeColor = DashboardTheme.Danger;
            this.feedback.Text = error.Message;
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

internal sealed class ProfilePicker : Form
{
    private readonly ComboBox choices = new() { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList, DisplayMember = nameof(Row.Name) };

    public ProfilePicker(PlayerProfileLibrary profiles, Guid current, string serverAddress)
    {
        RunDialogs.Configure(this, "Run wechseln", new Size(520, 260));
        var server = new TextBox { Dock = DockStyle.Top, Text = serverAddress, AccessibleName = "Serveradresse" };
        var invite = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Einladungscode", AccessibleName = "Einladungscode" };
        var name = new TextBox { Dock = DockStyle.Top, PlaceholderText = "Name für die Run-Liste", AccessibleName = "Name für die Run-Liste", MaxLength = 140 };
        var add = DashboardTheme.Button("Einladung merken");
        var open = DashboardTheme.Button("Run öffnen");
        var feedback = new Label { Dock = DockStyle.Bottom, Height = 35, ForeColor = DashboardTheme.Danger };
        add.Click += (_, _) =>
        {
            try
            {
                var added = profiles.AddInvitation(server.Text, invite.Text, name.Text);
                this.LoadProfiles(profiles, added.Id);
                invite.Clear();
                feedback.Text = "";
            }
            catch (Exception error) when (RunDialogs.IsActionError(error) || error is FormatException) { feedback.Text = error.Message; }
        };
        open.Click += (_, _) =>
        {
            if (this.choices.SelectedItem is not Row row) return;
            this.SelectedProfile = row.Id;
            this.DialogResult = DialogResult.OK;
        };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 40 };
        actions.Controls.AddRange([open, add]);
        this.Controls.Add(feedback);
        this.Controls.Add(name);
        this.Controls.Add(invite);
        this.Controls.Add(server);
        this.Controls.Add(new Label { Text = "Eine weitere Einladung hinzufügen", Dock = DockStyle.Top, Height = 30 });
        this.Controls.Add(actions);
        this.Controls.Add(this.choices);
        this.LoadProfiles(profiles, current);
    }

    internal Guid SelectedProfile { get; private set; }

    private void LoadProfiles(PlayerProfileLibrary profiles, Guid selected)
    {
        Row[] rows = [new(Guid.Empty, "Lokaler Spieltest"), .. profiles.List().Select(p => new Row(p.Id, p.Label ?? p.Server))];
        this.choices.DataSource = rows;
        this.choices.SelectedIndex = Math.Max(0, Array.FindIndex(rows, row => row.Id == selected));
    }

    private sealed record Row(Guid Id, string Name);
}
