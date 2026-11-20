using System.Drawing.Drawing2D;
using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class RosterGrid : ScrollableControl
{
    private const int Header = 35;
    private const int RowHeight = 157;
    private const int Gap = 24;
    private readonly RubySprites sprites;
    private readonly Font heading = new("Segoe UI Semibold", 10);
    private readonly Font small = new("Segoe UI", 8.5f);
    private RosterGridData data = new([], []);
    private int cellWidth;

    public RosterGrid(RubySprites sprites)
    {
        this.sprites = sprites;
        this.DoubleBuffered = true;
        this.AutoScroll = true;
        this.TabStop = true;
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

    private void ResizeContent()
    {
        int count = this.data.Columns.Length;
        if (count == 0) return;
        this.cellWidth = Math.Max(145, (this.ClientSize.Width - 16 - (count - 1) * Gap) / count);
        this.AutoScrollMinSize = new Size((this.cellWidth + Gap) * count - Gap, Header + this.data.Rows.Length * RowHeight + 3);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var canvas = e.Graphics;
        var scroll = this.AutoScrollPosition;
        var center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        if (this.data.Rows.Length == 0)
        {
            TextRenderer.DrawText(canvas, "Keine Pokémon in dieser Ansicht", this.heading, this.ClientRectangle, DashboardTheme.Muted, center);
            return;
        }
        for (int col = 0; col < this.data.Columns.Length; col++)
        {
            int x = scroll.X + col * (this.cellWidth + Gap);
            TextRenderer.DrawText(canvas, this.data.Columns[col].Name, this.heading,
                new Rectangle(x, scroll.Y, this.cellWidth, Header), DashboardTheme.Text, center);
        }
        for (int row = 0; row < this.data.Rows.Length; row++)
        {
            int y = scroll.Y + Header + row * RowHeight;
            if (y + RowHeight < e.ClipRectangle.Top || y > e.ClipRectangle.Bottom) continue;
            var group = this.data.Rows[row];
            int first = Array.FindIndex(group.Slots, p => p is not null);
            int last = Array.FindLastIndex(group.Slots, p => p is not null);
            for (int col = 0; col < group.Slots.Length; col++)
            {
                int x = scroll.X + col * (this.cellWidth + Gap);
                var box = new Rectangle(x, y, this.cellWidth, RowHeight - 7);
                if (group.Slots[col] is { } pokemon) this.DrawPokemon(canvas, box, pokemon);
                else
                {
                    DashboardTheme.Fill(canvas, box, DashboardTheme.Surface);
                    TextRenderer.DrawText(canvas, "–", this.heading, box, DashboardTheme.Muted, center);
                }
                if (group.Linked && col >= first && col < last)
                    DrawLink(canvas, box.Right + Gap / 2f, box.Top + box.Height / 2f);
            }
        }
    }

    private void DrawPokemon(Graphics canvas, Rectangle box, DashboardPokemon pokemon)
    {
        var state = pokemon.State;
        var appearance = state.IsEgg ? null : RubyDashboardCatalog.Appearance(state.Species);
        DashboardTheme.Fill(canvas, box, DashboardTheme.Panel);
        var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
        string name = state.IsEgg ? "Ei" : (state.IsShiny ? "★ " : "") + RubyNames.Pokemon(state.Species);
        TextRenderer.DrawText(canvas, name, this.heading, new Rectangle(box.X + 3, box.Y + 3, box.Width - 6, 23),
            state.IsShiny ? DashboardTheme.Gold : DashboardTheme.Text, flags);
        var portrait = new Rectangle(box.X + (box.Width - 61) / 2, box.Y + 26, 61, 60);
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
        if (!state.IsEgg && state.MetArea != 255)
        {
            string area = RubyNames.Area(state.MetArea);
            int width = Math.Min(box.Width - 12, TextRenderer.MeasureText(area, this.small).Width + 15);
            this.DrawTag(canvas, new Rectangle(box.X + (box.Width - width) / 2, box.Y + 88, width, 19), area, DashboardTheme.Accent);
        }
        string metrics = state.Container == Placement.Party
            ? $"Lv. {state.Level} · KP {state.Hp}/{state.MaxHp}"
            : $"Box {(state.Box ?? 0) + 1} · Platz {state.Slot + 1}";
        TextRenderer.DrawText(canvas, metrics, this.small, new Rectangle(box.X + 4, box.Y + 109, box.Width - 8, 18), DashboardTheme.Muted, flags);
        if (appearance is null) return;
        var types = new[] { DashboardTheme.RubyType(state.RubyType1) ?? appearance.Type1, DashboardTheme.RubyType(state.RubyType2) ?? appearance.Type2 }.Distinct().ToArray();
        const int size = 64;
        int left = box.X + (box.Width - (size * types.Length + (types.Length - 1) * 4)) / 2;
        foreach (string type in types)
        {
            var badge = new Rectangle(left, box.Y + 128, size, 17);
            var (label, color) = DashboardTheme.Type(type);
            this.DrawTag(canvas, badge, label, color);
            if (this.sprites.TypeBadge(type) is { } icon)
            {
                DashboardTheme.Fill(canvas, badge, DashboardTheme.Surface, 7);
                canvas.InterpolationMode = InterpolationMode.NearestNeighbor;
                canvas.DrawImage(icon, new Rectangle(badge.X + (size - 42) / 2, badge.Y + 2, 42, 13));
            }
            left += size + 4;
        }
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
        if (disposing) { this.heading.Dispose(); this.small.Dispose(); }
        base.Dispose(disposing);
    }
}
