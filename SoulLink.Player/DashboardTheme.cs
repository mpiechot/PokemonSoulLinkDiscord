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
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    private static readonly Dictionary<string, (string Label, Color Color)> Types = new()
    {
        ["normal"] = ("Normal", Color.FromArgb(171, 182, 193)),
        ["fire"] = ("Feuer", Color.FromArgb(255, 139, 114)),
        ["water"] = ("Wasser", Color.FromArgb(119, 188, 255)),
        ["electric"] = ("Elektro", Color.FromArgb(255, 219, 109)),
        ["grass"] = ("Pflanze", Color.FromArgb(152, 223, 123)),
        ["ice"] = ("Eis", Color.FromArgb(171, 230, 243)),
        ["fighting"] = ("Kampf", Color.FromArgb(244, 161, 122)),
        ["poison"] = ("Gift", Color.FromArgb(216, 159, 251)),
        ["ground"] = ("Boden", Color.FromArgb(220, 184, 125)),
        ["flying"] = ("Flug", Color.FromArgb(178, 189, 255)),
        ["psychic"] = ("Psycho", Color.FromArgb(255, 159, 211)),
        ["bug"] = ("Käfer", Color.FromArgb(188, 224, 109)),
        ["rock"] = ("Gestein", Color.FromArgb(221, 200, 148)),
        ["ghost"] = ("Geist", Color.FromArgb(186, 175, 245)),
        ["dragon"] = ("Drache", Color.FromArgb(166, 163, 255)),
        ["dark"] = ("Unlicht", Color.FromArgb(195, 182, 178)),
        ["steel"] = ("Stahl", Color.FromArgb(174, 205, 219)),
    };

    public static (string Label, Color Color) Type(string type) => Types.GetValueOrDefault(type, (type, Muted));

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

internal sealed class RubySprites(string directory) : IDisposable
{
    private readonly Dictionary<string, Bitmap?> loaded = new();

    public Image? Find(RubySpeciesAppearance appearance, bool shiny)
    {
        string suffix = shiny ? "-shiny" : "";
        return this.Load($"{appearance.NationalDex}{suffix}.png");
    }

    public Image? TypeBadge(string type) => this.Load(Path.Combine("types", type + ".png"));

    private Image? Load(string key)
    {
        if (this.loaded.TryGetValue(key, out var result)) return result;
        try
        {
            string path = Path.Combine(directory, key);
            if (File.Exists(path) && new FileInfo(path).Length <= 256 * 1024)
            {
                using var file = File.OpenRead(path);
                using var sprite = Image.FromStream(file, useEmbeddedColorManagement: false, validateImageData: true);
                if (sprite.Width <= 256 && sprite.Height <= 256) result = new Bitmap(sprite);
            }
        }
        catch (Exception error) when (error is IOException or ArgumentException or UnauthorizedAccessException or OutOfMemoryException) { }
        this.loaded[key] = result;
        return result;
    }

    public void Dispose()
    {
        foreach (var sprite in this.loaded.Values) sprite?.Dispose();
        this.loaded.Clear();
    }
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
