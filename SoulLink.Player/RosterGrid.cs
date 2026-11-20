using System.Drawing.Drawing2D;
using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class RosterGrid : ScrollableControl
{
    private const int Header = 35;
    private const int RowHeight = 122;
    private const int Gap = 18;
    private readonly RubySprites sprites;
    private readonly Font heading = new("Segoe UI Semibold", 10);
    private readonly Font small = new("Segoe UI", 8.5f);
    private readonly ToolTip typeHint = new();
    private readonly List<(Rectangle Bounds, string Label)> typeIcons = [];
    private RosterGridData data = new([], []);
    private int cellWidth;
    private string? hoveredType;

    public RosterGrid(RubySprites sprites)
    {
        this.sprites = sprites;
        this.sprites.Changed += this.Invalidate;
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
        if (count == 0 || this.data.Rows.Length == 0)
        {
            this.AutoScrollMinSize = Size.Empty;
            return;
        }
        int scrollbar = this.VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth;
        this.cellWidth = Math.Max(152, (this.ClientSize.Width - scrollbar - (count - 1) * Gap) / count);
        this.AutoScrollMinSize = new Size((this.cellWidth + Gap) * count - Gap, Header + this.data.Rows.Length * RowHeight + 3);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var canvas = e.Graphics;
        var scroll = this.AutoScrollPosition;
        this.typeIcons.Clear();
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
            if (y + RowHeight < this.ClientRectangle.Top || y > this.ClientRectangle.Bottom) continue;
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
        int width = Math.Min(box.Width - 10, 270);
        int left = box.X + (box.Width - width) / 2;
        int rightWidth = Math.Clamp(width / 3, 64, 90);
        int leftWidth = width - rightWidth - 7;
        int right = left + leftWidth + 7;
        string name = state.IsEgg ? "Ei" : (state.IsShiny ? "★ " : "") + RubyNames.Pokemon(state.Species);
        TextRenderer.DrawText(canvas, name, this.heading, new Rectangle(left, box.Y + 4, leftWidth, 21),
            state.IsShiny ? DashboardTheme.Gold : DashboardTheme.Text, flags);
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
        if (!state.IsEgg && state.MetArea != 255)
        {
            string area = RubyNames.Area(state.MetArea);
            int tagWidth = Math.Min(leftWidth, TextRenderer.MeasureText(area, this.small).Width + 15);
            this.DrawTag(canvas, new Rectangle(left + (leftWidth - tagWidth) / 2, box.Y + 87, tagWidth, 19), area, DashboardTheme.Accent);
        }
        var details = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis;
        bool inTeam = state.Container == Placement.Party;
        TextRenderer.DrawText(canvas, inTeam ? $"Level: {state.Level}" : $"Box: {(state.Box ?? 0) + 1}", this.small,
            new Rectangle(right, box.Y + 12, rightWidth, 19), DashboardTheme.Text, details);
        TextRenderer.DrawText(canvas, inTeam ? $"KP: {state.Hp}/{state.MaxHp}" : $"Platz: {state.Slot + 1}", this.small,
            new Rectangle(right, box.Y + 31, rightWidth, 19), DashboardTheme.Muted, details);
        if (appearance is null) return;
        var types = new[] { DashboardTheme.RubyType(state.RubyType1) ?? appearance.Type1, DashboardTheme.RubyType(state.RubyType2) ?? appearance.Type2 }.Distinct().ToArray();
        int iconLeft = right + 2;
        const int size = 26;
        foreach (string type in types)
        {
            var (icon, label) = this.sprites.TypeIcon(type);
            if (icon is not null)
            {
                var slot = new Rectangle(iconLeft, box.Y + 56, size, size);
                double scale = Math.Min((double)size / icon.Width, (double)size / icon.Height);
                int badgeWidth = (int)Math.Round(icon.Width * scale);
                int badgeHeight = (int)Math.Round(icon.Height * scale);
                canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
                canvas.DrawImage(icon, new Rectangle(slot.X + (size - badgeWidth) / 2, slot.Y + (size - badgeHeight) / 2, badgeWidth, badgeHeight));
                this.typeIcons.Add((slot, label));
            }
            iconLeft += size + 5;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        string? label = this.typeIcons.Find(icon => icon.Bounds.Contains(e.Location)).Label;
        if (this.hoveredType == label) return;
        this.hoveredType = label;
        this.typeHint.Hide(this);
        this.typeHint.SetToolTip(this, label);
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
            this.typeHint.Dispose();
            this.heading.Dispose();
            this.small.Dispose();
        }
        base.Dispose(disposing);
    }
}
