using SoulLink;

namespace SoulLink.Player;

internal sealed class PokeWikiForm : ToolWindow
{
    private static readonly Font ArticleFont = new("Segoe UI Semibold", 16);
    private readonly RubySprites sprites;
    private readonly Image? placeholder;
    private readonly TextBox search = RunDialogs.TextInput("Name oder Pokédex-Nr.", 64);
    private readonly ListBox species = new()
    {
        Dock = DockStyle.Fill,
        AccessibleName = "Pokémon-Liste",
        BorderStyle = BorderStyle.None,
        DisplayMember = nameof(RubyWikiPokemon.Name),
        BackColor = DashboardTheme.Background,
        ForeColor = DashboardTheme.Text,
        DrawMode = DrawMode.OwnerDrawFixed,
        IntegralHeight = false,
    };
    private readonly Label searchStatus = TextLabel("Pokémon", DashboardTheme.Muted);
    private readonly Label empty = TextLabel("Keine Pokémon gefunden.", DashboardTheme.Muted);
    private readonly Label name = Heading(string.Empty);
    private readonly Label types = TextLabel(string.Empty, DashboardTheme.Accent);
    private readonly Label source = TextLabel("Rubin · Standarddaten", DashboardTheme.Muted);
    private readonly PictureBox portrait = SpriteBox();
    private readonly Label[] statValues = new Label[6];
    private readonly TableLayoutPanel article = new() { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
    private readonly RowStyle evolutionHeight = new(SizeType.Absolute, 96);
    private readonly Panel evolutions = new() { Dock = DockStyle.Fill, AutoScroll = true, AccessibleName = "Entwicklungen" };
    private readonly DashboardComboBox learnMethod = new() { Dock = DockStyle.Fill, AccessibleName = "Lernmethode" };
    private readonly DataGridView learnset = MoveGrid();
    private readonly Label moveSummary = TextLabel(string.Empty, DashboardTheme.Accent);
    private readonly Label moveEffect = TextLabel(string.Empty, DashboardTheme.Text);
    private readonly List<(int Species, PictureBox Image)> pictures = [];
    private readonly Dictionary<int, Bitmap?> cartridgePictures = [];
    private RubyWikiCatalog catalog = RubyWikiCatalog.Standard;
    private string? artworkRom;
    private string? requestedRom;
    private RubyWikiPokemon? selected;
    private bool updatingSpecies;

    public PokeWikiForm(RubySprites sprites, Image? placeholder) : base("PokéWiki · Pokémon Rubin", new Size(936, 720), resizable: true)
    {
        this.sprites = sprites;
        this.placeholder = placeholder;
        this.MinimumSize = new Size(730, 540);
        this.name.Font = ArticleFont;
        this.search.Dock = DockStyle.Fill;
        this.search.Margin = new Padding(0, 5, 0, 6);
        this.species.ItemHeight = 28;
        this.empty.TextAlign = ContentAlignment.MiddleCenter;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 218));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.Controls.Add(this.BuildNavigation(), 0, 0);
        var page = new Panel { Dock = DockStyle.Fill, Margin = new Padding(10, 0, 0, 0) };
        this.article.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        this.article.RowStyles.Add(this.evolutionHeight);
        this.article.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        this.article.Controls.Add(this.BuildOverview(), 0, 0);
        this.article.Controls.Add(this.BuildEvolutions(), 0, 1);
        this.article.Controls.Add(this.BuildLearnset(), 0, 2);
        page.Controls.Add(this.article);
        page.Controls.Add(this.empty);
        root.Controls.Add(page, 1, 0);
        this.Body.Controls.Add(root);

