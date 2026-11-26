using SoulLink;

namespace SoulLink.Player;

internal sealed class PokemonDetailsForm : ToolWindow
{
    private readonly DetailLines content = new() { Dock = DockStyle.Fill };
    private readonly Button wiki = Action("Im PokéWiki");
    private CancellationTokenSource? loading;
    private int species;

    public PokemonDetailsForm() : base("Pokémon", new Size(450, 575))
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(13) };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(this.content, 0, 0);
        var close = DashboardTheme.Button("Schließen");
        close.DialogResult = DialogResult.Cancel;
        close.Click += (_, _) => this.Close();
        this.wiki.Enabled = false;
        this.wiki.Click += async (_, _) =>
        {
            if (this.WikiRequested is { } open) await open(this.species);
        };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.AddRange([close, this.wiki]);
        layout.Controls.Add(actions, 0, 1);
        this.CancelButton = close;
        this.Body.Controls.Add(layout);
    }

    public event Func<int, Task>? WikiRequested;

    public async Task DisplayAsync(PokemonState pokemon, string? romPath, Func<CancellationToken, Task<PokemonRomDetails?>>? partnerLookup)
    {
        this.CancelLookup();
        this.species = pokemon.Species;
        this.wiki.Enabled = !pokemon.IsEgg && SpeciesCatalog.IsValid(pokemon.Species) && this.WikiRequested is not null;
        this.SetTitle(RubyNames.PokemonWithNickname(pokemon));
        this.content.ScrollToTop();
        if (partnerLookup is null)
        {
            var local = ReadLocal(pokemon.Species, romPath);
            this.content.SetLines(BuildLines(pokemon, local.Details, local.Error));
            return;
        }

        this.content.SetLines(BuildLines(pokemon, null, "Pokémon-Daten des Mitspielers werden geladen …"));
        using var request = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        this.loading = request;
        try
        {
            PokemonRomDetails? partner = null;
            try { partner = await partnerLookup(request.Token); }
            catch (OperationCanceledException) { }
            if (this.loading != request || this.IsDisposed) return;
            if (partner is not null)
            {
                this.content.SetLines(BuildLines(pokemon, partner, null));
                return;
            }

            var local = ReadLocal(pokemon.Species, romPath);
            string? reference = local.Details is not null ? "Ersatzdaten aus deiner Rubin-ROM." : local.Error;
            this.content.SetLines(BuildLines(pokemon, local.Details, reference));
        }
        finally { if (this.loading == request) this.loading = null; }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) this.CancelLookup();
        base.Dispose(disposing);
    }

    private void CancelLookup()
    {
        var request = this.loading;
        this.loading = null;
        request?.Cancel();
    }

    private static (PokemonRomDetails? Details, string? Error) ReadLocal(int species, string? romPath)
    {
        try
        {
            if (romPath is null) throw new InvalidDataException("Wähle eine ROM, um Attacken und Entwicklungen zu sehen.");
            using var rom = File.OpenRead(romPath);
            return (GameProfiles.ReadPokemonDetails(rom, species), null);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return (null, exception is InvalidDataException ? exception.Message : "Die gewählte ROM konnte nicht gelesen werden.");
        }
    }

    private static List<(string? Label, string Text)> BuildLines(PokemonState pokemon, PokemonRomDetails? details, string? note)
    {
        var lines = new List<(string? Label, string Text)>();
        lines.Add((null, details is null ? "Werte" : "Werte · aktuell / Basis"));
        if (pokemon.Stats is not null || pokemon.MaxHp.HasValue || details is not null)
        {
            var stats = pokemon.Stats;
            var bases = details?.BaseStats;
            lines.Add(("KP", FormatStat(pokemon.Hp, stats?.Hp ?? pokemon.MaxHp, bases?.Hp)));
            lines.Add(("Angriff", FormatStat(null, stats?.Attack, bases?.Attack)));
            lines.Add(("Verteidigung", FormatStat(null, stats?.Defense, bases?.Defense)));
            lines.Add(("Sp.-Angriff", FormatStat(null, stats?.SpecialAttack, bases?.SpecialAttack)));
            lines.Add(("Sp.-Verteidigung", FormatStat(null, stats?.SpecialDefense, bases?.SpecialDefense)));
            lines.Add(("Initiative", FormatStat(null, stats?.Speed, bases?.Speed)));
        }
        if (note is not null) lines.Add(("", note));
        if (details is not null)
        {
            lines.Add((null, "Entwicklungen"));
            foreach (var evolution in details.Evolutions)
                lines.Add((RubyNames.Pokemon(evolution.Species), evolution.Requirement));
            if (details.Evolutions.Length == 0) lines.Add(("", "Keine weitere Entwicklung."));
            lines.Add((null, "Attacken per Levelaufstieg"));
            foreach (var move in details.Moves.OrderBy(move => move.Level))
                lines.Add((move.Level > 1 ? $"Level {move.Level}" : "Start", RubyNames.Move(move.Move) ?? ""));
            if (details.Moves.Length == 0) lines.Add(("", "Keine Attacken per Levelaufstieg."));
        }
        return lines;
    }

    private static string FormatStat(int? hp, int? current, int? baseline)
    {
        string observed = hp is int remaining && current is int maximum ? $"{remaining}/{maximum}" : current?.ToString() ?? "–";
        return baseline is int value ? $"{observed}  /  {value}" : observed;
    }

    private sealed class DetailLines : DashboardScrollView
    {
        private (string? Label, string Text)[] lines = [];
        private readonly Font heading = new("Segoe UI Semibold", 10);

        public void SetLines(List<(string? Label, string Text)> lines)
        {
            this.lines = lines.ToArray();
            this.ContentSize = new Size(0, this.lines.Sum(line => line.Label is null ? 32 : 27));
            this.AccessibleName = string.Join(Environment.NewLine, this.lines.Select(line =>
                string.IsNullOrEmpty(line.Label) ? line.Text : $"{line.Label}: {line.Text}"));
            this.Invalidate();
        }

        protected override void PaintViewport(PaintEventArgs e)
        {
            int top = -this.ScrollPosition.Y;
            int width = this.Viewport.Width;
            const TextFormatFlags flags = TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;
            foreach (var (label, text) in this.lines)
            {
                int height = label is null ? 32 : 27;
                if (top + height > 0 && top < this.Viewport.Height)
                {
                    if (string.IsNullOrEmpty(label))
                    {
                        if (label is null)
                            DashboardTheme.Fill(e.Graphics, new Rectangle(2, top + 2, width - 5, height - 3), DashboardTheme.Panel, 6);
                        TextRenderer.DrawText(e.Graphics, text, label is null ? this.heading : this.Font, new Rectangle(11, top, width - 18, height),
                            label is null ? DashboardTheme.Accent : DashboardTheme.Muted, flags);
                    }
                    else
                    {
                        const int labelWidth = 112;
                        TextRenderer.DrawText(e.Graphics, label, this.Font, new Rectangle(11, top, labelWidth, height), DashboardTheme.Muted, flags);
                        TextRenderer.DrawText(e.Graphics, text, this.Font, new Rectangle(labelWidth + 15, top, width - labelWidth - 20, height),
                            DashboardTheme.Text, flags);
                    }
                }
                top += height;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) this.heading.Dispose();
            base.Dispose(disposing);
        }
    }
}
