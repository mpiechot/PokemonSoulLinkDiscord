using SoulLink;

namespace SoulLink.Player;

internal sealed class PlayerIdentityForm : ToolWindow
{
    private readonly TextBox name = RunDialogs.TextInput("Spielername", 64);
    private readonly DashboardComboBox marker = new() { Dock = DockStyle.Top, AccessibleName = "Karten-Icon" };
    private readonly DashboardComboBox color = new(item => ((ColorOption)item).Value is int rgb
        ? Color.FromArgb(unchecked((int)0xFF000000) | rgb) : DashboardTheme.Muted)
        { Dock = DockStyle.Top, AccessibleName = "Spielerfarbe", MaxDropDownItems = 14 };
    private readonly TextBox custom = RunDialogs.TextInput("Eigene Farbe als #RRGGBB", 7);
    private static readonly (string Label, int? Value)[] Colors =
    [
        ("Automatisch", null), ("Mint", 0x6EE1BD), ("Himmelblau", 0x6FBFFF), ("Gold", 0xFFD372),
        ("Flieder", 0xCBA4FF), ("Rosé", 0xFA919F), ("Hellgrün", 0xADE37C), ("Koralle", 0xFF7966),
        ("Türkis", 0x49DDE4), ("Orange", 0xFFAA65), ("Indigo", 0x8CA2FF), ("Weiß", 0xF1F5F9),
    ];

    public PlayerIdentityForm(string? initialName, PlayerMarker initialMarker, int? initialColor = null, bool editing = false)
        : base("Dein Profil für diesen Run", new Size(415, 350))
    {
        this.name.Dock = DockStyle.Top;
        foreach (var (label, value) in Colors) this.color.Items.Add(new ColorOption(label, value));
        int customIndex = this.color.Items.Add(new ColorOption("Eigene Farbe …", null));
        int selectedColor = Array.FindIndex(Colors, option => option.Value == initialColor);
        this.color.SelectedIndex = selectedColor < 0 ? customIndex : selectedColor;
        this.custom.Dock = DockStyle.Top;
        if (initialColor.HasValue) this.custom.Text = $"#{initialColor.Value:X6}";
        this.name.Text = initialName ?? "";
        foreach (var icon in Enum.GetValues<PlayerMarker>()) this.marker.Items.Add(MarkerPalette.Label(icon));
        this.marker.SelectedIndex = Enum.IsDefined(initialMarker) ? (int)initialMarker : 0;
        var confirm = Action(editing ? "Speichern" : "Run beitreten", DashboardTheme.Accent);
        confirm.Dock = DockStyle.Fill;
        confirm.DialogResult = DialogResult.OK;
        var later = Action(editing ? "Abbrechen" : "Später");
        later.Dock = DockStyle.Fill;
        later.DialogResult = DialogResult.Cancel;
        void UpdateFields()
        {
            this.custom.Visible = this.color.SelectedIndex == customIndex;
            confirm.Enabled = !string.IsNullOrWhiteSpace(this.name.Text) && (this.color.SelectedIndex != customIndex || this.CustomColor.HasValue);
        }
        this.name.TextChanged += (_, _) => UpdateFields();
        this.custom.TextChanged += (_, _) => UpdateFields();
        this.color.SelectedIndexChanged += (_, _) => UpdateFields();
        var buttons = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 40, ColumnCount = 2 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        buttons.Controls.Add(confirm, 0, 0);
        buttons.Controls.Add(later, 1, 0);
        var card = new ToolCard { Dock = DockStyle.Fill };
        card.Controls.Add(buttons);
        card.Controls.Add(this.custom);
        card.Controls.Add(this.color);
        card.Controls.Add(Field("Spielerfarbe"));
        card.Controls.Add(this.marker);
        card.Controls.Add(Field("Dein Icon auf der Karte"));
        card.Controls.Add(this.name);
        card.Controls.Add(Field("Spielername"));
        this.Body.Controls.Add(card);
        this.AcceptButton = confirm;
        this.CancelButton = later;
        UpdateFields();
    }

    public string PlayerName => this.name.Text.Trim();

    public PlayerMarker Marker => (PlayerMarker)this.marker.SelectedIndex;

    public int? SelectedColor => this.color.SelectedIndex == Colors.Length ? this.CustomColor : ((ColorOption)this.color.SelectedItem!).Value;

    private int? CustomColor
    {
        get
        {
            string hex = this.custom.Text.TrimStart('#');
            return hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out int color) ? color : null;
        }
    }

    private static Label Field(string text) => new() { Text = text, Dock = DockStyle.Top, Height = 28, TextAlign = ContentAlignment.MiddleLeft };

    private sealed record ColorOption(string Label, int? Value)
    {
        public override string ToString() => this.Label;
    }
}

internal static class MarkerPalette
{
    public static string Glyph(PlayerMarker marker) => marker switch
    {
        PlayerMarker.Diamond => "◆",
        PlayerMarker.Star => "★",
        PlayerMarker.Triangle => "▲",
        PlayerMarker.Square => "■",
        PlayerMarker.Heart => "♥",
        PlayerMarker.Moon => "☾",
        PlayerMarker.Sun => "☀",
        _ => "●",
    };

    public static string Label(PlayerMarker marker) => Glyph(marker) + "  " + (marker switch
    {
        PlayerMarker.Diamond => "Raute",
        PlayerMarker.Star => "Stern",
        PlayerMarker.Triangle => "Dreieck",
        PlayerMarker.Square => "Quadrat",
        PlayerMarker.Heart => "Herz",
        PlayerMarker.Moon => "Mond",
        PlayerMarker.Sun => "Sonne",
        _ => "Kreis",
    });
}
