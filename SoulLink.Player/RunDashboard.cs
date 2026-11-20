using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class RunDashboard : TableLayoutPanel
{
    private readonly HoennMap map = new() { Dock = DockStyle.Fill };
    private readonly RichTextBox locations = new();
    private readonly ArenaPanel arena;
    private ClientView? view;

    public RunDashboard(DiscordIcons icons)
    {
        this.arena = new ArenaPanel(icons) { Dock = DockStyle.Fill };
        this.Dock = DockStyle.Fill;
        this.BackColor = DashboardTheme.Surface;
        this.Padding = new Padding(7);
        this.ColumnCount = 1;
        this.RowCount = 3;
        this.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        this.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        this.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));

        this.locations.Dock = DockStyle.Fill;
        this.locations.Multiline = true;
        this.locations.ReadOnly = true;
        this.locations.WordWrap = false;
        this.locations.DetectUrls = false;
        this.locations.BorderStyle = BorderStyle.None;
        this.locations.BackColor = DashboardTheme.Surface;
        this.locations.ForeColor = DashboardTheme.Muted;
        this.locations.TabStop = false;
        this.locations.AccessibleName = "Spielerpositionen";
        this.arena.ProgressChange += (number, complete) =>
        {
            if (this.view?.Run is { } run) this.ArenaChanged?.Invoke(run.Id, number, complete);
        };
        this.Controls.Add(this.map, 0, 0);
        var legend = DashboardScrollHost.ForText(this.locations);
        legend.Dock = DockStyle.Fill;
        legend.TabStop = false;
        this.Controls.Add(legend, 0, 1);
        this.Controls.Add(this.arena, 0, 2);
    }

    public event Action<Guid, int, bool>? ArenaChanged;

    public void Render(ClientView view)
    {
        this.view = view;
        var peers = view.Run?.Players ?? [];
        var positions = peers.Select(p =>
        {
            bool local = p.Id == view.LocalPlayerId;
            return new MapPlayer(p.Name, local && view.IsLocalRun ? view.Marker : p.Marker,
                local ? view.Snapshot?.Area ?? p.Area : p.Area,
                local ? view.Snapshot is not null : p.Connection == "Ready", local,
                local ? view.Snapshot?.Position ?? p.Position : p.Position);
        }).ToList();
        if (!positions.Any(p => p.Local) && view.Snapshot is { } snapshot)
            positions.Insert(0, new MapPlayer(view.Player.Length > 0 ? view.Player : "Du", view.Marker,
                snapshot.Area, true, true, snapshot.Position));

        this.map.UpdatePlayers(positions.ToArray());
        string legend = positions.Count == 0 ? "Warte auf Spielerpositionen."
            : string.Join(Environment.NewLine, positions.Select(p => $"{MarkerPalette.Glyph(p.Marker)}  {p.Name}: " +
                (p.Area is int area && area != 255 ? RubyNames.Area(area) + (p.Live ? "" : " (zuletzt)") : "Warte auf Position")));
        if (this.locations.Text.Replace("\r", "") != legend.Replace("\r", ""))
        {
            this.locations.Text = legend;
            int start = 0;
            for (int index = 0; index < positions.Count; index++)
            {
                this.locations.Select(start, this.locations.Lines[index].Length);
                this.locations.SelectionColor = HoennMap.PlayerColor(index, positions[index].Live);
                start += this.locations.Lines[index].Length + 1;
            }
            this.locations.Select(0, 0);
        }
        this.arena.Render(view.Snapshot, peers.FirstOrDefault(p => p.Id == view.LocalPlayerId), view.Run?.Phase == RunPhase.Active);
    }

    public void SetMapArtwork(Bitmap? image, bool attempted = false) => this.map.SetArtwork(image, attempted);
}
