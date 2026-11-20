using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class RunDashboard : TableLayoutPanel
{
    private static readonly string[] Tabs = ["Team", "Links", "Box", "Tot", "Shinies", "Feed"];
    private readonly ComboBox player = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly HoennMap map = new() { Dock = DockStyle.Fill };
    private readonly RichTextBox locations = new();
    private readonly Label arena = new();
    private readonly TabControl navigation = new();
    private readonly DashboardCards[] cards;
    private readonly RubySprites sprites;
    private ClientView? view;
    private PlayerChoice[] choices = [];

    public RunDashboard(string spriteDirectory)
    {
        this.sprites = new RubySprites(spriteDirectory);
        this.Dock = DockStyle.Fill;
        this.BackColor = DashboardTheme.Surface;
        this.Padding = new Padding(9);
        this.ColumnCount = 1;
        this.RowCount = 5;
        this.RowStyles.Add(new RowStyle(SizeType.Absolute, 172));
        this.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        this.RowStyles.Add(new RowStyle(SizeType.Absolute, 63));
        this.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        this.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        this.locations.Dock = DockStyle.Fill;
        this.locations.Multiline = true;
        this.locations.ReadOnly = true;
        this.locations.ScrollBars = RichTextBoxScrollBars.Vertical;
        this.locations.WordWrap = false;
        this.locations.DetectUrls = false;
        this.locations.BorderStyle = BorderStyle.None;
        this.locations.BackColor = DashboardTheme.Surface;
        this.locations.ForeColor = DashboardTheme.Muted;
        this.locations.TabStop = false;
        this.locations.AccessibleName = "Spielerpositionen";
        this.arena.Dock = DockStyle.Fill;
        this.arena.Padding = new Padding(9, 7, 0, 0);
        this.arena.BackColor = DashboardTheme.Panel;
        this.arena.ForeColor = DashboardTheme.Text;
        this.arena.AccessibleName = "Nächste Arena";
        this.player.AccessibleName = "Spieler auswählen";
        this.player.BackColor = DashboardTheme.Panel;
        this.player.ForeColor = DashboardTheme.Text;
        this.player.SelectedIndexChanged += (_, _) => this.RenderCards();
        this.navigation.Dock = DockStyle.Fill;
        this.navigation.SelectedIndexChanged += (_, _) => this.RenderCards();
        this.cards = new DashboardCards[Tabs.Length];

        for (int i = 0; i < Tabs.Length; i++)
        {
            var tab = new TabPage(Tabs[i]) { BackColor = DashboardTheme.Surface };
            var list = this.cards[i] = new DashboardCards(this.sprites) { Dock = DockStyle.Fill };
            tab.Controls.Add(list);
            this.navigation.TabPages.Add(tab);
        }

        this.Controls.Add(this.map, 0, 0);
        this.Controls.Add(this.locations, 0, 1);
        this.Controls.Add(this.arena, 0, 2);
        this.Controls.Add(this.player, 0, 3);
        this.Controls.Add(this.navigation, 0, 4);
    }

    public void Render(ClientView view)
    {
        this.view = view;
        var peers = view.Run?.Players ?? [];
        var refreshed = peers.Select(p => new PlayerChoice(p.Id, p.Id == view.LocalPlayerId ? p.Name + " (du)" : p.Name)).ToArray();
        if (refreshed.Length == 0) refreshed = [new PlayerChoice(Guid.Empty, "Dein Spiel")];
        if (!refreshed.SequenceEqual(this.choices))
        {
            Guid? previous = (this.player.SelectedItem as PlayerChoice)?.Id;
            this.choices = refreshed;
            this.player.DataSource = this.choices;
            this.player.DisplayMember = nameof(PlayerChoice.Name);
            Guid? target = previous is null || previous == Guid.Empty ? view.LocalPlayerId : previous;
            int index = Array.FindIndex(this.choices, p => p.Id == target);
            this.player.SelectedIndex = index < 0 ? 0 : index;
        }

        var positions = peers.Select(p =>
        {
            bool local = p.Id == view.LocalPlayerId;
            int? area = local ? view.Snapshot?.Area ?? p.Area : p.Area;
            return new MapPlayer(p.Name, p.Marker, area, local ? view.Snapshot is not null : p.Connection == "Ready", local);
        }).ToList();
        if (!positions.Any(p => p.Local) && view.Snapshot is { } snapshot)
            positions.Insert(0, new MapPlayer(view.Player.Length > 0 ? view.Player : "Du", view.Marker, snapshot.Area, true, true));
        this.map.UpdatePlayers(positions.ToArray());
        string legend = positions.Count == 0
            ? "Spieler erscheinen auf der Karte, sobald Spieldaten vorliegen."
            : string.Join(Environment.NewLine, positions.Select(p => $"{MarkerPalette.Glyph(p.Marker)}  {p.Name}  ·  " +
                (p.Area is not null and not 255 ? AreaCatalog.Name(p.Area.Value) + (p.Live ? "" : " (zuletzt)") : "Warte auf Position")));
        if (this.locations.Text.Replace("\r", "") != legend.Replace("\r", ""))
        {
            this.locations.Text = legend;
            int start = 0;
            foreach (var (position, index) in positions.Select((position, index) => (position, index)))
            {
                this.locations.Select(start, this.locations.Lines[index].Length);
                this.locations.SelectionColor = HoennMap.PlayerColor(index, position.Live);
                start += this.locations.Lines[index].Length + 1;
            }
            this.locations.Select(0, 0);
        }
        this.arena.Text = ArenaText(view.Snapshot);
        this.RenderCards();
    }

    public void SetMapArtwork(Bitmap? image, bool attempted = false) => this.map.SetArtwork(image, attempted);

    protected override void Dispose(bool disposing)
    {
        if (disposing) this.sprites.Dispose();
        base.Dispose(disposing);
    }

    private static string ArenaText(GameSnapshot? snapshot)
    {
        if (snapshot?.BadgeMask is not byte badges) return "NÄCHSTE ARENA\nWarte auf deinen Ordenstand";
        var gym = RubyDashboardCatalog.NextGym(badges);
        string label = snapshot.GymLevels is null ? "Rubin-Standardlevel" : "Level aus deiner ROM";
        return gym is null
            ? "ARENA-FORTSCHRITT\n8 von 8 Orden"
            : $"ARENA {gym.Number}  ·  {gym.Name}\n{label}: {string.Join(" / ", snapshot.GymLevels ?? gym.Levels)}";
    }

    private void RenderCards()
    {
        if (this.view is not { } view || this.navigation.SelectedIndex < 0) return;
        var selected = this.player.SelectedItem as PlayerChoice;
        var peer = view.Run?.Players.FirstOrDefault(p => p.Id == selected?.Id);
        bool local = peer is null || peer.Id == view.LocalPlayerId;
        var data = peer?.Pokemon ?? [];
        var seen = local && view.Snapshot is { } snapshot
            ? snapshot.Pokemon.Select(p => new DashboardPokemon(p, data.Any(old => old.State.Identity == p.Identity && old.Dead), true)).ToArray()
            : data;
        int panel = this.navigation.SelectedIndex;
        this.player.Enabled = (panel is 0 or 2 or 3 or 4) && this.choices.Length > 1;
        var entries = panel switch
        {
            0 => seen.Where(p => p.Present && p.State.Container == Placement.Party).OrderBy(p => p.State.Slot).Select(Card).ToArray(),
            1 => (view.Run?.Links ?? []).SelectMany(LinkCards).ToArray(),
            2 => seen.Where(p => p.Present && p.State.Container == Placement.Box).OrderBy(p => p.State.Box).ThenBy(p => p.State.Slot).Select(Card).ToArray(),
            3 => data.Where(p => p.Dead).Select(Card).ToArray(),
            4 => (data.Length > 0 ? data : seen).Where(p => p.State.IsShiny).Select(Card).ToArray(),
            5 => (view.Run?.History ?? []).Reverse().Select(e => new DashboardEntry(EventTitle(e.Kind), e.Message, e.At.ToLocalTime().ToString("HH:mm"))).ToArray(),
            _ => [],
        };
        string empty = panel switch
        {
            0 => "Noch kein Team erfasst",
            1 => "Noch keine Links erfasst",
            2 => "Keine Pokémon in der Box erfasst",
            3 => "Keine Todesfälle erfasst",
            4 => "Keine Shinies erfasst",
            _ => "Noch keine Ereignisse",
        };
        this.cards[panel].Render(entries.Length == 0 ? [new DashboardEntry(empty, "")] : entries);
    }

    private static DashboardEntry Card(DashboardPokemon pokemon)
    {
        var state = pokemon.State;
        string heading = state.IsEgg ? "Ei" : state.DisplayName;
        if (heading.Length > 0) heading = char.ToUpperInvariant(heading[0]) + heading[1..];
        if (state.IsShiny && !state.IsEgg) heading = "★ " + heading;
        string details = state.Container == Placement.Party
            ? $"Lv. {state.Level}  ·  KP {state.Hp}/{state.MaxHp}"
            : $"Box {(state.Box ?? 0) + 1}  ·  Platz {state.Slot + 1}";
        return new DashboardEntry(heading, details, pokemon.Dead ? "TOT" : "", state);
    }

    private static IEnumerable<DashboardEntry> LinkCards(DashboardLink link)
    {
        string status = link.State switch
        {
            "ACTIVE" => "IM TEAM",
            "BOX" => "BOX",
            "DEAD" => "TOT",
            "PARTIAL" => "OFFEN",
            "FAILED" => "VERPASST",
            _ => "TEAMKONFLIKT",
        };
        yield return new DashboardEntry(link.Area, $"{link.Members.Length} Spieler im Link", status);
        foreach (var member in link.Members)
        {
            var card = Card(member.Pokemon);
            yield return card with { Detail = $"{member.Player}  ·  {card.Detail}" };
        }
    }

    private static string EventTitle(string kind) => kind switch
    {
        "caught" or "extra-catch" => "Fang",
        "linked" => "Link vollständig",
        "evolved" => "Entwicklung",
        "death" or "death-confirmed" => "Todesfall",
        "death-undone" => "Tod zurückgenommen",
        "encounter-failed" => "Fang verpasst",
        "badge" => "Orden",
        _ => "Run",
    };

    private sealed record PlayerChoice(Guid Id, string Name);
}

