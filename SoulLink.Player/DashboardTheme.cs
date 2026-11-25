using System.Drawing.Drawing2D;
using SoulLink;

namespace SoulLink.Player;

internal enum DashboardIcon { Minimize, Maximize, Close, Check, Undo, Box, Skull, Sparkle }

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

    public static Button ImageButton(string name, Image? image)
    {
        var button = Button("");
        button.AccessibleName = name;
        button.Image = image;
        button.AutoSize = false;
        button.Padding = new Padding(3);
        return button;
    }

    public static void DrawImage(Graphics canvas, Image image, Rectangle bounds, bool pixelArt = false)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        double scale = Math.Min((double)bounds.Width / image.Width, (double)bounds.Height / image.Height);
        int width = Math.Max(1, (int)Math.Round(image.Width * scale));
        int height = Math.Max(1, (int)Math.Round(image.Height * scale));
        var saved = canvas.Save();
        canvas.InterpolationMode = pixelArt ? InterpolationMode.NearestNeighbor : InterpolationMode.HighQualityBicubic;
        canvas.PixelOffsetMode = PixelOffsetMode.Half;
        canvas.DrawImage(image, new Rectangle(bounds.X + (bounds.Width - width) / 2, bounds.Y + (bounds.Height - height) / 2, width, height));
        canvas.Restore(saved);
    }

    public static Button IconButton(DashboardIcon icon, string name, Color background, Color hover, Color foreground)
    {
        var button = new RoundedButton(icon)
        {
            AccessibleName = name,
            FlatStyle = FlatStyle.Flat,
            BackColor = background,
            ForeColor = foreground,
            Margin = new Padding(2, 4, 2, 4),
            Size = new Size(44, 34),
        };
        button.FlatAppearance.MouseOverBackColor = hover;
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

    private sealed class RoundedButton(DashboardIcon? icon = null) : Button
    {
        private bool hovered;

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); this.hovered = true; this.Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); this.hovered = false; this.Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(this.Parent?.BackColor ?? Background);
            var bounds = new Rectangle(1, 1, this.Width - 2, this.Height - 2);
            var color = !this.Enabled && icon.HasValue ? Surface
                : this.Enabled && this.hovered ? this.FlatAppearance.MouseOverBackColor : this.BackColor;
            Fill(e.Graphics, bounds, color, 7);
            if (this.Focused && this.ShowFocusCues)
            {
                using var path = Rounded(bounds, 7);
                using var pen = new Pen(Accent);
                e.Graphics.DrawPath(pen, path);
            }
            var foreground = this.Enabled ? this.ForeColor : Muted;
            if (icon is { } symbol)
            {
                this.DrawIcon(e.Graphics, bounds, symbol, foreground);
                return;
            }
            if (this.Image is { } image)
            {
                int size = Math.Min((int)Math.Round(20 * this.DeviceDpi / 96f), bounds.Height - this.Padding.Vertical);
                var imageBounds = this.Text.Length == 0
                    ? Rectangle.Inflate(bounds, -this.Padding.Left, -this.Padding.Top)
                    : new Rectangle(bounds.X + this.Padding.Left, bounds.Y, size, bounds.Height);
                if (this.Text.Length == 0) imageBounds.Inflate(-Math.Max(0, (imageBounds.Width - size) / 2), 0);
                DrawImage(e.Graphics, image, imageBounds, pixelArt: this.Text.Length == 0);
                if (this.Text.Length == 0) return;
                bounds.X += size + this.Padding.Left + 2;
                bounds.Width -= size + this.Padding.Horizontal + 2;
            }
            TextRenderer.DrawText(e.Graphics, this.Text, this.Font, bounds, foreground,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
                TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping);
        }

        private void DrawIcon(Graphics canvas, Rectangle bounds, DashboardIcon icon, Color foreground)
        {
            var state = canvas.Save();
            canvas.TranslateTransform(bounds.Left + bounds.Width / 2f, bounds.Top + bounds.Height / 2f);
            float scale = this.DeviceDpi / 96f;
            canvas.ScaleTransform(scale, scale);
            using var pen = new Pen(foreground, 1.65f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            switch (icon)
            {
                case DashboardIcon.Minimize:
                    canvas.DrawLine(pen, -7, 0, 7, 0);
                    break;
                case DashboardIcon.Maximize when this.FindForm()?.WindowState == FormWindowState.Maximized:
                    canvas.DrawRectangle(pen, -7, -2, 10, 9);
                    canvas.DrawLines(pen, [new Point(6, 4), new Point(6, -6), new Point(-4, -6), new Point(-4, -3)]);
                    break;
                case DashboardIcon.Maximize:
                    canvas.DrawRectangle(pen, -6, -6, 12, 12);
                    break;
                case DashboardIcon.Close:
                    canvas.DrawLine(pen, -6, -6, 6, 6);
                    canvas.DrawLine(pen, -6, 6, 6, -6);
                    break;
                case DashboardIcon.Check:
                    canvas.DrawLines(pen, [new Point(-7, 0), new Point(-2, 5), new Point(7, -5)]);
                    break;
                case DashboardIcon.Undo:
                    canvas.DrawLines(pen, [new Point(-3, -7), new Point(-7, -3), new Point(-3, 1)]);
                    canvas.DrawBezier(pen, -7, -3, 13, -7, 10, 8, -1, 7);
                    break;
                case DashboardIcon.Box:
                    pen.Width = 2;
                    canvas.DrawRectangle(pen, -9, -7, 18, 5);
                    canvas.DrawLines(pen, [new Point(-7, -2), new Point(-7, 7), new Point(7, 7), new Point(7, -2)]);
                    canvas.DrawLine(pen, -2, 2, 2, 2);
                    break;
                case DashboardIcon.Skull:
                    using (var skull = new SolidBrush(foreground))
                    using (var cutout = new SolidBrush(this.Enabled && this.hovered ? this.FlatAppearance.MouseOverBackColor : this.BackColor))
                    {
                        canvas.FillEllipse(skull, -8, -8, 16, 13);
                        canvas.FillRectangle(skull, -4, 1, 8, 7);
                        canvas.FillEllipse(cutout, -5, -3, 4, 4);
                        canvas.FillEllipse(cutout, 1, -3, 4, 4);
                        canvas.FillRectangle(cutout, -0.7f, 5, 1.4f, 3);
                    }
                    break;
                case DashboardIcon.Sparkle:
                    using (var sparkle = new SolidBrush(foreground))
                    {
                        canvas.FillPolygon(sparkle, [new Point(0, -9), new Point(3, -3), new Point(9, 0), new Point(3, 3),
                            new Point(0, 9), new Point(-3, 3), new Point(-9, 0), new Point(-3, -3)]);
                    }
                    break;
            }
            canvas.Restore(state);
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
        var assembly = typeof(DiscordIcons).Assembly;
        using var stream = assembly.GetManifestResourceStream($"SoulLink.Player.PrivateIcons.{name}.png") ??
            assembly.GetManifestResourceStream($"SoulLink.Player.Icons.{name}.png");
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
