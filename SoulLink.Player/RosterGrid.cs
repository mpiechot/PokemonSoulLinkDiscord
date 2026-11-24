using System.Drawing.Drawing2D;
using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class RosterGrid : DashboardScrollView
{
    private const int Header = 35;
    private const int RowHeight = 122;
    private const int Gap = 18;
    private const int RouteWidth = 136;
    private readonly RubySprites sprites;
    private readonly Image? placeholder;
    private readonly Font heading = new("Segoe UI Semibold", 10);
    private readonly Font small = new("Segoe UI", 8.5f);
    private readonly ToolTip cellHint = new();
    private readonly List<(Rectangle Bounds, string Label)> hints = [];
    private RosterGridData data = new([], []);
    private int cellWidth;
    private string? hoveredLabel;

    public event Action<Guid, PokemonState>? PokemonOpened;

    public RosterGrid(RubySprites sprites, Image? placeholder)
    {
        this.sprites = sprites;
        this.placeholder = placeholder;
        this.sprites.Changed += this.Invalidate;
        this.BackColor = DashboardTheme.Background;
        this.AccessibleName = "Pokémon-Grid";
    }

    public void Render(ClientView view, RosterView filter)
    {
        this.data = RosterGridData.From(view, filter);
        this.ResizeContent();
        this.Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        this.ResizeContent();
    }

    private int RouteOffset => this.data.ShowRouteColumn ? RouteWidth + Gap : 0;

    private void ResizeContent()
    {
        int count = this.data.Columns.Length;
        if (count == 0 || this.data.Rows.Length == 0)
        {
            this.ContentSize = Size.Empty;
            return;
        }
        int height = Header + this.data.Rows.Length * RowHeight + 3;
        int minimumWidth = this.RouteOffset + count * 152 + (count - 1) * Gap;
        int availableHeight = this.ClientSize.Height - (minimumWidth > this.ClientSize.Width ? this.RailSize : 0);
        int scrollbar = height > availableHeight ? this.RailSize : 0;
        this.cellWidth = Math.Max(152, (this.ClientSize.Width - this.RouteOffset - scrollbar - (count - 1) * Gap) / count);
        this.ContentSize = new Size(this.RouteOffset + (this.cellWidth + Gap) * count - Gap, height);
    }

    protected override void PaintViewport(PaintEventArgs e)
    {
        var canvas = e.Graphics;
        var scroll = new Point(-this.ScrollPosition.X, -this.ScrollPosition.Y);
        this.hints.Clear();
        var center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis |
            TextFormatFlags.PreserveGraphicsClipping;
        if (this.data.Rows.Length == 0)
        {
            TextRenderer.DrawText(canvas, "Keine Pokémon in dieser Ansicht", this.heading, this.Viewport, DashboardTheme.Muted, center);
            return;
        }
        if (this.data.ShowRouteColumn)
            TextRenderer.DrawText(canvas, "Route", this.heading, new Rectangle(scroll.X, scroll.Y, RouteWidth, Header), DashboardTheme.Text, center);
        for (int col = 0; col < this.data.Columns.Length; col++)
        {
            int x = scroll.X + this.RouteOffset + col * (this.cellWidth + Gap);
            TextRenderer.DrawText(canvas, this.data.Columns[col].Name, this.heading,
                new Rectangle(x, scroll.Y, this.cellWidth, Header), DashboardTheme.Text, center);
        }
        for (int row = 0; row < this.data.Rows.Length; row++)
        {
            int y = scroll.Y + Header + row * RowHeight;
            if (y + RowHeight < this.Viewport.Top || y > this.Viewport.Bottom) continue;
            var group = this.data.Rows[row];
            if (this.data.ShowRouteColumn)
                this.DrawRoute(canvas, new Rectangle(scroll.X, y, RouteWidth, RowHeight - 7), group);
            int first = Array.FindIndex(group.Slots, p => p is not null);
            int last = Array.FindLastIndex(group.Slots, p => p is not null);
            for (int col = 0; col < group.Slots.Length; col++)
            {
                int x = scroll.X + this.RouteOffset + col * (this.cellWidth + Gap);
                var box = new Rectangle(x, y, this.cellWidth, RowHeight - 7);
                if (group.Slots[col] is { } pokemon) this.DrawPokemon(canvas, box, pokemon, group.LinkName);
                else
                {
                    DashboardTheme.Fill(canvas, box, DashboardTheme.Surface);
                    if (this.placeholder is { } unknown && group.MissingMembers?[col] == true)
                    {
                        var icon = new Rectangle(box.X + (box.Width - 52) / 2, box.Y + (box.Height - 52) / 2, 52, 52);
                        DashboardTheme.DrawImage(canvas, unknown, icon, pixelArt: true);
                        this.hints.Add((icon, "Noch kein Pokémon für diesen Link"));
                    }
                }
                if (group.Linked && col >= first && col < last)
                    DrawLink(canvas, box.Right + Gap / 2f, box.Top + box.Height / 2f);
            }
        }
    }

    private void DrawRoute(Graphics canvas, Rectangle box, RosterRow row)
    {
        DashboardTheme.Fill(canvas, box, DashboardTheme.Surface);
        var mon = row.Slots.FirstOrDefault(slot => slot is not null);
        string route = row.LinkName ?? (mon is null || mon.State.MetArea == 255 ? "Unbekannt" : RubyNames.Area(mon.State.MetArea));
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix;
        var title = Rectangle.Inflate(box, -8, -12);
        if (row.Failed)
        {
            title.Height -= 20;
            this.DrawTag(canvas, new Rectangle(box.X + 8, box.Bottom - 33, box.Width - 16, 22), "Erstfang verpasst", DashboardTheme.Danger);
        }
        TextRenderer.DrawText(canvas, route, this.heading, title, row.Failed ? DashboardTheme.Danger : DashboardTheme.Text, flags);
    }

    private void DrawPokemon(Graphics canvas, Rectangle box, DashboardPokemon pokemon, string? linkName)
    {
        var state = pokemon.State;
        var appearance = state.IsEgg ? null : RubyDashboardCatalog.Appearance(state.Species);
        DashboardTheme.Fill(canvas, box, DashboardTheme.Panel);
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        int width = Math.Min(box.Width - 10, 270);
        int left = box.X + (box.Width - width) / 2;
        int rightWidth = Math.Clamp(width / 3, 64, 90);
        int leftWidth = width - rightWidth - 7;
        int right = left + leftWidth + 7;
        string name = (state.IsShiny && !state.IsEgg ? "★ " : "") + RubyNames.PokemonWithNickname(state);
        var nameBounds = new Rectangle(left, box.Y + 3, width, 21);
        TextRenderer.DrawText(canvas, name, this.heading, nameBounds, state.IsShiny ? DashboardTheme.Gold : DashboardTheme.Text,
            flags | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
        if (TextRenderer.MeasureText(name, this.heading).Width > nameBounds.Width) this.hints.Add((nameBounds, name));
        var portrait = new Rectangle(left + (leftWidth - 58) / 2, box.Y + 25, 58, 58);
        if (appearance is not null && this.sprites.Find(appearance, state.IsShiny) is { } sprite)
        {
            canvas.InterpolationMode = InterpolationMode.NearestNeighbor;
            canvas.DrawImage(sprite, portrait);
        }
        else
        {
            TextRenderer.DrawText(canvas, appearance is null ? "–" : $"#{appearance.NationalDex}", this.heading,
                portrait, DashboardTheme.Muted, flags);
        }
        string? metArea = state.MetArea == 255 ? null : RubyNames.Area(state.MetArea);
        if (!this.data.ShowRouteColumn && !state.IsEgg && (linkName ?? metArea) is { } tag)
        {
            int tagWidth = Math.Min(leftWidth, TextRenderer.MeasureText(tag, this.small).Width + 15);
            var tagBounds = new Rectangle(left + (leftWidth - tagWidth) / 2, box.Y + 87, tagWidth, 19);
            this.DrawTag(canvas, tagBounds, tag, DashboardTheme.Accent);
            if (metArea is not null && metArea != tag) this.hints.Add((tagBounds, $"Ursprünglicher Fangort: {metArea}"));
        }
        if (this.data.ShowRouteColumn && metArea is not null && linkName is not null && metArea != linkName)
            this.hints.Add((portrait, $"Ursprünglicher Fangort: {metArea}"));
        var details = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        bool inTeam = state.Container == Placement.Party;
        TextRenderer.DrawText(canvas, inTeam ? $"Level: {state.Level}" : $"Box: {(state.Box ?? 0) + 1}", this.small,
            new Rectangle(right, box.Y + 25, rightWidth, 19), DashboardTheme.Text, details);
        TextRenderer.DrawText(canvas, inTeam ? $"KP: {state.Hp}/{state.MaxHp}" : $"Platz: {state.Slot + 1}", this.small,
            new Rectangle(right, box.Y + 44, rightWidth, 19), DashboardTheme.Muted, details);
        if (appearance is null) return;
        var types = new[] { DashboardTheme.RubyType(state.RubyType1) ?? appearance.Type1, DashboardTheme.RubyType(state.RubyType2) ?? appearance.Type2 }.Distinct().ToArray();
        int iconLeft = right + 2;
        const int size = 26;
        foreach (string type in types)
        {
            var (icon, label) = this.sprites.TypeIcon(type);
            if (icon is not null)
            {
                var slot = new Rectangle(iconLeft, box.Y + 68, size, size);
                DashboardTheme.DrawImage(canvas, icon, slot);
                this.hints.Add((slot, label));
            }
            iconLeft += size + 5;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        this.Cursor = this.CardAt(e.Location)?.Pokemon.State.IsEgg == false ? Cursors.Hand : Cursors.Default;
        string? label = this.hints.Find(hint => hint.Bounds.Contains(e.Location)).Label;
        if (this.hoveredLabel == label) return;
        this.hoveredLabel = label;
        this.cellHint.Hide(this);
        this.cellHint.SetToolTip(this, label);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && this.CardAt(e.Location) is { Pokemon.State.IsEgg: false } card)
            this.PokemonOpened?.Invoke(card.Player, card.Pokemon.State);
    }

    private (Guid Player, DashboardPokemon Pokemon)? CardAt(Point point)
    {
        if (!this.Viewport.Contains(point) || this.cellWidth == 0) return null;
        int x = point.X + this.ScrollPosition.X - this.RouteOffset;
        int y = point.Y + this.ScrollPosition.Y - Header;
        if (x < 0 || y < 0 || y % RowHeight >= RowHeight - 7 || x % (this.cellWidth + Gap) >= this.cellWidth) return null;
        int row = y / RowHeight;
        int column = x / (this.cellWidth + Gap);
        if (row >= this.data.Rows.Length || column >= this.data.Columns.Length ||
            this.data.Rows[row].Slots[column] is not { } pokemon) return null;
        return (this.data.Columns[column].Id, pokemon);
    }

    private void DrawTag(Graphics canvas, Rectangle bounds, string text, Color color)
    {
        DashboardTheme.Fill(canvas, bounds, DashboardTheme.Surface, 8);
        TextRenderer.DrawText(canvas, text, this.small, bounds, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static void DrawLink(Graphics canvas, float x, float y)
    {
        using var shape = DashboardTheme.Rounded(new Rectangle(-9, -3, 12, 7), 3);
        using var second = DashboardTheme.Rounded(new Rectangle(-2, -3, 12, 7), 3);
        shape.AddPath(second, false);
        using var transform = new Matrix();
        transform.Rotate(-35);
        transform.Translate(x, y, MatrixOrder.Append);
        shape.Transform(transform);
        using var pen = new Pen(DashboardTheme.Accent, 1.65f);
        canvas.DrawPath(pen, shape);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.sprites.Changed -= this.Invalidate;
            this.cellHint.Dispose();
            this.heading.Dispose();
            this.small.Dispose();
        }
        base.Dispose(disposing);
    }
}