internal sealed record DashboardEntry(string Title, string Detail, string Badge = "", PokemonState? Pokemon = null);

internal sealed class DashboardCards : ListBox
{
    private readonly RubySprites sprites;
    private readonly Font heading = new("Segoe UI", 10, FontStyle.Bold);
    private readonly Font small = new("Segoe UI", 8);
    private DashboardEntry[] current = [];

    public DashboardCards(RubySprites sprites)
    {
        this.sprites = sprites;
        this.DrawMode = DrawMode.OwnerDrawFixed;
        this.ItemHeight = 72;
        this.IntegralHeight = false;
        this.BorderStyle = BorderStyle.None;
        this.BackColor = DashboardTheme.Surface;
        this.ForeColor = DashboardTheme.Text;
    }

    public void Render(DashboardEntry[] entries)
    {
        if (this.current.SequenceEqual(entries)) return;
        int top = this.TopIndex;
        this.current = entries;
        this.BeginUpdate();
        try
        {
            this.Items.Clear();
            this.Items.AddRange(entries);
            if (entries.Length > 0) this.TopIndex = Math.Clamp(top, 0, entries.Length - 1);
        }
        finally { this.EndUpdate(); }
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= this.current.Length) return;
        var entry = this.current[e.Index];
        var rect = Rectangle.Inflate(e.Bounds, -3, -3);
        var appearance = entry.Pokemon is { IsEgg: false } state ? RubyDashboardCatalog.Appearance(state.Species) : null;
        if (appearance is not null)
        {
            appearance = appearance with
            {
                Type1 = DashboardTheme.RubyType(entry.Pokemon!.RubyType1) ?? appearance.Type1,
                Type2 = DashboardTheme.RubyType(entry.Pokemon.RubyType2) ?? appearance.Type2,
            };
        }
        using var card = new SolidBrush(DashboardTheme.Panel);
        e.Graphics.FillRectangle(card, rect);
        int textX = rect.X + 10;
        if (appearance is not null)
        {
            var icon = new Rectangle(textX, rect.Y + 8, 46, 46);
            using var tile = new SolidBrush(DashboardTheme.Surface);
            e.Graphics.FillEllipse(tile, icon);
            if (this.sprites.Find(appearance, entry.Pokemon!.IsShiny) is { } image)
            {
                e.Graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                e.Graphics.DrawImage(image, icon);
            }
            else
            {
                TextRenderer.DrawText(e.Graphics, $"#{appearance.NationalDex}", this.small, icon,
                    DashboardTheme.Type(appearance.Type1).Color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            textX += 56;
        }

        int width = Math.Max(1, rect.Right - textX - 7);
        TextRenderer.DrawText(e.Graphics, entry.Title, this.heading, new Rectangle(textX, rect.Y + 5, width - (entry.Badge.Length > 0 ? 92 : 0), 20),
            entry.Pokemon?.IsShiny == true ? DashboardTheme.Gold : DashboardTheme.Text, TextFormatFlags.EndEllipsis);
        if (entry.Badge.Length > 0)
        {
            TextRenderer.DrawText(e.Graphics, entry.Badge, this.small, new Rectangle(rect.Right - 96, rect.Y + 6, 90, 20),
                entry.Badge == "TOT" ? DashboardTheme.Danger : DashboardTheme.Accent, TextFormatFlags.Right);
        }
        TextRenderer.DrawText(e.Graphics, entry.Detail, this.small, new Rectangle(textX, rect.Y + 25, width, appearance is null ? 37 : 16),
            DashboardTheme.Muted, TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        if (appearance is not null)
        {
            int x = textX;
            foreach (var kind in new[] { appearance.Type1, appearance.Type2 }.Distinct())
            {
                var type = DashboardTheme.Type(kind);
                int tagWidth = TextRenderer.MeasureText(type.Label, this.small).Width + 9;
                using var chip = new SolidBrush(DashboardTheme.Surface);
                var badge = new Rectangle(x, rect.Y + 43, tagWidth, 16);
                e.Graphics.FillRectangle(chip, badge);
                TextRenderer.DrawText(e.Graphics, type.Label, this.small, badge, type.Color,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                x += tagWidth + 5;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.heading.Dispose();
            this.small.Dispose();
        }
        base.Dispose(disposing);
    }
}
