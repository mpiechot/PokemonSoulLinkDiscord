namespace SoulLink.Player;

internal sealed class TypeChartForm : Form
{
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
    private readonly Button[] selectors;
    private readonly FlowLayoutPanel[] attack;
    private readonly FlowLayoutPanel[] defense;

    public TypeChartForm(RubySprites sprites)
    {
        this.sprites = sprites;
        RunDialogs.Configure(this, "Typen · Pokémon Rubin", new Size(690, 565));

        var selection = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(4),
            BackColor = DashboardTheme.Background,
            AccessibleName = "Pokémon-Typen",
        };
        this.selectors = Matchups.Select((type, index) =>
        {
            var button = DashboardTheme.Button(type.Name);
            button.AutoSize = false;
            button.Size = new Size(102, 34);
            button.Tag = index;
            button.Click += (_, _) => this.SelectType(index);
            selection.Controls.Add(button);
            return button;
        }).ToArray();

        var (attackPanel, attackRows) = MatchupPanel("Angriff", ["2×  Sehr effektiv gegen", "½×  Wenig effektiv gegen", "0×  Wirkungslos gegen"]);
        var (defensePanel, defenseRows) = MatchupPanel("Verteidigung", ["2×  Schwach gegen", "½×  Resistent gegen", "0×  Immun gegen"]);
        this.attack = attackRows;
        this.defense = defenseRows;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 126));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        layout.Controls.Add(selection, 0, 0);
        layout.SetColumnSpan(selection, 2);
        layout.Controls.Add(attackPanel, 0, 1);
        layout.Controls.Add(defensePanel, 1, 1);
        this.Controls.Add(layout);
        this.sprites.Changed += this.RefreshIcons;
        this.SelectType(0);
    }

    private void SelectType(int index)
    {
        for (int i = 0; i < this.selectors.Length; i++)
        {
            this.selectors[i].BackColor = i == index ? DashboardTheme.Border : DashboardTheme.Panel;
            this.selectors[i].ForeColor = i == index ? DashboardTheme.Accent : DashboardTheme.Text;
        }

        var selected = Matchups[index];
        SetTypes(this.attack[0], selected.Strong.Select(Index));
        SetTypes(this.attack[1], selected.Weak.Select(Index));
        SetTypes(this.attack[2], selected.Immune.Select(Index));
        SetTypes(this.defense[0], Incoming(index, type => type.Strong));
        SetTypes(this.defense[1], Incoming(index, type => type.Weak));
        SetTypes(this.defense[2], Incoming(index, type => type.Immune));
        this.RefreshIcons();
    }

    private static (TableLayoutPanel Panel, FlowLayoutPanel[] Rows) MatchupPanel(string title, string[] headings)
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4, Margin = new Padding(2, 8, 2, 0) };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        panel.Controls.Add(new Label
        {
            Text = title,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = DashboardTheme.Text,
        }, 0, 0);
        var rows = new FlowLayoutPanel[headings.Length];
        for (int i = 0; i < headings.Length; i++)
        {
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / headings.Length));
            var group = new Panel { Dock = DockStyle.Fill, BackColor = DashboardTheme.Background, Padding = new Padding(5) };
            var heading = new Label
            {
                Text = headings[i],
                Dock = DockStyle.Top,
                Height = 22,
                ForeColor = i == 0 ? DashboardTheme.Accent : i == 1 ? DashboardTheme.Gold : DashboardTheme.Muted,
            };
            rows[i] = new FlowLayoutPanel { Dock = DockStyle.Fill };
            group.Controls.Add(rows[i]);
            group.Controls.Add(heading);
            panel.Controls.Add(group, 0, i + 1);
        }
        return (panel, rows);
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
            chip.Size = new Size(93, 25);
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
