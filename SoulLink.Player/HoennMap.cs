using SoulLink;

namespace SoulLink.Player;

internal sealed record MapPlayer(string Name, PlayerMarker Marker, int? Area, bool Live, bool Local, MapPosition? Position = null,
    string? SyncStatus = null, int? Money = null, int? Color = null);

internal sealed class HoennMap : Control
{
    private MapPlayer[] players = [];
    private Bitmap? artwork;
    private RubyMapLabels? labels;
    private byte[]? flags;
    private bool attempted;
    private Point? pointer;

    public HoennMap()
    {
        this.DoubleBuffered = true;
        this.AccessibleName = "Hoenn Minimap";
        this.BackColor = DashboardTheme.Surface;
    }

    public void UpdatePlayers(MapPlayer[] players, byte[]? flags)
    {
        if (this.players.SequenceEqual(players) && this.flags.AsSpan().SequenceEqual(flags)) return;
        this.players = players;
        this.flags = flags;
        this.Invalidate();
    }

    public static Color PlayerColor(int index, MapPlayer player) => player.Live
        ? Color.FromArgb(unchecked((int)0xFF000000) | PlayerColors.Resolve(index, player.Color)) : DashboardTheme.Muted;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (this.pointer == e.Location) return;
        this.pointer = e.Location;
        this.Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        this.pointer = null;
        this.Invalidate();
    }

    public void SetArtwork(Bitmap? image, RubyMapLabels? labels, bool attempted = false)
    {
        this.artwork?.Dispose();
        this.artwork = image;
        this.labels = labels;
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
        var chart = new Rectangle(3, 22, Math.Max(1, this.Width - 6), Math.Max(1, this.Height - 24));
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

        MapPlayer? hoveredPlayer = null;
        Color hoveredColor = DashboardTheme.Accent;
        var indexes = this.players.Select((player, index) => (Player: player, Index: index, Point: Project(player)));
        foreach (var group in indexes.Where(p => p.Point.HasValue).GroupBy(p => p.Point))
        {
            var stack = group.ToArray();
            for (int i = 0; i < stack.Length; i++)
            {
                var (player, index, point) = stack[i];
                double angle = i * Math.Tau / stack.Length;
                float orbit = stack.Length == 1 ? 0 : 10 + Math.Max(0, stack.Length - 4);
                float x = left + point!.Value.X * scale + (float)Math.Cos(angle) * orbit;
                float y = top + point.Value.Y * scale + (float)Math.Sin(angle) * orbit;
                using var backing = new SolidBrush(DashboardTheme.Background);
                var marker = new RectangleF(x - 10, y - 10, 20, 20);
                e.Graphics.FillEllipse(backing, marker);
                var color = PlayerColor(index, player);
                if (this.pointer is { } cursor && ((cursor.X - x) * (cursor.X - x)) + ((cursor.Y - y) * (cursor.Y - y)) <= 100)
                {
                    hoveredPlayer = player;
                    hoveredColor = color;
                }
                using var pen = new Pen(color, player.Local ? 2f : 1f);
                if (!player.Live) pen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dot;
                e.Graphics.DrawEllipse(pen, marker);
                using var glyph = new System.Drawing.Drawing2D.GraphicsPath();
                glyph.AddString(MarkerPalette.Glyph(player.Marker), this.Font.FontFamily, (int)FontStyle.Regular, 12,
                    PointF.Empty, StringFormat.GenericTypographic);
                var ink = glyph.GetBounds();
                using var shift = new System.Drawing.Drawing2D.Matrix();
                shift.Translate(x - ink.X - ink.Width / 2, y - ink.Y - ink.Height / 2);
                glyph.Transform(shift);
                using var foreground = new SolidBrush(color);
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                e.Graphics.FillPath(foreground, glyph);
            }
        }
        if (this.pointer is not { } mouse) return;

        int column = (int)MathF.Floor((mouse.X - left) / scale);
        int row = (int)MathF.Floor((mouse.Y - top) / scale);
        int? area = hoveredPlayer?.Area ?? RubyDashboardCatalog.AreaAt(column, row);
        if (area is null || area == 255 || (hoveredPlayer is null && !RubyMapLabels.IsVisible(area.Value, this.flags))) return;
        if (hoveredPlayer is null)
        {
            using var frame = new Pen(DashboardTheme.Accent, 1.5f);
            e.Graphics.DrawRectangle(frame, left + column * scale, top + row * scale, scale, scale);
        }

        var lines = new List<string> { this.labels?.Names.GetValueOrDefault(area.Value) ?? RubyNames.Area(area.Value) };
        if (hoveredPlayer is { } playerAtPoint)
        {
            lines.Add(playerAtPoint.Name);
            if (Project(playerAtPoint) is { } projected)
            {
                column = (int)projected.X;
                row = (int)projected.Y;
            }
        }
        if (this.labels is not null && RubyDashboardCatalog.AreaAt(column, row) == area && RubyDashboardCatalog.LandmarkCellAt(column, row) is int cell)
            lines.AddRange(this.labels.LandmarkNames(area.Value, cell, this.flags));
        this.DrawHint(e.Graphics, mouse, string.Join('\n', lines), hoveredColor);
    }

    private void DrawHint(Graphics graphics, Point mouse, string label, Color accent)
    {
        const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
        var textSize = TextRenderer.MeasureText(label, this.Font, new Size(Math.Max(1, this.Width - 16), 0), flags);
        int width = Math.Min(this.Width - 4, textSize.Width + 18);
        int height = textSize.Height + 10;
        int x = Math.Clamp(mouse.X + 12, 2, Math.Max(2, this.Width - width - 2));
        int y = Math.Clamp(mouse.Y + 12 < this.Height - height ? mouse.Y + 12 : mouse.Y - height - 8,
            2, Math.Max(2, this.Height - height - 2));
        var panel = new Rectangle(x, y, width, height);
        DashboardTheme.Fill(graphics, panel, DashboardTheme.Background, 6);
        using var stripe = new SolidBrush(accent);
        graphics.FillRectangle(stripe, x + 3, y + 5, 2, height - 10);
        TextRenderer.DrawText(graphics, label, this.Font, new Rectangle(x + 10, y + 5, width - 12, textSize.Height), DashboardTheme.Text, flags);
    }

    private static PointF? Project(MapPlayer player)
    {
        var area = RubyDashboardCatalog.Areas.FirstOrDefault(a => a.Area == player.Area);
        if (area is null) return null;
        if (player.Position is not { Outdoor: true, Valid: true } position)
            return new PointF(area.X + area.Width / 2f, area.Y + area.Height / 2f);

        float x = ((position.X + 0.5f) / position.Width) * area.Width;
        float y = ((position.Y + 0.5f) / position.Height) * area.Height;
        if (area.Area == 29 && y >= 1) x = 0.5f; // Route 114 bends around the northern mountains.
        return new PointF(area.X + x, area.Y + y);
    }
}
