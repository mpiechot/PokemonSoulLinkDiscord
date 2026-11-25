using System.Globalization;
using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

/// <summary>Displays the Rubin PokéNav region map and arena data.</summary>
internal sealed class RubyRunDashboard : TableLayoutPanel
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private readonly HoennMap map = new() { Dock = DockStyle.Fill };
    private readonly RichTextBox locations = new();
    private Color[] legendColors = [];

    public RubyRunDashboard()
    {
        this.Dock = DockStyle.Fill;
        this.BackColor = DashboardTheme.Surface;
        this.Padding = new Padding(7);
        this.ColumnCount = 1;
        this.RowCount = 2;
        this.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        this.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));

        this.locations.Dock = DockStyle.Fill;
        this.locations.Multiline = true;
        this.locations.ReadOnly = true;
        this.locations.WordWrap = false;
        this.locations.DetectUrls = false;
        this.locations.BorderStyle = BorderStyle.None;
        this.locations.BackColor = DashboardTheme.Surface;
        this.locations.ForeColor = DashboardTheme.Muted;
        this.locations.TabStop = false;
        this.locations.AccessibleName = "Spielerpositionen und Geld";
        this.Controls.Add(this.map, 0, 0);
        var legend = DashboardScrollHost.ForText(this.locations);
        legend.Dock = DockStyle.Fill;
        legend.TabStop = false;
        this.Controls.Add(legend, 0, 1);
    }

    public void Render(ClientView view)
    {
        var peers = view.Run?.Players ?? [];
        var positions = peers.Select(p =>
        {
            bool local = p.Id == view.LocalPlayerId;
            bool localData = local && view.IsLocalRun;
            return new MapPlayer(p.Name, localData ? view.Marker : p.Marker,
                localData ? view.Snapshot?.Area ?? p.Area : p.Area,
                localData ? view.Snapshot is not null : p.Connection == "Ready", local,
                localData ? view.Snapshot?.Position ?? p.Position : p.Position, localData ? null : p.Connection switch
                {
                    "OutOfSync" => "Anderer Spielstand",
                    "Synchronizing" => "Wartet auf Spieldaten",
                    "Stale" => "Spieldaten pausiert",
                    "Offline" => "Offline",
                    _ => null,
                }, localData ? view.Snapshot?.Money ?? p.Money : p.Money, localData ? view.Color : p.Color);
        }).ToList();
        if (!positions.Any(p => p.Local) && view.Snapshot is { } snapshot)
            positions.Insert(0, new MapPlayer(view.Player.Length > 0 ? view.Player : "Du", view.Marker,
                snapshot.Area, true, true, snapshot.Position, Money: snapshot.Money, Color: view.Color));

        this.map.UpdatePlayers(positions.ToArray(), view.Snapshot?.PokeNavFlags);
        string legend = positions.Count == 0 ? "Warte auf Spielerpositionen."
            : string.Join(Environment.NewLine, positions.Select(p => $"{MarkerPalette.Glyph(p.Marker)}  {p.Name}: " +
                (p.Area is int area && area != 255 ? RubyNames.Area(area) : "Warte auf Position") +
                (p.Money is int wallet ? $"  |  {wallet.ToString("N0", German)} ₽" : "") +
                (p.SyncStatus is { } status ? $" ({status})" : p.Live ? "" : " (zuletzt)")));
        Color[] colors = positions.Select((player, index) => HoennMap.PlayerColor(index, player)).ToArray();
        if (!this.legendColors.SequenceEqual(colors) || this.locations.Text.Replace("\r", "") != legend.Replace("\r", ""))
        {
            this.legendColors = colors;
            this.locations.Text = legend;
            int start = 0;
            for (int index = 0; index < positions.Count; index++)
            {
                this.locations.Select(start, this.locations.Lines[index].Length);
                this.locations.SelectionColor = colors[index];
                start += this.locations.Lines[index].Length + 1;
            }
            this.locations.Select(0, 0);
        }
    }

    public void SetCartridge(string? path)
    {
        if (path is null) this.map.SetArtwork(null, null);
        else this.map.SetArtwork(RubyMapArtwork.Read(path), RubyMapArtwork.ReadLabels(path), attempted: true);
    }
}

internal sealed class RubyMapForm : ToolWindow
{
    internal RubyMapForm() : base("Hoenn-Karte", new Size(552, 402), resizable: true)
    {
        this.Body.Controls.Add(this.Map);
        this.TopMost = true;
        this.MinimumSize = new Size(350, 290);
    }

    internal RubyRunDashboard Map { get; } = new();

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == 0x0021)
        {
            message.Result = 3; // MA_NOACTIVATE
            return;
        }
        base.WndProc(ref message);
    }
}
