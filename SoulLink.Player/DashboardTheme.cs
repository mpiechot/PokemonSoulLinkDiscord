using System.Drawing.Drawing2D;
using SoulLink;

namespace SoulLink.Player;

internal static class DashboardTheme
{
    public static readonly Color Background = Color.FromArgb(13, 21, 33);
    public static readonly Color Surface = Color.FromArgb(22, 34, 52);
    public static readonly Color Panel = Color.FromArgb(30, 47, 67);
    public static readonly Color Border = Color.FromArgb(49, 67, 88);
    public static readonly Color Text = Color.FromArgb(240, 246, 253);
    public static readonly Color Muted = Color.FromArgb(166, 188, 210);
    public static readonly Color Accent = Color.FromArgb(110, 225, 189);
    public static readonly Color Danger = Color.FromArgb(250, 145, 159);
    public static readonly Color Gold = Color.FromArgb(255, 211, 114);

    public static Button Button(string text)
    {
        var button = new RoundedButton
        {
            Text = text,
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            BackColor = Panel,
            ForeColor = Text,
            Margin = new Padding(2),
            Padding = new Padding(7, 0, 7, 0),
        };
        button.FlatAppearance.BorderColor = Border;
        button.FlatAppearance.MouseOverBackColor = Border;
        return button;
    }

    public static GraphicsPath Rounded(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        int diameter = Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height));
        if (diameter <= 0) return path;
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void Fill(Graphics graphics, Rectangle bounds, Color color, int radius = 10)
    {
        using var path = Rounded(bounds, radius);
        using var brush = new SolidBrush(color);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.FillPath(brush, path);
    }

    private sealed class RoundedButton : Button
    {
        private bool hovered;

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); this.hovered = true; this.Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); this.hovered = false; this.Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(this.Parent?.BackColor ?? Background);
            var bounds = new Rectangle(1, 1, this.Width - 2, this.Height - 2);
            var color = this.Enabled && this.hovered ? this.FlatAppearance.MouseOverBackColor : this.BackColor;
            Fill(e.Graphics, bounds, color, 7);
            if (this.Focused && this.ShowFocusCues)
            {
                using var path = Rounded(bounds, 7);
                using var pen = new Pen(Accent);
                e.Graphics.DrawPath(pen, path);
            }
            TextRenderer.DrawText(e.Graphics, this.Text, this.Font, bounds, this.Enabled ? this.ForeColor : Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
        }
    }

    public static string? RubyType(byte? id) => id switch
    {
        0 => "normal",
        1 => "fighting",
        2 => "flying",
        3 => "poison",
        4 => "ground",
        5 => "rock",
        6 => "bug",
        7 => "ghost",
        8 => "steel",
        10 => "fire",
        11 => "water",
        12 => "grass",
        13 => "electric",
        14 => "psychic",
        15 => "ice",
        16 => "dragon",
        17 => "dark",
        _ => null,
    };
}

internal sealed class DiscordIcons : IDisposable
{
    private readonly Dictionary<string, Image> icons = new(StringComparer.Ordinal);

    public Image? Get(string name)
    {
        if (this.icons.TryGetValue(name, out var icon)) return icon;
        using var stream = typeof(DiscordIcons).Assembly.GetManifestResourceStream($"SoulLink.Player.Icons.{name}.png");
        if (stream is null) return null;
        using var original = Image.FromStream(stream);
        return this.icons[name] = new Bitmap(original);
    }

    public void Dispose()
    {
        foreach (var icon in this.icons.Values) icon.Dispose();
        this.icons.Clear();
    }
}
