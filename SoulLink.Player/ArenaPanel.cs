using System.Drawing.Drawing2D;
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

    public ArenaPanel()
    {
        this.BackColor = DashboardTheme.Panel;
        this.ForeColor = DashboardTheme.Text;
        this.Padding = new Padding(6);
        this.ColumnCount = 2;
        this.RowCount = 3;
        this.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        this.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        this.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        this.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
        this.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var stadium = new StadiumIcon { Dock = DockStyle.Fill, AccessibleName = "Arena" };
        this.Controls.Add(stadium, 0, 0);
        this.SetRowSpan(stadium, 2);
        this.Controls.Add(this.heading, 1, 0);
        this.Controls.Add(this.levels, 1, 1);
        this.levels.ForeColor = DashboardTheme.Gold;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Margin = new Padding(0) };
        actions.Controls.AddRange([this.complete, this.revert]);
        this.Controls.Add(actions, 0, 2);
        this.SetColumnSpan(actions, 2);
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
        this.heading.Text = gym is not null ? $"Arena {gym.Number} · {gym.Name}" : progress.HasValue ? "8 von 8 Orden" : "Nächste Arena";
        int[]? roster = gym is null ? null
            : snapshot?.BadgeMask is byte read && RubyDashboardCatalog.NextGym(read)?.Number == gym.Number ? snapshot.GymLevels ?? gym.Levels
            : snapshot?.GymLineups?.ElementAtOrDefault(gym.Number - 1) ?? gym.Levels;
        this.levels.Text = gym is not null ? $"Level  {string.Join("  ·  ", roster!)}" : progress.HasValue ? "Alle Arenen abgeschlossen" : "Warte auf Spielstand";
    }

    private sealed class StadiumIcon : Control
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var baseColor = new SolidBrush(DashboardTheme.Surface);
            using var field = new SolidBrush(Color.FromArgb(38, 88, 83));
            using var border = new Pen(DashboardTheme.Accent, 1.6f);
            var outer = new Rectangle(4, 12, 44, 25);
            var inner = new Rectangle(10, 9, 32, 20);
            e.Graphics.FillEllipse(baseColor, outer);
            e.Graphics.DrawEllipse(border, outer);
            e.Graphics.FillEllipse(field, inner);
            e.Graphics.DrawEllipse(border, inner);
            e.Graphics.DrawLine(border, 13, 19, 39, 19);
            e.Graphics.DrawEllipse(border, 22, 15, 8, 8);
        }
    }
}
