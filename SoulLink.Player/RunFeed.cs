using System.Drawing.Drawing2D;
using SoulLink;

namespace SoulLink.Player;

internal sealed class RunFeed(DiscordIcons icons, RubySprites sprites) : ScrollableControl
{
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
        base.OnHandleCreated(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var graphics = e.Graphics;
        if (this.cards.Length == 0)
        {
            this.AutoScrollMinSize = Size.Empty;
            TextRenderer.DrawText(graphics, this.empty, this.body, Rectangle.Inflate(this.ClientRectangle, -8, -8),
                DashboardTheme.Muted, TextFormatFlags.WordBreak);
            return;
        }
        int width = Math.Max(1, this.ClientSize.Width - 19);
        if (this.measuredWidth != width)
        {
            int top = 4;
            foreach (var card in this.cards)
            {
                card.Top = top;
                card.LineHeights = card.Lines.Select(line => Measure(graphics, line, this.body, Math.Max(1, width - 20)) + 3).ToArray();
                card.Height = 48 + (card.Pokemon is null ? 0 : 53) + card.LineHeights.Sum();
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
            var bounds = new Rectangle(2, y, width, card.Height);
            DashboardTheme.Fill(graphics, bounds, card.Background, 11);
            if (icons.Get(card.Icon) is { } icon) graphics.DrawImage(icon, new Rectangle(11, y + 8, 30, 30));
            TextRenderer.DrawText(graphics, card.Title, this.heading, new Rectangle(48, y + 7, Math.Max(1, width - 52), 31),
                card.Accent, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            int lineTop = y + 40;
            for (int index = 0; index < card.Lines.Length; index++)
            {
                TextRenderer.DrawText(graphics, card.Lines[index], this.body, new Rectangle(12, lineTop, Math.Max(1, width - 20), card.LineHeights[index]),
                    DashboardTheme.Text, TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                lineTop += card.LineHeights[index];
                if (index == 0 && card.Pokemon is { } pokemon)
                {
                    if (RubyDashboardCatalog.Appearance(pokemon.Species) is { } appearance && sprites.Find(appearance, pokemon.Shiny) is { } sprite)
                    {
                        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
                        graphics.DrawImage(sprite, new Rectangle(12, lineTop, 52, 51));
                    }
                    lineTop += 53;
                }
            }
        }
    }

    private static int Measure(Graphics canvas, string text, Font font, int width) =>
        TextRenderer.MeasureText(canvas, text, font, new Size(width, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;

    private static FeedCard CreateCard(AutomationEvent e)
    {
        string time = e.At.ToLocalTime().ToString("HH:mm:ss");
        string area = e.Area is int known ? RubyNames.Area(known) : "Kein Link";
        var detail = e.Details;
        if (e.Kind is "caught" or "extra-catch" && detail?.Pokemon is { } caught)
        {
            string verdict = detail.AreaAvailable == false ? "Ungültig · Gebiet bereits vergeben"
                : detail.CatchCheck?.PreviouslyCaught is { } earlier ? $"Ungültig · Evolutionsfamilie bereits gefangen ({earlier})"
                : detail.CatchCheck?.Pokemon is null ? "Unbekannt" : "Gültig";
            return new FeedCard("Fang", "catch", Color.FromArgb(24, 58, 48), DashboardTheme.Accent,
                [RubyNames.Pokemon(caught.Species), $"Spieler · {caught.Player}", $"Catch-Check · {verdict}", $"Fangort · {area}", $"Uhrzeit · {time}"], caught);
        }
        if (e.Kind == "death" && detail?.Pokemon is { } dead)
        {
            var lines = new List<string>
            {
                $"Pokémon · {RubyNames.Pokemon(dead.Species)}", $"Spieler · {dead.Player}",
                $"{(detail.HasLink == false ? "Fangort" : "Link")} · {area}",
            };
            var affected = detail.Members ?? [];
            if (affected.Length == 0) lines.Add("Betroffen · Keine weiteren Pokémon");
            lines.AddRange(affected.Select(p => $"Betroffen · {p.Player} — {RubyNames.Pokemon(p.Species)}"));
            lines.Add($"Uhrzeit · {time}");
            if (detail.Battle?.OpponentSpecies is int foe) lines.Add($"Gegner · {RubyNames.Pokemon(foe)}");
            if (!string.IsNullOrWhiteSpace(detail.Battle?.Trainer)) lines.Add($"Trainer · {detail.Battle.Trainer}");
            if (RubyNames.Move(detail.Battle?.Move) is { } move)
                lines.Add($"{(detail.Battle?.FinishingBlow == true ? "K.-o.-Attacke" : "Letzte erfasste Attacke")} · {move}");
            lines.Add($"Grund · {(string.IsNullOrWhiteSpace(detail.Reason) ? "Keine Angabe" : detail.Reason)}");
            return new FeedCard("Todesfall", "death", Color.FromArgb(44, 49, 58), Color.FromArgb(210, 216, 225), lines.ToArray());
        }
        if (e.Kind == "linked" && detail?.Members is { } members)
        {
            return new FeedCard("Link vollständig", "team", DashboardTheme.Panel, DashboardTheme.Text,
                [area, .. members.Select(p => $"{p.Player} · {RubyNames.Pokemon(p.Species)}"), $"Uhrzeit · {time}"]);
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
        return new FeedCard(title, icon, background, DashboardTheme.Text, [e.Message, $"Uhrzeit · {time}"]);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { this.body.Dispose(); this.heading.Dispose(); }
        base.Dispose(disposing);
    }

    private sealed record FeedCard(string Title, string Icon, Color Background, Color Accent, string[] Lines, EventPokemon? Pokemon = null)
    {
        public int Top { get; set; }
        public int Height { get; set; }
        public int[] LineHeights { get; set; } = [];
    }
}
