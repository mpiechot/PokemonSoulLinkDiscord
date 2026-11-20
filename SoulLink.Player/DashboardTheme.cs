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
        var button = new Button
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
    private readonly Dictionary<(int Number, bool Shiny), Bitmap?> loaded = new();

    public Image? Find(RubySpeciesAppearance appearance, bool shiny)
    {
        var key = (appearance.NationalDex, shiny);
        if (this.loaded.TryGetValue(key, out var result)) return result;
        try
        {
            string suffix = shiny ? "-shiny" : "";
            string path = Path.Combine(directory, $"{appearance.NationalDex}{suffix}.png");
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
