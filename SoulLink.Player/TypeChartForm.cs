namespace SoulLink.Player;

internal sealed class TypeChartForm : ToolWindow
{
    private const int ChipWidth = 96;
    private const int ChipHeight = 28;
    private const int ChipGap = 5;

    // Pokémon Ruby type matchups, before the Steel changes in Generation VI.
    private static readonly TypeMatchup[] Matchups =
    [
        new("Normal", [], [5, 8], [7]),
        new("Kampf", [0, 5, 8, 15, 17], [2, 3, 6, 14], [7]),
        new("Flug", [1, 6, 12], [5, 8, 13], []),
        new("Gift", [12], [3, 4, 5, 7], [8]),
        new("Boden", [3, 5, 8, 10, 13], [6, 12], [2]),
        new("Gestein", [2, 6, 10, 15], [1, 4, 8], []),
        new("Käfer", [12, 14, 17], [1, 2, 3, 7, 8, 10], []),
        new("Geist", [7, 14], [8, 17], [0]),
        new("Stahl", [5, 15], [8, 10, 11, 13], []),
        new("Feuer", [6, 8, 12, 15], [5, 10, 11, 16], []),
        new("Wasser", [4, 5, 10], [11, 12, 16], []),
        new("Pflanze", [4, 5, 11], [2, 3, 6, 8, 10, 12, 16], []),
        new("Elektro", [2, 11], [12, 13, 16], [4]),
        new("Psycho", [1, 3], [8, 14], [17]),
        new("Eis", [2, 4, 12, 16], [8, 10, 11, 15], []),
        new("Drache", [16], [8], []),
        new("Unlicht", [7, 14], [1, 8, 17], []),
    ];

    private readonly RubySprites sprites;
    private readonly Panel document = new() { BackColor = DashboardTheme.Background };
    private readonly ToolCard choices = new();
    private readonly FlowLayoutPanel selection = new() { Dock = DockStyle.Fill, AccessibleName = "Pokémon-Typen" };
    private readonly Label selectionHeading = SectionHeading("Typ auswählen");
    private readonly TableLayoutPanel results = new() { ColumnCount = 2, RowCount = 4 };
    private readonly DashboardScrollHost scroll;
    private readonly Button[] selectors;
    private readonly FlowLayoutPanel[] attack;
    private readonly FlowLayoutPanel[] defense;

