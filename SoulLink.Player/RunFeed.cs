using System.Drawing.Drawing2D;
using SoulLink;

namespace SoulLink.Player;

internal sealed class RunFeed(DiscordIcons icons, RubySprites sprites) : ScrollableControl
{
    private const int SpriteSize = 66;
    private const int SpriteColumn = SpriteSize + 5;
    private const TextFormatFlags LineFormat = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl |
        TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsClipping;
    private readonly Font heading = new("Segoe UI Semibold", 10);
    private readonly Font body = new("Segoe UI", 9);
    private AutomationEvent[] history = [];
    private FeedCard[] cards = [];
    private int measuredWidth;
    private string empty = "";

    public void Render(AutomationEvent[] events, string message)
    {
        this.empty = message;
        if (this.history.Select(e => e.Id).SequenceEqual(events.Select(e => e.Id)))
        {
            if (events.Length == 0) this.Invalidate();
            return;
        }
        this.history = events;
        this.cards = events.Reverse().Select(CreateCard).ToArray();
        this.measuredWidth = 0;
        this.Invalidate();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        this.DoubleBuffered = true;
        this.AutoScroll = true;
        this.TabStop = true;
        this.AccessibleName = "Newsfeed";
        sprites.Changed += this.Invalidate;
        base.OnHandleCreated(e);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        sprites.Changed -= this.Invalidate;
        base.OnHandleDestroyed(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        if (this.cards.Length == 0)
        {
            this.AutoScrollMinSize = Size.Empty;
            TextRenderer.DrawText(graphics, this.empty, this.body, Rectangle.Inflate(this.ClientRectangle, -8, -8),
                DashboardTheme.Muted, LineFormat);
            return;
        }

        int scrollbar = this.VerticalScroll.Visible ? 0 : SystemInformation.VerticalScrollBarWidth;
        int width = Math.Max(1, this.ClientSize.Width - scrollbar - 4);
        if (this.measuredWidth != width)
        {
            int top = 4;
            foreach (var card in this.cards)
            {
                card.Top = top;
                int bodyWidth = BodyWidth(width, card);
                card.LineHeights = card.Lines.Select(line => this.MeasureLine(graphics, line, bodyWidth)).ToArray();
                card.Height = 51 + Math.Max(card.Pokemon is null ? 0 : SpriteSize, card.LineHeights.Sum());
                top += card.Height + 7;
            }
            this.measuredWidth = width;
            this.AutoScrollMinSize = new Size(0, top);
        }

        int offset = this.AutoScrollPosition.Y;
        foreach (var card in this.cards)
        {
            int y = card.Top + offset;
            if (y + card.Height < e.ClipRectangle.Top || y > e.ClipRectangle.Bottom) continue;
            this.DrawCard(graphics, card, new Rectangle(2, y, width, card.Height));
        }
    }

    private static int BodyWidth(int cardWidth, FeedCard card) =>
        Math.Max(1, cardWidth - 20 - (card.Pokemon is null ? 0 : SpriteColumn));

    private int MeasureLine(Graphics canvas, FeedLine line, int width)
    {
        int marker = line.Verdict is null ? 0 : this.CheckWidth(canvas, width);
        int height = TextRenderer.MeasureText(canvas, line.Text, this.body, new Size(Math.Max(1, width - marker), int.MaxValue), LineFormat).Height;
        return Math.Max(line.Verdict is null ? 0 : 18, height) + 4;
    }

    private int CheckWidth(Graphics canvas, int width) =>
        Math.Min(width - 1, TextRenderer.MeasureText(canvas, "Catch-Check:", this.body).Width + 19);

    private void DrawCard(Graphics graphics, FeedCard card, Rectangle bounds)
    {
        DashboardTheme.Fill(graphics, bounds, card.Background, 11);
        int y = bounds.Y;
        if (icons.Get(card.Icon) is { } icon) graphics.DrawImage(icon, new Rectangle(bounds.X + 9, y + 8, 30, 30));

        const TextFormatFlags headerFlags = TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix;
        int timeWidth = TextRenderer.MeasureText(graphics, card.Time, this.body).Width + 4;
        TextRenderer.DrawText(graphics, card.Time, this.body, new Rectangle(bounds.Right - timeWidth - 8, y + 7, timeWidth, 30),
            DashboardTheme.Muted, headerFlags | TextFormatFlags.Right);
        TextRenderer.DrawText(graphics, card.Title, this.heading,
            new Rectangle(bounds.X + 46, y + 7, Math.Max(1, bounds.Width - timeWidth - 55), 30), card.Accent, headerFlags);

        int lineTop = y + 42;
        int left = bounds.X + 10;
        if (card.Pokemon is { } pokemon)
        {
            if (RubyDashboardCatalog.Appearance(pokemon.Species) is { } appearance && sprites.Find(appearance, pokemon.Shiny) is { } sprite)
            {
                graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                graphics.DrawImage(sprite, new Rectangle(left, lineTop, SpriteSize, SpriteSize));
            }
            left += SpriteColumn;
        }

        int width = BodyWidth(bounds.Width, card);
        for (int index = 0; index < card.Lines.Length; index++)
        {
            var line = card.Lines[index];
            int markerWidth = 0;
            if (line.Verdict is { } verdict)
            {
                markerWidth = this.CheckWidth(graphics, width);
                this.DrawCheck(graphics, verdict, left, lineTop, markerWidth);
            }
            TextRenderer.DrawText(graphics, line.Text, this.body,
                new Rectangle(left + markerWidth, lineTop, width - markerWidth, card.LineHeights[index]),
                DashboardTheme.Text, LineFormat);
            lineTop += card.LineHeights[index];
        }
    }

    private void DrawCheck(Graphics graphics, CatchVerdict verdict, int x, int y, int width)
    {
        TextRenderer.DrawText(graphics, "Catch-Check:", this.body, new Rectangle(x, y, Math.Max(1, width - 16), 20),
            DashboardTheme.Text, TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        var bounds = new Rectangle(x + width - 17, y, 16, 18);
        var (symbol, color) = verdict switch
        {
            CatchVerdict.Valid => ("✓", DashboardTheme.Accent),
            CatchVerdict.Invalid => ("✕", DashboardTheme.Danger),
            _ => ("?", DashboardTheme.Muted),
        };
        TextRenderer.DrawText(graphics, symbol, this.heading, bounds, color, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
    }

    private static FeedCard CreateCard(AutomationEvent e)
    {
        string time = e.At.ToLocalTime().ToString("HH:mm");
        string area = e.Area is int known ? RubyNames.Area(known) : "Unbekannt";
        var detail = e.Details;
        if (e.Kind is "caught" or "extra-catch" && detail?.Pokemon is { } caught)
        {
            var (verdict, explanation) = detail.AreaAvailable == false ? (CatchVerdict.Invalid, "(Fangort bereits vergeben)")
                : detail.CatchCheck?.PreviouslyCaught is { } earlier ? (CatchVerdict.Invalid, $"(Evolutionsfamilie bereits gefangen: {earlier})")
                : detail.CatchCheck?.Pokemon is null ? (CatchVerdict.Unknown, "") : (CatchVerdict.Valid, "");
            return new FeedCard("Fang", "catch", Color.FromArgb(24, 58, 48), DashboardTheme.Accent, time,
                [new($"Pokémon: {RubyNames.Pokemon(caught.Species)}"), new($"Spieler: {caught.Player}"),
                    new(explanation, verdict), new($"Fangort: {area}")], caught);
        }
        if (e.Kind == "death" && detail?.Pokemon is { } dead)
        {
            var lines = new List<FeedLine>
            {
                new($"Pokémon: {RubyNames.Pokemon(dead.Species)}"), new($"Spieler: {dead.Player}"), new($"Fangort: {area}"),
            };
            var affected = detail.Members ?? [];
            if (affected.Length == 0) lines.Add(new("Betroffen: Keine weiteren Pokémon"));
            lines.AddRange(affected.Select(p => new FeedLine($"Betroffen: {p.Player} ({RubyNames.Pokemon(p.Species)})")));
            string opponent = detail.Battle?.OpponentSpecies is int foe ? RubyNames.Pokemon(foe) : "Unbekannt";
            lines.Add(new($"Gegner: {opponent}"));
            if (!string.IsNullOrWhiteSpace(detail.Battle?.Trainer)) lines.Add(new($"Trainer: {detail.Battle.Trainer}"));
            var (label, move) = RunDialogs.BattleMove(detail.Battle);
            lines.Add(new($"{label}: {move}"));
            lines.Add(new($"Grund: {(string.IsNullOrWhiteSpace(detail.Reason) ? "Keine Angabe" : detail.Reason)}"));
            return new FeedCard("Todesfall", "death", Color.FromArgb(44, 49, 58), Color.FromArgb(210, 216, 225), time, lines.ToArray(), dead);
        }
        if (e.Kind == "linked" && detail?.Members is { } members)
        {
            return new FeedCard("Link vollständig", "team", DashboardTheme.Panel, DashboardTheme.Text, time,
                [new($"Link: {area}"), .. members.Select(p => new FeedLine($"{p.Player}: {RubyNames.Pokemon(p.Species)}"))]);
        }
        var (title, icon) = e.Kind switch
        {
            "caught" or "extra-catch" => ("Fang", "catch"),
            "linked" => ("Link vollständig", "team"),
            "death" or "death-confirmed" or "death-undone" => ("Todesfall", "death"),
            "badge" or "arena-completed" or "arena-reverted" => ("Arena", "arena"),
            "evolved" => ("Entwicklung", "use"),
            _ => ("Run", "status"),
        };
        var background = icon == "catch" ? Color.FromArgb(24, 58, 48) :
            icon == "death" ? Color.FromArgb(44, 49, 58) : DashboardTheme.Panel;
        return new FeedCard(title, icon, background, DashboardTheme.Text, time, [new(e.Message.Replace(" · ", ": ", StringComparison.Ordinal))]);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { this.body.Dispose(); this.heading.Dispose(); }
        base.Dispose(disposing);
    }

    private enum CatchVerdict { Unknown, Valid, Invalid }

    private sealed record FeedLine(string Text, CatchVerdict? Verdict = null);

    private sealed record FeedCard(string Title, string Icon, Color Background, Color Accent, string Time, FeedLine[] Lines, EventPokemon? Pokemon = null)
    {
        public int Top { get; set; }
        public int Height { get; set; }
        public int[] LineHeights { get; set; } = [];
    }
}
