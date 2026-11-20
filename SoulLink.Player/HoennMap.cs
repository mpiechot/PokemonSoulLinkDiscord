using SoulLink;

namespace SoulLink.Player;

internal sealed record MapPlayer(string Name, PlayerMarker Marker, int? Area, bool Live, bool Local);

internal sealed class HoennMap : Control
{
    private static readonly Color[] Colors =
    [
        DashboardTheme.Accent, Color.FromArgb(111, 191, 255), DashboardTheme.Gold,
        Color.FromArgb(203, 164, 255), DashboardTheme.Danger, Color.FromArgb(173, 227, 124),
    ];
    private MapPlayer[] players = [];
    private Bitmap? artwork;
    private bool attempted;

    public HoennMap()
    {
        this.DoubleBuffered = true;
        this.AccessibleName = "Hoenn Minimap";
        this.BackColor = DashboardTheme.Surface;
    }

    public void UpdatePlayers(MapPlayer[] players)
    {
        if (this.players.SequenceEqual(players)) return;
        this.players = players;
        this.Invalidate();
    }

    public static Color PlayerColor(int index, bool live) => live ? Colors[index % Colors.Length] : DashboardTheme.Muted;

    public void SetArtwork(Bitmap? image, bool attempted = false)
    {
        this.artwork?.Dispose();
        this.artwork = image;
        this.attempted = attempted;
        this.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) this.artwork?.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        TextRenderer.DrawText(e.Graphics, "HOENN  /  LIVE-KARTE", this.Font, new Point(11, 4), DashboardTheme.Muted);
        var chart = new Rectangle(10, 23, Math.Max(1, this.Width - 20), Math.Max(1, this.Height - 28));
        if (this.artwork is null)
        {
            string message = this.attempted ? "Keine Originalkarte in dieser ROM erkannt." : "Die Originalkarte wird aus der Rubin-ROM geladen.";
            TextRenderer.DrawText(e.Graphics, message, this.Font, chart,
                DashboardTheme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        float scale = Math.Min(chart.Width / 28f, chart.Height / 15f);
        if (scale < 2) return;
        float left = chart.X + (chart.Width - 28 * scale) / 2;
        float top = chart.Y + (chart.Height - 15 * scale) / 2;
        e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
        e.Graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        e.Graphics.DrawImage(this.artwork, new RectangleF(left, top, 28 * scale, 15 * scale));

        var indexes = this.players.Select((player, index) => (player, index));
        foreach (var group in indexes.Where(p => p.player.Area.HasValue).GroupBy(p => p.player.Area))
        {
            var area = RubyDashboardCatalog.Areas.FirstOrDefault(a => a.Area == group.Key);
            if (area is null) continue;
            var stack = group.ToArray();
            for (int i = 0; i < stack.Length; i++)
            {
                var (player, index) = stack[i];
                double angle = i * Math.Tau / stack.Length;
                float orbit = stack.Length == 1 ? 0 : 10 + Math.Max(0, stack.Length - 4);
                float x = left + (area.X + area.Width / 2f) * scale + (float)Math.Cos(angle) * orbit;
                float y = top + (area.Y + area.Height / 2f) * scale + (float)Math.Sin(angle) * orbit;
                x = Math.Clamp(x, 10, Math.Max(10, this.Width - 10));
                y = Math.Clamp(y, 33, Math.Max(33, this.Height - 10));
                using var backing = new SolidBrush(DashboardTheme.Background);
                var marker = Rectangle.FromLTRB((int)x - 10, (int)y - 10, (int)x + 10, (int)y + 10);
                e.Graphics.FillEllipse(backing, marker);
                var color = PlayerColor(index, player.Live);
                using var pen = new Pen(color, player.Local ? 2f : 1f);
                if (!player.Live) pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                e.Graphics.DrawEllipse(pen, marker);
                TextRenderer.DrawText(e.Graphics, MarkerPalette.Glyph(player.Marker), this.Font, marker, color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }
}
