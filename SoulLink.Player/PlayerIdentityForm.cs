using SoulLink;

namespace SoulLink.Player;

internal sealed class PlayerIdentityForm : PlayerWindow
{
    private readonly TextBox name = RunDialogs.TextInput("Spielername", 64);
    private readonly ComboBox marker = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Top };

    public PlayerIdentityForm(string? initialName, PlayerMarker initialMarker)
    {
        RunDialogs.Configure(this, "Dein Profil für diesen Run", new Size(415, 225));
        this.Font = new Font("Segoe UI", 11);
        this.name.Dock = DockStyle.Top;
        DashboardTheme.StyleComboBox(this.marker);
        this.name.Text = initialName ?? "";
        foreach (var icon in Enum.GetValues<PlayerMarker>()) this.marker.Items.Add(MarkerPalette.Label(icon));
        this.marker.SelectedIndex = Enum.IsDefined(initialMarker) ? (int)initialMarker : 0;
        var confirm = DashboardTheme.Button("Run beitreten");
        confirm.Dock = DockStyle.Fill;
        confirm.DialogResult = DialogResult.OK;
        var later = DashboardTheme.Button("Später");
        later.Dock = DockStyle.Fill;
        later.DialogResult = DialogResult.Cancel;
        confirm.Enabled = !string.IsNullOrWhiteSpace(this.name.Text);
        this.name.TextChanged += (_, _) => confirm.Enabled = !string.IsNullOrWhiteSpace(this.name.Text);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 40, ColumnCount = 2 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        buttons.Controls.Add(confirm, 0, 0);
        buttons.Controls.Add(later, 1, 0);
        this.Controls.Add(buttons);
        this.Controls.Add(this.marker);
        this.Controls.Add(new Label { Text = "Dein Icon auf der Karte", Dock = DockStyle.Top, Height = 33, TextAlign = ContentAlignment.BottomLeft });
        this.Controls.Add(this.name);
        this.Controls.Add(new Label { Text = "Spielername", Dock = DockStyle.Top, Height = 28 });
        this.AcceptButton = confirm;
        this.CancelButton = later;
    }

    public string PlayerName => this.name.Text.Trim();

    public PlayerMarker Marker => (PlayerMarker)this.marker.SelectedIndex;
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
