using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class ArenaPanel : TableLayoutPanel
{
    private readonly Label heading = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label levels = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Button complete = DashboardTheme.Button("Abschließen");
    private readonly Button revert = DashboardTheme.Button("Zurücknehmen");
    private int? next;
    private int? last;

    public ArenaPanel(DiscordIcons icons)
    {
        this.BackColor = DashboardTheme.Panel;
        this.ForeColor = DashboardTheme.Text;
        this.Padding = new Padding(7);
        this.ColumnCount = 3;
        this.RowCount = 2;
        this.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 49));
        this.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        this.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 126));
        this.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        this.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var stadium = new PictureBox { Dock = DockStyle.Fill, Image = icons.Get("arena"), SizeMode = PictureBoxSizeMode.Zoom, AccessibleName = "Arena" };
        this.Controls.Add(stadium, 0, 0);
        this.SetRowSpan(stadium, 2);
        this.Controls.Add(this.heading, 1, 0);
        this.Controls.Add(this.levels, 1, 1);
        this.levels.ForeColor = DashboardTheme.Gold;
        this.complete.Dock = DockStyle.Fill;
        this.revert.Dock = DockStyle.Fill;
        this.Controls.Add(this.complete, 2, 0);
        this.Controls.Add(this.revert, 2, 1);
        this.complete.Click += (_, _) => { if (this.next is int number) this.ProgressChange?.Invoke(number, true); };
        this.revert.Click += (_, _) => { if (this.last is int number) this.ProgressChange?.Invoke(number, false); };
    }

    public event Action<int, bool>? ProgressChange;

    public void Render(GameSnapshot? snapshot, ClientPlayerView? player, bool active)
    {
        byte? progress = player?.ArenaProgress ?? snapshot?.BadgeMask;
        var gym = progress is byte badges ? RubyDashboardCatalog.NextGym(badges) : null;
        this.next = gym?.Number;
        this.last = progress is byte done
            ? RubyDashboardCatalog.Gyms.LastOrDefault(g => (done & (1 << (g.Number - 1))) != 0)?.Number
            : null;
        this.complete.Enabled = active && this.next is not null;
        this.revert.Enabled = active && this.last is not null;
        this.heading.Text = gym is not null ? $"Arena {gym.Number}: {gym.Name}" : progress.HasValue ? "8 von 8 Orden" : "Nächste Arena";
        int[]? roster = gym is null ? null
            : snapshot?.BadgeMask is byte read && RubyDashboardCatalog.NextGym(read)?.Number == gym.Number ? snapshot.GymLevels ?? gym.Levels
            : snapshot?.GymLineups?.ElementAtOrDefault(gym.Number - 1) ?? gym.Levels;
        this.levels.Text = gym is not null ? $"Level: {string.Join(", ", roster!)}" : progress.HasValue ? "Alle Arenen abgeschlossen" : "Warte auf Spielstand";
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(this.Parent?.BackColor ?? DashboardTheme.Background);
        DashboardTheme.Fill(e.Graphics, this.ClientRectangle, this.BackColor);
    }
}
