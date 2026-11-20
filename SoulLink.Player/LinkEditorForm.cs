using SoulLink;

namespace SoulLink.Player;

internal sealed class LinkEditorForm : Form
{
    private readonly HostLinkEditor editor;
    private readonly Dictionary<int, HostLinkRow> links;
    private readonly DataGridView grid = new();
    private readonly Label feedback = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button save = DashboardTheme.Button("Links speichern");
    private Card? dragged;
    private Card? activeDrag;
    private Point dragOrigin;
    private long lastScroll;
    private bool saving;

    public LinkEditorForm(string runName, HostLinkEditor editor, Func<EditLinksRequest, Task> store)
    {
        this.editor = editor;
        this.links = editor.Links.ToDictionary(row => row.Area, row => row with { Members = new(row.Members) });
        foreach (int area in editor.Players.SelectMany(player => player.Pokemon).Select(mon => mon.MetArea)
            .Where(area => area != 255).Append(Encounter.StarterArea).Distinct())
            this.links.TryAdd(area, new HostLinkRow(area, false, []));

        RunDialogs.Configure(this, $"Links bearbeiten in {runName}", new Size(930, 570));
        this.FormBorderStyle = FormBorderStyle.Sizable;
        this.MinimumSize = new Size(720, 420);
        this.grid.Dock = DockStyle.Fill;
        this.grid.AllowUserToAddRows = this.grid.AllowUserToDeleteRows = this.grid.AllowUserToResizeRows = false;
        this.grid.AllowDrop = true;
        this.grid.ReadOnly = true;
        this.grid.RowHeadersVisible = false;
        this.grid.MultiSelect = false;
        this.grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        this.grid.BackgroundColor = DashboardTheme.Background;
        this.grid.GridColor = DashboardTheme.Border;
        this.grid.EnableHeadersVisualStyles = false;
        this.grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = DashboardTheme.Surface, ForeColor = DashboardTheme.Text };
        this.grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = DashboardTheme.Panel,
            ForeColor = DashboardTheme.Text,
            SelectionBackColor = DashboardTheme.Border,
            SelectionForeColor = DashboardTheme.Text,
        };
        this.grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Link", MinimumWidth = 160, SortMode = DataGridViewColumnSortMode.NotSortable });
        foreach (var player in editor.Players)
            this.grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = player.Name, MinimumWidth = 130, SortMode = DataGridViewColumnSortMode.NotSortable });
        this.grid.MouseDown += (_, e) =>
        {
            var slot = this.grid.HitTest(e.X, e.Y);
            this.dragged = e.Button == MouseButtons.Left && slot.ColumnIndex > 0 && slot.RowIndex >= 0
                ? this.grid[slot.ColumnIndex, slot.RowIndex].Tag as Card : null;
            this.dragOrigin = e.Location;
        };
        this.grid.MouseMove += (_, e) =>
        {
            if (this.dragged is not { } card || e.Button != MouseButtons.Left) return;
            var motion = SystemInformation.DragSize;
            if (Math.Abs(this.dragOrigin.X - e.X) < motion.Width && Math.Abs(this.dragOrigin.Y - e.Y) < motion.Height) return;
            this.dragged = null;
            this.activeDrag = card;
            try { this.grid.DoDragDrop("SoulLink-Pokémon", DragDropEffects.Move); }
            finally { this.activeDrag = null; }
        };
        this.grid.DragOver += (_, e) =>
        {
            if (this.activeDrag is not null) this.ScrollDuringDrag(e.Y);
            e.Effect = this.DropTarget(e) is null ? DragDropEffects.None : DragDropEffects.Move;
        };
        this.grid.DragDrop += (_, e) =>
        {
            if (this.DropTarget(e) is { } target && this.activeDrag is { } card)
                this.MovePokemon(card.Player, card.Pokemon, card.Area, target.Area);
        };

        this.save.Enabled = false;
        this.save.Click += async (_, _) =>
        {
            this.saving = true;
            this.save.Enabled = this.grid.Enabled = false;
            try
            {
                await store(this.Changes());
                this.saving = false;
                this.DialogResult = DialogResult.OK;
            }
            catch (Exception error) when (RunDialogs.IsActionError(error) || error is HttpRequestException or System.Text.Json.JsonException)
            {
                this.feedback.ForeColor = DashboardTheme.Danger;
                this.feedback.Text = error.Message;
            }
            finally
            {
                this.saving = false;
                if (!this.IsDisposed) this.save.Enabled = this.grid.Enabled = true;
            }
        };
        this.FormClosing += (_, e) => e.Cancel = this.saving;
        var cancel = DashboardTheme.Button("Schließen");
        cancel.DialogResult = DialogResult.Cancel;
        this.CancelButton = cancel;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        actions.Controls.AddRange([this.save, cancel]);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.Controls.Add(new Label { Dock = DockStyle.Fill, Text = "Ziehe Pokémon in ihrer Spielerspalte in die gewünschte Zeile. Belegte Plätze tauschen. Zum Lösen nach „Unverlinkt“ ziehen." }, 0, 0);
        layout.Controls.Add(this.grid, 0, 1);
        layout.Controls.Add(this.feedback, 0, 2);
        layout.Controls.Add(actions, 0, 3);
        this.Controls.Add(layout);
        this.RefreshRows();
    }

    internal EditLinksRequest Changes() => new(this.editor.Revision, this.links.Values
        .Where(row => !row.Locked && row.Members.Count > 0).Select(row => new HostLinkEdit(row.Area, new(row.Members))).ToArray());

    internal bool MovePokemon(Guid player, Guid pokemon, int? sourceArea, int? targetArea)
    {
        if (sourceArea == targetArea) return false;
        var source = sourceArea is int area ? this.links[area] : null;
        var target = targetArea is int drop ? this.links[drop] : null;
        if (source is { Locked: true } || target is { Locked: true }) return false;
        if (source is not null) source.Members.Remove(player);
        if (target is not null)
        {
            if (source is not null && target.Members.TryGetValue(player, out Guid swapped)) source.Members[player] = swapped;
            target.Members[player] = pokemon;
        }
        this.save.Enabled = true;
        this.feedback.ForeColor = DashboardTheme.Muted;
        this.feedback.Text = "Änderungen warten auf Speichern.";
        this.RefreshRows();
        return true;
    }

    private RowTarget? DropTarget(DragEventArgs e)
    {
        if (this.activeDrag is not { } card) return null;
        var point = this.grid.PointToClient(new Point(e.X, e.Y));
        var slot = this.grid.HitTest(point.X, point.Y);
        if (slot.RowIndex < 0 || slot.ColumnIndex <= 0 || this.editor.Players[slot.ColumnIndex - 1].Id != card.Player) return null;
        return this.grid.Rows[slot.RowIndex].Tag is RowTarget { Locked: false } target && target.Area != card.Area ? target : null;
    }

    private void ScrollDuringDrag(int screenY)
    {
        long now = Environment.TickCount64;
        int first = this.grid.FirstDisplayedScrollingRowIndex;
        if (first < 0 || now - this.lastScroll < 110) return;
        int y = this.grid.PointToClient(new Point(0, screenY)).Y;
        int visibleRows = this.grid.DisplayedRowCount(includePartialRow: false);
        int target = y < this.grid.ColumnHeadersHeight + 23 ? Math.Max(0, first - 1)
            : y > this.grid.ClientSize.Height - 25 && first + visibleRows < this.grid.Rows.Count ? first + 1 : first;
        if (target == first) return;
        this.grid.FirstDisplayedScrollingRowIndex = target;
        this.lastScroll = now;
    }

    private void RefreshRows()
    {
        int scroll = this.grid.FirstDisplayedScrollingRowIndex;
        this.grid.Rows.Clear();
        var players = this.editor.Players;
        foreach (var link in this.links.Values.OrderBy(row => row.Area == Encounter.StarterArea ? -1 : row.Area))
        {
            string title = RubyNames.Area(link.Area) + (link.Locked ? " (geschützt)" : "");
            int index = this.grid.Rows.Add(title);
            this.grid.Rows[index].Tag = new RowTarget(link.Area, link.Locked);
            for (int i = 0; i < players.Length; i++)
            {
                var player = players[i];
                if (link.Members.TryGetValue(player.Id, out Guid id) && player.Pokemon.FirstOrDefault(mon => mon.Id == id) is { } pokemon)
                    this.SetCell(index, i + 1, player.Id, pokemon, link.Area, link.Locked);
            }
        }

        var assigned = this.links.Values.SelectMany(row => row.Members.Values).ToHashSet();
        var unlinked = players.Select(player => player.Pokemon.Where(mon => !assigned.Contains(mon.Id))
            .OrderBy(mon => RubyNames.Pokemon(mon.Species), StringComparer.CurrentCultureIgnoreCase).ToArray()).ToArray();
        int rows = Math.Max(1, unlinked.Max(list => list.Length));
        for (int position = 0; position < rows; position++)
        {
            int index = this.grid.Rows.Add(position == 0 ? "Unverlinkt" : "");
            this.grid.Rows[index].Tag = new RowTarget(null, false);
            this.grid.Rows[index].DefaultCellStyle.BackColor = DashboardTheme.Surface;
            for (int player = 0; player < players.Length; player++)
                if (position < unlinked[player].Length) this.SetCell(index, player + 1, players[player].Id, unlinked[player][position], null, false);
        }
        if (scroll >= 0 && scroll < this.grid.Rows.Count) this.grid.FirstDisplayedScrollingRowIndex = scroll;
    }

    private void SetCell(int row, int column, Guid player, HostLinkPokemon pokemon, int? area, bool locked)
    {
        var cell = this.grid[column, row];
        string name = RubyNames.Pokemon(pokemon.Species);
        cell.Value = name + (pokemon.Level is int level ? $" (Lv. {level})" : "");
        cell.ToolTipText = $"{pokemon.Nickname}, {RubyNames.Area(pokemon.MetArea)}, {(pokemon.Container == Placement.Party ? "Team" : "Box")}";
        if (!locked) cell.Tag = new Card(player, pokemon.Id, area);
    }

    private sealed record Card(Guid Player, Guid Pokemon, int? Area);
    private sealed record RowTarget(int? Area, bool Locked);
}
