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

    public TypeChartForm()
    {
        RunDialogs.Configure(this, "Typen · Pokémon Rubin", new Size(580, 365));
        var selection = new ComboBox
        {
            Dock = DockStyle.Top,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = DashboardTheme.Background,
            ForeColor = DashboardTheme.Text,
            AccessibleName = "Pokémon-Typ",
        };
        selection.Items.AddRange(Matchups.Select(type => type.Name).ToArray());
        var attack = ResultLabel();
        var defense = ResultLabel();
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.Controls.Add(selection, 0, 0);
        layout.Controls.Add(Heading("Angriff"), 0, 1);
        layout.Controls.Add(attack, 0, 2);
        layout.Controls.Add(Heading("Verteidigung"), 0, 3);
        layout.Controls.Add(defense, 0, 4);
        this.Controls.Add(layout);
        selection.SelectedIndexChanged += (_, _) =>
        {
            int index = selection.SelectedIndex;
            if (index < 0) return;
            var type = Matchups[index];
            attack.Text = $"2×  Sehr effektiv gegen  {Names(type.Strong)}\n" +
                $"½×  Wenig effektiv gegen  {Names(type.Weak)}\n" +
                $"0×  Wirkungslos gegen  {Names(type.Immune)}";
            defense.Text = $"2×  Schwach gegen  {Incoming(index, match => match.Strong)}\n" +
                $"½×  Resistent gegen  {Incoming(index, match => match.Weak)}\n" +
                $"0×  Immun gegen  {Incoming(index, match => match.Immune)}";
        };
        selection.SelectedIndex = 0;
    }

    private static Label Heading(string text) => new()
    {
        Dock = DockStyle.Fill,
        Text = text,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = DashboardTheme.Accent,
    };

    private static Label ResultLabel() => new()
    {
        Dock = DockStyle.Fill,
        BackColor = DashboardTheme.Panel,
        Padding = new Padding(9),
    };

    // Ruby's internal type IDs leave a gap at 9.
    private static int Index(int type) => type < 9 ? type : type - 1;

    private static string Names(int[] types) => Describe(types.Select(type => Matchups[Index(type)].Name));

    private static string Incoming(int index, Func<TypeMatchup, int[]> relation) =>
        Describe(Matchups.Where(type => relation(type).Any(target => Index(target) == index)).Select(type => type.Name));

    private static string Describe(IEnumerable<string> names) =>
        string.Join(", ", names.Order(StringComparer.CurrentCulture).DefaultIfEmpty("—"));

    private sealed record TypeMatchup(string Name, int[] Strong, int[] Weak, int[] Immune);
}
