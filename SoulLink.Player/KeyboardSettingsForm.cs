using System.Drawing.Drawing2D;

namespace SoulLink.Player;

internal sealed class KeyboardSettingsForm : ToolWindow
{
    private static readonly (string Name, string Label, float X, float Y)[] Positions =
    [
        ("L", "L", .22f, .00f), ("R", "R", .78f, .00f),
        ("Up", "Oben", .20f, .30f), ("Left", "Links", .08f, .52f),
        ("Right", "Rechts", .32f, .52f), ("Down", "Unten", .20f, .76f),
        ("Select", "Select", .42f, .76f), ("Start", "Start", .55f, .76f),
        ("B", "B", .71f, .76f), ("A", "A", .85f, .76f),
    ];

    private readonly Bitmap controller;
    private readonly Panel diagram = new() { Dock = DockStyle.Fill };
    private readonly Dictionary<string, KeyEntry> fields = new(StringComparer.Ordinal);

    internal KeyboardSettingsForm(MgbaKeyboardBindings settings, Dictionary<string, int> values)
        : base("Tastenbelegung", new Size(800, 474))
    {
        using var resource = typeof(KeyboardSettingsForm).Assembly.GetManifestResourceStream("SoulLink.Player.Icons.controller.png")
            ?? throw new InvalidOperationException("Die Controller-Grafik fehlt.");
        using var original = Image.FromStream(resource);
        this.controller = new Bitmap(original);
        this.diagram.Paint += (_, args) =>
        {
            args.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            args.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
            args.Graphics.DrawImage(this.controller, this.ControllerBounds());
        };

        foreach (var (name, label, _, _) in Positions)
        {
            var group = new Panel { Size = new Size(92, 53), BackColor = DashboardTheme.Background };
            var caption = new Label { Dock = DockStyle.Top, Text = label, Height = 20, TextAlign = ContentAlignment.MiddleCenter };
            var input = new KeyEntry(label, values[name]) { Dock = DockStyle.Top };
            this.fields.Add(name, input);
            group.Controls.Add(input);
            group.Controls.Add(caption);
            this.diagram.Controls.Add(group);
        }
        this.diagram.SizeChanged += (_, _) => this.PlaceFields();
        this.Shown += (_, _) => this.PlaceFields();

        var feedback = new Label
        {
            Dock = DockStyle.Fill,
            Text = "Feld anklicken und eine Taste drücken. Die Belegung gilt beim nächsten Start von mGBA.",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = DashboardTheme.Muted,
            AutoEllipsis = true,
        };
        var save = Action("Speichern", DashboardTheme.Accent);
        save.Dock = DockStyle.Right;
        save.Width = 115;
        save.Click += (_, _) =>
        {
            try
            {
                settings.Save(this.fields.ToDictionary(pair => pair.Key, pair => pair.Value.Binding, StringComparer.Ordinal));
                feedback.Text = "Gespeichert. Die Belegung wird beim nächsten mGBA-Start geladen.";
                feedback.ForeColor = DashboardTheme.Accent;
            }
            catch (Exception error) when (RunDialogs.IsActionError(error))
            {
                PlayerDiagnostics.Report("keyboard.save.failed", error);
                feedback.Text = error is InvalidDataException ? error.Message : "Die Tastenbelegung konnte nicht gespeichert werden.";
                feedback.ForeColor = DashboardTheme.Danger;
            }
        };
        var bottom = new Panel { Dock = DockStyle.Bottom, Height = 38 };
        bottom.Controls.Add(feedback);
        bottom.Controls.Add(save);
        this.Body.Controls.Add(this.diagram);
        this.Body.Controls.Add(bottom);
    }

    private Rectangle ControllerBounds()
    {
        int top = this.LogicalToDeviceUnits(42);
        int available = Math.Max(1, this.diagram.ClientSize.Height - top - this.LogicalToDeviceUnits(32));
        int width = Math.Min(Math.Max(1, this.diagram.ClientSize.Width), available * this.controller.Width / this.controller.Height);
        int height = Math.Max(1, width * this.controller.Height / this.controller.Width);
        return new Rectangle((this.diagram.ClientSize.Width - width) / 2, top + (available - height) / 2, width, height);
    }

    private void PlaceFields()
    {
        var bounds = this.ControllerBounds();
        foreach (var (name, _, x, y) in Positions)
        {
            var group = this.fields[name].Parent!;
            int left = bounds.Left + (int)(bounds.Width * x) - group.Width / 2;
            int top = y == 0 ? 0 : bounds.Top + (int)(bounds.Height * y);
            group.Location = new Point(left, top);
        }
        this.diagram.Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) this.controller.Dispose();
        base.Dispose(disposing);
    }

    private sealed class KeyEntry : TextBox
    {
        internal KeyEntry(string label, int binding)
        {
            this.AccessibleName = $"{label} – Tastaturbelegung";
            this.TextAlign = HorizontalAlignment.Center;
            this.ReadOnly = true;
            this.ShortcutsEnabled = false;
            this.BackColor = DashboardTheme.Panel;
            this.ForeColor = DashboardTheme.Text;
            this.SetBinding(binding);
        }

        internal int Binding { get; private set; }

        protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode)
            is Keys.Up or Keys.Down or Keys.Left or Keys.Right or Keys.Enter || base.IsInputKey(keyData);

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (MgbaKeyboardBindings.QtKey(e.KeyCode) is not int key) return;
            this.SetBinding(key);
            e.SuppressKeyPress = true;
        }

        private void SetBinding(int value)
        {
            this.Binding = value;
            this.Text = MgbaKeyboardBindings.Label(value);
        }
    }
}