    public TypeChartForm(RubySprites sprites) : base("Typen · Pokémon Rubin", new Size(780, 656))
    {
        this.sprites = sprites;
        this.selectors = Matchups.Select((type, index) =>
        {
            var button = Action(type.Name);
            button.AutoSize = false;
            button.Tag = index;
            button.Click += (_, _) => this.SelectType(index);
            this.selection.Controls.Add(button);
            return button;
        }).ToArray();

        this.choices.Controls.Add(this.selection);
        this.choices.Controls.Add(this.selectionHeading);
        this.results.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        this.results.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int i = 0; i < this.results.RowCount; i++) this.results.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        this.attack = this.AddMatchupColumn(0, "Angriff", ["2×  Sehr effektiv gegen", "½×  Wenig effektiv gegen", "0×  Wirkungslos gegen"],
            [DashboardTheme.Accent, DashboardTheme.Gold, DashboardTheme.Muted]);
        this.defense = this.AddMatchupColumn(1, "Verteidigung", ["2×  Schwach gegen", "½×  Resistent gegen", "0×  Immun gegen"],
            [DashboardTheme.Danger, DashboardTheme.Accent, DashboardTheme.Muted]);
        this.document.Controls.AddRange([this.choices, this.results]);
        this.scroll = new DashboardScrollHost(this.document, this.Measure) { Dock = DockStyle.Fill, AccessibleName = "Typenübersicht" };
        this.Body.Controls.Add(this.scroll);
        this.Shown += (_, _) =>
        {
            this.FitWorkingArea();
            this.scroll.ResizeDocument();
        };
        this.sprites.Changed += this.RefreshIcons;
        this.SelectType(0);
    }

    private void SelectType(int index)
    {
        for (int i = 0; i < this.selectors.Length; i++)
        {
            this.selectors[i].BackColor = i == index ? DashboardTheme.Border : DashboardTheme.Panel;
            this.selectors[i].ForeColor = i == index ? DashboardTheme.Accent : DashboardTheme.Text;
            this.selectors[i].AccessibleDescription = i == index ? "Ausgewählt" : null;
        }

        var selected = Matchups[index];
        SetTypes(this.attack[0], selected.Strong.Select(Index));
        SetTypes(this.attack[1], selected.Weak.Select(Index));
        SetTypes(this.attack[2], selected.Immune.Select(Index));
        SetTypes(this.defense[0], Incoming(index, type => type.Strong));
        SetTypes(this.defense[1], Incoming(index, type => type.Weak));
        SetTypes(this.defense[2], Incoming(index, type => type.Immune));
        this.RefreshIcons();
        this.scroll.ResizeDocument();
    }

    private FlowLayoutPanel[] AddMatchupColumn(int column, string title, string[] headings, Color[] colors)
    {
        var titleLabel = SectionHeading(title);
        titleLabel.Dock = DockStyle.Fill;
        titleLabel.Margin = new Padding(column == 0 ? 4 : 10, 0, 0, 0);
        this.results.Controls.Add(titleLabel, column, 0);
        var rows = new FlowLayoutPanel[headings.Length];
        for (int i = 0; i < headings.Length; i++)
        {
            var card = new ToolCard
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(12),
                Margin = new Padding(column == 0 ? 0 : 6, 0, column == 0 ? 6 : 0, 8),
            };
            var heading = SectionHeading(headings[i]);
            heading.ForeColor = colors[i];
            rows[i] = new FlowLayoutPanel { Dock = DockStyle.Fill, AccessibleName = headings[i] };
            card.Controls.Add(rows[i]);
            card.Controls.Add(heading);
            this.results.Controls.Add(card, column, i + 1);
        }

        return rows;
    }

    private int Measure(int width)
    {
        int innerWidth = Math.Max(1, width - this.choices.Padding.Horizontal);
        int gap = this.LogicalToDeviceUnits(6);
        int columns = Math.Clamp(innerWidth / this.LogicalToDeviceUnits(108), 1, 6);
        int buttonHeight = this.LogicalToDeviceUnits(35);
        int buttonWidth = Math.Max(1, (innerWidth / columns) - gap);
        foreach (var button in this.selectors)
        {
            button.Size = new Size(buttonWidth, buttonHeight);
            button.Margin = new Padding(0, 0, gap, gap);
        }

        int selectorRows = (this.selectors.Length + columns - 1) / columns;
        int choicesHeight = this.choices.Padding.Vertical + this.selectionHeading.Height + (selectorRows * (buttonHeight + gap));
        this.choices.SetBounds(0, 0, width, choicesHeight);

        int titleHeight = this.LogicalToDeviceUnits(35);
        this.results.RowStyles[0].Height = titleHeight;
        int matchupsHeight = titleHeight;
        for (int i = 0; i < this.attack.Length; i++)
        {
            int height = Math.Max(this.SizeChips(this.attack[i], width / 2), this.SizeChips(this.defense[i], width / 2));
            this.results.RowStyles[i + 1].Height = height;
            matchupsHeight += height;
        }

        this.results.SetBounds(0, choicesHeight + this.LogicalToDeviceUnits(12), width, matchupsHeight);
        return this.results.Bottom;
    }

    private int SizeChips(FlowLayoutPanel row, int columnWidth)
    {
        var card = (ToolCard)row.Parent!;
        int innerWidth = Math.Max(1, columnWidth - card.Margin.Horizontal - card.Padding.Horizontal);
        int gap = Math.Min(this.LogicalToDeviceUnits(ChipGap), innerWidth - 1);
        int chipWidth = Math.Min(this.LogicalToDeviceUnits(ChipWidth), innerWidth - gap);
        int chipHeight = this.LogicalToDeviceUnits(ChipHeight);
        int across = Math.Max(1, innerWidth / (chipWidth + gap));
        foreach (var chip in row.Controls.OfType<Button>())
        {
            chip.Size = new Size(chipWidth, chipHeight);
            chip.Margin = new Padding(0, 0, gap, gap);
        }

        int chipRows = Math.Max(1, (row.Controls.Count + across - 1) / across);
        int headingHeight = card.Controls.OfType<Label>().Single().Height;
        int content = card.Margin.Vertical + card.Padding.Vertical + headingHeight + (chipRows * (chipHeight + gap));
        return Math.Max(this.LogicalToDeviceUnits(97), content);
    }

    private static Label SectionHeading(string text)
    {
        var heading = Heading(text);
        heading.Dock = DockStyle.Top;
        heading.Height = 28;
        return heading;
    }

    private static void SetTypes(FlowLayoutPanel row, IEnumerable<int> types)
    {
        row.SuspendLayout();
        foreach (var control in row.Controls.Cast<Control>().ToArray()) control.Dispose();
        foreach (int index in types.OrderBy(type => Matchups[type].Name, StringComparer.CurrentCulture))
        {
            var chip = DashboardTheme.Button(Matchups[index].Name);
            chip.AutoSize = false;
            chip.Tag = index;
            chip.TabStop = false;
            chip.AccessibleRole = AccessibleRole.StaticText;
            row.Controls.Add(chip);
        }
        if (row.Controls.Count == 0)
            row.Controls.Add(new Label { Text = "Keine", ForeColor = DashboardTheme.Muted, AutoSize = true, Margin = new Padding(3) });
        row.ResumeLayout(performLayout: true);
    }

    private void RefreshIcons()
    {
        foreach (var button in this.selectors.Concat(this.attack.Concat(this.defense).SelectMany(row => row.Controls.OfType<Button>())))
        {
            int index = (int)button.Tag!;
            string? type = DashboardTheme.RubyType((byte)(index < 9 ? index : index + 1));
            if (type is not null) button.Image = this.sprites.TypeIcon(type).Image;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) this.sprites.Changed -= this.RefreshIcons;
        base.Dispose(disposing);
    }

    // Ruby's internal type IDs leave a gap at 9.
    private static int Index(int type) => type < 9 ? type : type - 1;

    private static IEnumerable<int> Incoming(int index, Func<TypeMatchup, int[]> relation) =>
        Enumerable.Range(0, Matchups.Length).Where(i => relation(Matchups[i]).Any(target => Index(target) == index));

    private sealed record TypeMatchup(string Name, int[] Strong, int[] Weak, int[] Immune);
}
