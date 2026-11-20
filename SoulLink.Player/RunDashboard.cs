using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class RunDashboard : TableLayoutPanel
{
    private static readonly string[] Tabs = ["Team", "Links", "Box", "Tot", "Shinies", "Fangorte"];
    private readonly ComboBox player = new() { Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly HoennMap map = new() { Dock = DockStyle.Fill };
    private readonly RichTextBox locations = new();
    private readonly ArenaPanel arena = new() { Dock = DockStyle.Fill };
    private readonly DashboardCards cards;
    private readonly FlowLayoutPanel places = new() { Dock = DockStyle.Fill, AutoScroll = true, Visible = false, Padding = new Padding(2) };
    private readonly Button[] navigation;
    private readonly RubySprites sprites;
    private ClientView? view;
    private PlayerChoice[] choices = [];
    private int selectedPanel;
    private string[] lastTags = [];

    public event Action<Guid, int, bool>? ArenaChanged;

    public RunDashboard(string spriteDirectory)
    {
        this.sprites = new RubySprites(spriteDirectory);
        this.Dock = DockStyle.Fill;
        this.BackColor = DashboardTheme.Surface;
        this.Padding = new Padding(9);
        this.ColumnCount = 1;
        this.RowCount = 6;
        this.RowStyles.Add(new RowStyle(SizeType.Absolute, 172));
        this.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        this.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        this.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        this.RowStyles.Add(new RowStyle(SizeType.AutoSize));
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
        this.arena.AccessibleName = "Nächste Arena";
        this.arena.ProgressChange += (number, complete) =>
        {
            if (this.view?.Run is { } run) this.ArenaChanged?.Invoke(run.Id, number, complete);
        };
        this.player.AccessibleName = "Spieler auswählen";
        this.player.BackColor = DashboardTheme.Panel;
        this.player.ForeColor = DashboardTheme.Text;
        this.player.SelectedIndexChanged += (_, _) => this.RenderCards();
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(0, 0, 0, 5) };
        this.navigation = new Button[Tabs.Length];
        for (int i = 0; i < Tabs.Length; i++)
        {
            int index = i;
            var tab = this.navigation[i] = DashboardTheme.Button(Tabs[i]);
            tab.Height = 29;
            tab.Click += (_, _) => this.SelectPanel(index);
            toolbar.Controls.Add(tab);
        }
        this.cards = new DashboardCards(this.sprites) { Dock = DockStyle.Fill };
        this.SelectPanel(0);

        this.Controls.Add(this.map, 0, 0);
        this.Controls.Add(this.locations, 0, 1);
        this.Controls.Add(this.arena, 0, 2);
        this.Controls.Add(this.player, 0, 3);
        this.Controls.Add(toolbar, 0, 4);
        var roster = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0) };
        roster.Controls.Add(this.cards);
        roster.Controls.Add(this.places);
        this.Controls.Add(roster, 0, 5);
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
            return new MapPlayer(p.Name, local && view.IsLocalRun ? view.Marker : p.Marker, area,
                local ? view.Snapshot is not null : p.Connection == "Ready", local, local ? view.Snapshot?.Position ?? p.Position : p.Position);
        }).ToList();
        if (!positions.Any(p => p.Local) && view.Snapshot is { } snapshot)
            positions.Insert(0, new MapPlayer(view.Player.Length > 0 ? view.Player : "Du", view.Marker, snapshot.Area, true, true, snapshot.Position));
        this.map.UpdatePlayers(positions.ToArray());
        string legend = positions.Count == 0
            ? "Spieler erscheinen auf der Karte, sobald Spieldaten vorliegen."
            : string.Join(Environment.NewLine, positions.Select(p => $"{MarkerPalette.Glyph(p.Marker)}  {p.Name}  ·  " +
                (p.Area is not null and not 255 ? RubyNames.Area(p.Area.Value) + (p.Live ? "" : " (zuletzt)") : "Warte auf Position")));
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
        this.arena.Render(view.Snapshot, peers.FirstOrDefault(p => p.Id == view.LocalPlayerId), view.Run?.Phase == RunPhase.Active);
        this.RenderCards();
    }

    public void SetMapArtwork(Bitmap? image, bool attempted = false) => this.map.SetArtwork(image, attempted);

    protected override void Dispose(bool disposing)
    {
        if (disposing) this.sprites.Dispose();
        base.Dispose(disposing);
    }

    private void SelectPanel(int index)
    {
        this.selectedPanel = index;
        for (int i = 0; i < this.navigation.Length; i++)
        {
            if (this.navigation[i] is not { } tab) continue;
            tab.ForeColor = i == index ? DashboardTheme.Accent : DashboardTheme.Text;
            tab.FlatAppearance.BorderColor = i == index ? DashboardTheme.Accent : DashboardTheme.Border;
        }
        this.RenderCards();
    }

    private void RenderCards()
    {
        if (this.view is not { } view) return;
        var selected = this.player.SelectedItem as PlayerChoice;
        var peer = view.Run?.Players.FirstOrDefault(p => p.Id == selected?.Id);
        bool local = peer is null || peer.Id == view.LocalPlayerId;
        var data = peer?.Pokemon ?? [];
        var seen = local && view.Snapshot is { } snapshot
            ? snapshot.Pokemon.Select(p => new DashboardPokemon(p, data.Any(old => old.State.Identity == p.Identity && old.Dead), true)).ToArray()
            : data;
        int panel = this.selectedPanel;
        this.player.Enabled = (panel is 0 or 2 or 3 or 4) && this.choices.Length > 1;
        this.cards.Visible = panel != 5;
        this.places.Visible = panel == 5;
        if (panel == 5)
        {
            this.RenderPlaces(view.Run?.Players.SelectMany(p => p.Pokemon ?? []) ?? seen);
            return;
        }
        var entries = panel switch
        {
            0 => seen.Where(p => p.Present && p.State.Container == Placement.Party).OrderBy(p => p.State.Slot).Select(Card).ToArray(),
            1 => (view.Run?.Links ?? []).SelectMany(LinkCards).ToArray(),
            2 => seen.Where(p => p.Present && p.State.Container == Placement.Box).OrderBy(p => p.State.Box).ThenBy(p => p.State.Slot).Select(Card).ToArray(),
            3 => data.Where(p => p.Dead).Select(Card).ToArray(),
            4 => (data.Length > 0 ? data : seen).Where(p => p.State.IsShiny).Select(Card).ToArray(),
            _ => [],
        };
        string empty = panel switch
        {
            0 => "Noch kein Team erfasst",
            1 => "Noch keine Links erfasst",
            2 => "Keine Pokémon in der Box erfasst",
            3 => "Keine Todesfälle erfasst",
            4 => "Keine Shinies erfasst",
            _ => "Noch nichts erfasst",
        };
        this.cards.Render(entries.Length == 0 ? [new DashboardEntry(empty, "")] : entries);
    }

    private void RenderPlaces(IEnumerable<DashboardPokemon> pokemon)
    {
        var labels = pokemon.Where(p => !p.State.IsEgg && p.State.MetArea != 255)
            .GroupBy(p => RubyNames.Area(p.State.MetArea)).OrderBy(group => group.Min(p => p.State.MetArea))
            .Select(group => $"{group.Key}  ·  {group.Count()}").ToArray();
        if (labels.Length == 0) labels = ["Noch keine Fangorte erfasst"];
        if (labels.SequenceEqual(this.lastTags)) return;
        this.lastTags = labels;
        this.places.SuspendLayout();
        foreach (Control old in this.places.Controls.Cast<Control>().ToArray()) old.Dispose();
        foreach (string text in labels)
        {
            this.places.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = DashboardTheme.Accent,
                BackColor = DashboardTheme.Panel,
                Padding = new Padding(7, 5, 7, 5),
                Margin = new Padding(3),
            });
        }
        this.places.ResumeLayout();
    }

    private static DashboardEntry Card(DashboardPokemon pokemon)
    {
        var state = pokemon.State;
        string heading = state.IsEgg ? "Ei" : RubyNames.Pokemon(state.Species);
        if (heading.Length > 0) heading = char.ToUpperInvariant(heading[0]) + heading[1..];
        if (state.IsShiny && !state.IsEgg) heading = "★ " + heading;
        string details = state.Container == Placement.Party
            ? $"Lv. {state.Level}  ·  KP {state.Hp}/{state.MaxHp}"
            : $"Box {(state.Box ?? 0) + 1}  ·  Platz {state.Slot + 1}";
        return new DashboardEntry(heading, details, pokemon.Dead ? "TOT" : "", state,
            state.IsEgg ? "" : $"Fangort · {RubyNames.Area(state.MetArea)}");
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

    internal static string EventTitle(string kind) => kind switch
    {
        "caught" or "extra-catch" => "Fang",
        "linked" => "Link vollständig",
        "evolved" => "Entwicklung",
        "death" or "death-confirmed" => "Todesfall",
        "death-undone" => "Tod zurückgenommen",
        "encounter-failed" => "Fang verpasst",
        "badge" => "Orden",
        "arena-completed" => "Arena abgeschlossen",
        "arena-reverted" => "Arena zurückgenommen",
        _ => "Run",
    };

    private sealed record PlayerChoice(Guid Id, string Name);
}

internal sealed record DashboardEntry(string Title, string Detail, string Badge = "", PokemonState? Pokemon = null, string Location = "");

internal sealed class DashboardCards : ListBox
{
    private readonly RubySprites? sprites;
    private readonly Font heading = new("Segoe UI", 10, FontStyle.Bold);
    private readonly Font small = new("Segoe UI", 8);
    private DashboardEntry[] current = [];

    public DashboardCards(RubySprites? sprites = null)
    {
        this.sprites = sprites;
        this.DrawMode = DrawMode.OwnerDrawFixed;
        this.ItemHeight = 92;
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
            if (this.sprites?.Find(appearance, entry.Pokemon!.IsShiny) is { } image)
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
        TextRenderer.DrawText(e.Graphics, entry.Title, this.heading, new Rectangle(textX, rect.Y + 5, Math.Max(1, width - (entry.Badge.Length > 0 ? 60 : 0)), 20),
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
        if (entry.Location.Length > 0)
            TextRenderer.DrawText(e.Graphics, entry.Location, this.small, new Rectangle(textX, rect.Y + 63, width, 17),
                DashboardTheme.Muted, TextFormatFlags.EndEllipsis);
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
