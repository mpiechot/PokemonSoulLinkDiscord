using System.Drawing.Drawing2D;
using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class BattlePanel : DashboardScrollView
{
    private const int CardHeader = 24;
    private const int CombatantHeight = 36;
    private const int FlashDuration = 425;
    private const TextFormatFlags LabelFlags = TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis;
    private readonly RubySprites sprites;
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 33 };
    private readonly Dictionary<(Guid Player, int Position), (long Ends, int Damage)> hits = [];
    private readonly Font small = new("Segoe UI", 8);
    private ClientPlayerView[] rows = [];
    private Guid? runId;

    public event Action<Guid, PokemonState>? PokemonOpened;

    public BattlePanel(RubySprites sprites)
    {
        this.sprites = sprites;
        this.AccessibleName = "Live-Kämpfe";
        this.animation.Tick += (_, _) =>
        {
            long now = Environment.TickCount64;
            foreach (var hit in this.hits.Where(hit => hit.Value.Ends <= now).ToArray()) this.hits.Remove(hit.Key);
            if (this.hits.Count == 0) this.animation.Stop();
            this.Invalidate();
        };
    }

    public int Render(ClientView view)
    {
        if (this.runId != view.Run?.Id)
        {
            this.hits.Clear();
            this.rows = [];
            this.runId = view.Run?.Id;
            this.ContentSize = Size.Empty;
        }
        ClientPlayerView[] next = view is { IsLocalRun: false, Run.Phase: RunPhase.Active }
            ? view.Run.Players.Where(player => player.Id != view.LocalPlayerId && player.Connection == "Ready" && player.Battle is not null).ToArray() : [];
        if (this.rows.Length == next.Length && this.rows.Zip(next).All(pair =>
            pair.First.Id == pair.Second.Id && pair.First.Name == pair.Second.Name &&
            pair.First.Battle!.Key == pair.Second.Battle!.Key && pair.First.Battle.Trainer == pair.Second.Battle.Trainer &&
            pair.First.Battle.Combatants.SequenceEqual(pair.Second.Battle.Combatants)))
            return this.ContentSize.Height;

        long now = Environment.TickCount64;
        foreach (var hit in this.hits.Keys.ToArray())
        {
            var battle = next.FirstOrDefault(player => player.Id == hit.Player)?.Battle;
            var prior = this.rows.FirstOrDefault(player => player.Id == hit.Player)?.Battle;
            if (battle?.Key != prior?.Key || battle?.Combatants.Any(mon => mon.Position == hit.Position &&
                prior?.Combatants.Any(old => old.Position == mon.Position && old.Personality == mon.Personality && old.Species == mon.Species) == true) != true)
                this.hits.Remove(hit);
        }
        foreach (var player in next)
        {
            var prior = this.rows.FirstOrDefault(row => row.Id == player.Id)?.Battle;
            foreach (var (position, damage) in player.Battle!.DamageSince(prior))
                this.hits[(player.Id, position)] = (now + FlashDuration, damage);
        }

        if (this.hits.Count > 0) this.animation.Start();
        this.rows = next;
        this.ContentSize = new Size(0, this.rows.Sum(row => HeightOf(row.Battle!)));
        this.Invalidate();
        return this.ContentSize.Height;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        this.sprites.Changed += this.Invalidate;
        base.OnHandleCreated(e);
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        this.sprites.Changed -= this.Invalidate;
        base.OnHandleDestroyed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.animation.Dispose();
            this.small.Dispose();
        }
        base.Dispose(disposing);
    }

    protected override void PaintViewport(PaintEventArgs e)
    {
        int top = -this.ScrollPosition.Y;
        int width = this.Viewport.Width - 4;
        if (width < 50) return;
        foreach (var player in this.rows)
        {
            var battle = player.Battle!;
            int height = HeightOf(battle);
            var bounds = new Rectangle(2, top, width, height - 4);
            top += height;
            if (!bounds.IntersectsWith(e.ClipRectangle)) continue;
            DashboardTheme.Fill(e.Graphics, bounds, DashboardTheme.Panel);
            string heading = string.IsNullOrWhiteSpace(battle.Trainer) ? player.Name : $"{player.Name} gegen {battle.Trainer}";
            TextRenderer.DrawText(e.Graphics, heading, this.Font, new Rectangle(10, bounds.Y + 2, width - 16, CardHeader - 2),
                DashboardTheme.Text, LabelFlags);
            foreach (var mon in battle.Combatants)
                this.DrawCombatant(e.Graphics, player.Id, mon, CombatantBounds(mon, bounds.Y, width));
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        this.Cursor = this.CombatantAt(e.Location) is null ? Cursors.Default : Cursors.Hand;
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && this.CombatantAt(e.Location) is { } hit)
            this.PokemonOpened?.Invoke(hit.Player, new PokemonState { Species = hit.Mon.Species, Hp = hit.Mon.Hp, MaxHp = hit.Mon.MaxHp });
    }

    private (Guid Player, BattleCombatant Mon)? CombatantAt(Point point)
    {
        if (!this.Viewport.Contains(point) || this.Viewport.Width < 54) return null;
        int top = -this.ScrollPosition.Y;
        foreach (var player in this.rows)
        {
            var battle = player.Battle!;
            foreach (var mon in battle.Combatants)
            {
                if (mon.Species > 0 && CombatantBounds(mon, top, this.Viewport.Width - 4).Contains(point)) return (player.Id, mon);
            }
            top += HeightOf(battle);
        }
        return null;
    }

    private static Rectangle CombatantBounds(BattleCombatant mon, int top, int width)
    {
        int half = (width - 18) / 2;
        int x = 10 + ((mon.Position & 1) == 0 ? 0 : half + 4);
        int y = top + CardHeader + (mon.Position / 2) * CombatantHeight;
        return new Rectangle(x, y, half, CombatantHeight - 1);
    }

    private static int HeightOf(BattleStatus battle) => CardHeader + (battle.Combatants.Max(mon => mon.Position) / 2 + 1) * CombatantHeight + 7;

    private void DrawCombatant(Graphics canvas, Guid player, BattleCombatant mon, Rectangle bounds)
    {
        bool struck = this.hits.TryGetValue((player, mon.Position), out var hit) && hit.Ends > Environment.TickCount64;
        if (struck) DashboardTheme.Fill(canvas, bounds, Color.FromArgb(102, 49, 65), 6);
        int shake = struck ? (int)(Math.Sin((hit.Ends - Environment.TickCount64) / 18.0) * 3) : 0;
        if (mon.Species == 0)
        {
            TextRenderer.DrawText(canvas, "?", this.Font, new Rectangle(bounds.X + shake, bounds.Y, 32, 32),
                DashboardTheme.Muted, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
        else if (RubyDashboardCatalog.Appearance(mon.Species) is { } appearance && this.sprites.Find(appearance, false) is { } sprite)
        {
            canvas.InterpolationMode = InterpolationMode.NearestNeighbor;
            canvas.DrawImage(sprite, new Rectangle(bounds.X + shake, bounds.Y, 32, 32));
        }
        int width = Math.Max(4, bounds.Width - 37);
        int left = bounds.X + 33;
        if (mon.Species != 0)
        {
            TextRenderer.DrawText(canvas, RubyNames.Pokemon(mon.Species), this.small, new Rectangle(left, bounds.Y, width, 14),
                (mon.Position & 1) == 0 ? DashboardTheme.Accent : DashboardTheme.Text, LabelFlags);
        }
        var bar = new Rectangle(left, bounds.Y + 15, width, 4);
        DashboardTheme.Fill(canvas, bar, DashboardTheme.Background, 2);
        if (mon.Hp > 0)
            DashboardTheme.Fill(canvas, bar with { Width = Math.Max(1, width * mon.Hp / mon.MaxHp) }, DashboardTheme.Accent, 2);
        string health = struck && mon.Species != 0 ? $"−{hit.Damage} KP" : $"KP {mon.Hp}/{mon.MaxHp}";
        TextRenderer.DrawText(canvas, health, this.small, new Rectangle(left, bounds.Y + 20, width, 14),
            struck ? DashboardTheme.Danger : DashboardTheme.Muted, LabelFlags);
    }
}