        this.sprites.Changed += this.RefreshPictures;
        this.species.DrawItem += this.DrawSpecies;
        this.species.SelectedIndexChanged += (_, _) =>
        {
            if (!this.updatingSpecies) this.Display(this.species.SelectedItem as RubyWikiPokemon);
        };
        this.search.TextChanged += (_, _) =>
        {
            if (!this.updatingSpecies) this.Filter();
        };
        this.search.KeyDown += (_, args) =>
        {
            if (args.KeyCode is not (Keys.Down or Keys.Enter) || this.species.Items.Count == 0) return;
            this.species.Focus();
            args.SuppressKeyPress = true;
        };
        this.learnMethod.SelectedIndexChanged += (_, _) => this.ShowMoves();
        this.learnset.CurrentCellChanged += (_, _) => this.ShowSelectedMove();
        this.Shown += (_, _) =>
        {
            this.FitWorkingArea();
            this.search.Select();
        };
        this.KeyPreview = true;
        this.KeyDown += (_, args) =>
        {
            if (args.KeyCode == Keys.Escape)
            {
                args.SuppressKeyPress = true;
                this.Close();
            }
            else if (args.Control && args.KeyCode == Keys.F)
            {
                this.search.Select();
                this.search.SelectAll();
                args.SuppressKeyPress = true;
            }
        };
        this.Filter();
    }

    public void SelectPokemon(int species)
    {
        if (this.catalog.Find(species) is null) return;
        this.updatingSpecies = true;
        this.search.Clear();
        this.updatingSpecies = false;
        this.Filter(species);
    }

    public async Task SetCartridgeAsync(string? path)
    {
        if (string.Equals(path, this.requestedRom, StringComparison.OrdinalIgnoreCase)) return;
        this.requestedRom = path;
        this.artworkRom = null;
        this.ClearCartridgePictures();
        this.catalog = RubyWikiCatalog.Standard;
        this.source.Text = path is null ? "Rubin · Standarddaten" : "Rubin · ROM-Daten werden geladen …";
        this.Filter(this.selected?.Species);
        if (path is null) return;

        RubyWikiCatalog? cartridge = await Task.Run(() => ReadCartridge(path));
        if (this.IsDisposed || !string.Equals(path, this.requestedRom, StringComparison.OrdinalIgnoreCase)) return;
        this.catalog = cartridge ?? RubyWikiCatalog.Standard;
        this.artworkRom = cartridge is null ? null : path;
        this.source.Text = cartridge is null ? "Rubin · Standarddaten (ROM-Daten nicht lesbar)" : "Rubin · Daten aus deiner ROM";
        this.Filter(this.selected?.Species);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            this.sprites.Changed -= this.RefreshPictures;
            this.ClearCartridgePictures();
        }
        base.Dispose(disposing);
    }

    private void ClearCartridgePictures()
    {
        foreach (var (_, box) in this.pictures)
        {
            if (!box.IsDisposed) box.Image = null;
        }
        foreach (var bitmap in this.cartridgePictures.Values) bitmap?.Dispose();
        this.cartridgePictures.Clear();
    }

    private static RubyWikiCatalog? ReadCartridge(string path)
    {
        try
        {
            using var rom = File.OpenRead(path);
            return RubyWikiCatalog.ReadCartridge(rom);
        }
        catch (Exception error) when (error is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            PlayerDiagnostics.Report("wiki.cartridge.unavailable", error);
            return null;
        }
    }

    private static Label TextLabel(string text, Color color) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        ForeColor = color,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = Padding.Empty,
    };

    private static PictureBox SpriteBox() => new()
    {
        Dock = DockStyle.Fill,
        SizeMode = PictureBoxSizeMode.Zoom,
        Margin = new Padding(1),
    };

    private ToolCard BuildNavigation()
    {
        var card = new ToolCard { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(12) };
        var navigation = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
        navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        navigation.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        navigation.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        var title = Heading("Pokédex");
        title.Dock = DockStyle.Fill;
        navigation.Controls.Add(title, 0, 0);
        navigation.Controls.Add(this.search, 0, 1);
        navigation.Controls.Add(this.species, 0, 2);
        navigation.Controls.Add(this.searchStatus, 0, 3);
        card.Controls.Add(navigation);
        return card;
    }

    private ToolCard BuildOverview()
    {
        var card = new ToolCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(12, 8, 12, 8) };
        var overview = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 4 };
        overview.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 82));
        overview.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        overview.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        overview.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        overview.RowStyles.Add(new RowStyle(SizeType.Absolute, 20));
        overview.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        this.name.Dock = DockStyle.Fill;
        this.name.Margin = Padding.Empty;
        overview.Controls.Add(this.portrait, 0, 0);
        overview.SetRowSpan(this.portrait, 4);
        overview.Controls.Add(this.name, 1, 0);
        overview.Controls.Add(this.types, 1, 1);
        overview.Controls.Add(this.source, 1, 2);

        string[] statNames = ["KP", "Angriff", "Verteid.", "Sp.-Ang.", "Sp.-Vert.", "Initiative"];
        var stats = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = statNames.Length, RowCount = 1, Margin = Padding.Empty };
        for (int i = 0; i < statNames.Length; i++)
        {
            stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / statNames.Length));
            this.statValues[i] = TextLabel(statNames[i], DashboardTheme.Text);
            this.statValues[i].TextAlign = ContentAlignment.MiddleCenter;
            this.statValues[i].BackColor = DashboardTheme.Panel;
            this.statValues[i].Margin = new Padding(0, 1, 5, 0);
            stats.Controls.Add(this.statValues[i], i, 0);
        }
        overview.Controls.Add(stats, 1, 3);
        card.Controls.Add(overview);
        return card;
    }

    private ToolCard BuildEvolutions()
    {
        var card = new ToolCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 8), Padding = new Padding(12, 7, 12, 8) };
        var heading = Heading("Entwicklungen");
        heading.Dock = DockStyle.Top;
        heading.Height = 26;
        card.Controls.Add(this.evolutions);
        card.Controls.Add(heading);
        return card;
    }

    private ToolCard BuildLearnset()
    {
        var card = new ToolCard { Dock = DockStyle.Fill, Margin = Padding.Empty, Padding = new Padding(12, 8, 12, 10) };
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, Margin = Padding.Empty, ColumnCount = 2, RowCount = 1 };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 156));
        var title = Heading("Attacken");
        title.Dock = DockStyle.Fill;
        title.TextAlign = ContentAlignment.MiddleLeft;
        this.learnMethod.Items.AddRange(["Levelaufstieg", "TM und VM", "Zucht"]);
        this.learnMethod.SelectedIndex = 0;
        header.Controls.Add(title, 0, 0);
        header.Controls.Add(this.learnMethod, 1, 0);
        this.moveSummary.Padding = new Padding(0, 5, 0, 0);
        this.moveEffect.TextAlign = ContentAlignment.TopLeft;
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(this.learnset, 0, 1);
        layout.Controls.Add(this.moveSummary, 0, 2);
        layout.Controls.Add(this.moveEffect, 0, 3);
        card.Controls.Add(layout);
        return card;
    }

    private static DataGridView MoveGrid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AccessibleName = "Lernliste",
            BackgroundColor = DashboardTheme.Background,
            BorderStyle = BorderStyle.None,
            GridColor = DashboardTheme.Border,
            RowHeadersVisible = false,
            ReadOnly = true,
            ShowCellToolTips = true,
            MultiSelect = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            AllowUserToResizeRows = false,
            AllowUserToResizeColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 29,
            ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing,
        };
        grid.RowTemplate.Height = 27;
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = DashboardTheme.Background,
            ForeColor = DashboardTheme.Text,
            SelectionBackColor = DashboardTheme.Border,
            SelectionForeColor = DashboardTheme.Text,
            Padding = new Padding(6, 0, 0, 0),
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle(grid.DefaultCellStyle)
        {
            BackColor = DashboardTheme.Panel,
            ForeColor = DashboardTheme.Muted,
        };
        grid.Columns.AddRange(
            Column("Lernweg", 92), Column("Attacke", 120, fill: true), Column("Typ", 90), Column("Stärke", 77));
        return grid;

        static DataGridViewTextBoxColumn Column(string title, int width, bool fill = false) => new()
        {
            HeaderText = title,
            Width = width,
            MinimumWidth = width,
            SortMode = DataGridViewColumnSortMode.NotSortable,
            AutoSizeMode = fill ? DataGridViewAutoSizeColumnMode.Fill : DataGridViewAutoSizeColumnMode.None,
        };
    }

    private void Filter(int? keep = null)
    {
        RubyWikiPokemon[] matches = this.catalog.Search(this.search.Text).ToArray();
        this.updatingSpecies = true;
        this.species.BeginUpdate();
        try
        {
            this.species.Items.Clear();
            this.species.Items.AddRange(matches);
            int index = Array.FindIndex(matches, entry => entry.Species == keep);
            if (matches.Length > 0) this.species.SelectedIndex = Math.Max(0, index);
        }
        finally
        {
            this.species.EndUpdate();
            this.updatingSpecies = false;
        }

        this.searchStatus.Text = matches.Length == 1 ? "1 Pokémon" : $"{matches.Length} Pokémon";
        this.Display(this.species.SelectedItem as RubyWikiPokemon);
    }

    private void DrawSpecies(object? sender, DrawItemEventArgs args)
    {
        if (args.Index < 0 || this.species.Items[args.Index] is not RubyWikiPokemon entry) return;
        bool active = args.State.HasFlag(DrawItemState.Selected);
        using var background = new SolidBrush(active ? DashboardTheme.Panel : DashboardTheme.Background);
        args.Graphics.FillRectangle(background, args.Bounds);
        var bounds = args.Bounds;
        bounds.X += 8;
        bounds.Width = Math.Max(1, bounds.Width - 8);
        TextRenderer.DrawText(args.Graphics, $"#{entry.NationalDex:000}   {entry.Name}", this.Font, bounds,
            active ? DashboardTheme.Accent : DashboardTheme.Text, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        args.DrawFocusRectangle();
    }

    private void Display(RubyWikiPokemon? entry)
    {
        this.selected = entry;
        this.article.Visible = entry is not null;
        this.empty.Visible = entry is null;
        if (entry is null) return;
        this.name.Text = $"#{entry.NationalDex:000}  {entry.Name}";
        this.types.Text = string.Join(" · ", new[] { entry.Type1, entry.Type2 }.Distinct().Select(RubyWikiCatalog.TypeName)) + "  ·  Basiswerte";
        var stat = entry.Details.BaseStats;
        string[] values = [$"KP {stat.Hp}", $"Angr. {stat.Attack}", $"Vert. {stat.Defense}",
            $"SpA {stat.SpecialAttack}", $"SpV {stat.SpecialDefense}", $"Init. {stat.Speed}"];
        string[] labels = ["KP", "Angriff", "Verteidigung", "Spezial-Angriff", "Spezial-Verteidigung", "Initiative"];
        int[] scores = [stat.Hp, stat.Attack, stat.Defense, stat.SpecialAttack, stat.SpecialDefense, stat.Speed];
        for (int i = 0; i < values.Length; i++)
        {
            this.statValues[i].Text = values[i];
            this.statValues[i].AccessibleName = $"{labels[i]} {scores[i]}";
        }

        this.pictures.Clear();
        this.pictures.Add((entry.Species, this.portrait));
        this.ShowEvolutions(entry);
        this.RefreshPictures();
        this.ShowMoves();
    }

    private void ShowEvolutions(RubyWikiPokemon entry)
    {
        foreach (Control old in this.evolutions.Controls.Cast<Control>().ToArray()) old.Dispose();
        RubyWikiEvolution[] links = this.catalog.Evolutions(entry.Species).ToArray();
        this.evolutionHeight.Height = links.Length == 0 ? 79 : 49 + Math.Min(links.Length, 3) * 47;
        if (links.Length == 0)
        {
            this.evolutions.Controls.Add(TextLabel("Keine Entwicklung.", DashboardTheme.Muted));
            return;
        }

        var rows = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            RowCount = links.Length,
            ColumnCount = 3,
        };
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 133));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rows.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 133));
        for (int i = 0; i < links.Length; i++)
        {
            var edge = links[i];
            rows.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
            rows.Controls.Add(this.EvolutionStage(edge.From), 0, i);
            var requirement = TextLabel($"→  {edge.Requirement}", DashboardTheme.Muted);
            requirement.Padding = new Padding(5, 0, 4, 0);
            rows.Controls.Add(requirement, 1, i);
            rows.Controls.Add(this.EvolutionStage(edge.To), 2, i);
        }
        this.evolutions.Controls.Add(rows);
    }

    private Control EvolutionStage(int species)
    {
        var stage = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        stage.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 42));
        stage.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var image = SpriteBox();
        var button = Action(RubyNames.Pokemon(species), species == this.selected?.Species ? DashboardTheme.Accent : null);
        button.Tag = species;
        button.AutoSize = false;
        button.Dock = DockStyle.Fill;
        button.Padding = Padding.Empty;
        button.AutoEllipsis = true;
        button.Click += (_, _) => this.SelectPokemon(species);
        image.Click += (_, _) => this.SelectPokemon(species);
        image.Cursor = Cursors.Hand;
        stage.Controls.Add(image, 0, 0);
        stage.Controls.Add(button, 1, 0);
        this.pictures.Add((species, image));
        return stage;
    }

    private void RefreshPictures()
    {
        foreach (var (species, box) in this.pictures)
        {
            box.AccessibleName = RubyNames.Pokemon(species);
            if (!this.cartridgePictures.TryGetValue(species, out var sprite) && this.artworkRom is { } path)
            {
                sprite = RubyWikiArtwork.Read(path, species);
                this.cartridgePictures.Add(species, sprite);
            }
            box.Image = sprite ?? (RubyDashboardCatalog.Appearance(species) is { } appearance ? this.sprites.Find(appearance, false) : null) ?? this.placeholder;
        }
    }

    private void ShowMoves()
    {
        this.learnset.Rows.Clear();
        if (this.selected is not { } entry) return;
        IEnumerable<(string Source, int Move)> moves = this.learnMethod.SelectedIndex switch
        {
            1 => entry.Machines.Select(machine => ($"{(machine.Hidden ? "VM" : "TM")}{machine.Number:00}", machine.Move)),
            2 => entry.EggMoves.Select(move => ("Zucht", move)),
            _ => entry.Details.Moves.OrderBy(move => move.Level).Select(move =>
                (move.Level <= 1 ? "Start" : $"Level {move.Level}", move.Move)),
        };
        foreach (var (source, id) in moves)
        {
            if (this.catalog.Move(id) is not { } move) continue;
            int index = this.learnset.Rows.Add(source, move.Name, move.TypeName, move.PowerText);
            var row = this.learnset.Rows[index];
            row.Tag = move;
            string tooltip = $"{move.Name} · {move.TypeName} · {move.Category}\n{MoveStats(move)}\n{move.Description}";
            foreach (DataGridViewCell cell in row.Cells) cell.ToolTipText = tooltip;
        }
        if (this.learnset.Rows.Count > 0) this.learnset.CurrentCell = this.learnset.Rows[0].Cells[1];
        this.ShowSelectedMove();
    }

    private void ShowSelectedMove()
    {
        if (this.learnset.CurrentRow?.Tag is RubyWikiMove move)
        {
            this.moveSummary.Text = $"{move.Name} · {move.Category}  |  {MoveStats(move)}";
            this.moveEffect.Text = move.Description;
            return;
        }
        this.moveSummary.Text = "Keine Attacken für diese Lernmethode.";
        this.moveEffect.Text = string.Empty;
    }

    private static string MoveStats(RubyWikiMove move)
    {
        string accuracy = move.Accuracy == 0 ? "–" : $"{move.Accuracy} %";
        string priority = move.Priority == 0 ? string.Empty : $" · Priorität {move.Priority:+0;-0}";
        string power = move.PowerText.EndsWith(" KP", StringComparison.Ordinal) ? $"Fester Schaden {move.PowerText}" : $"Stärke {move.PowerText}";
        return $"{power} · Genauigkeit {accuracy} · AP {move.Pp}{priority}";
    }
}
