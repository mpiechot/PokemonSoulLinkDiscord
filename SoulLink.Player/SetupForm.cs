using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class SetupForm : ToolWindow
{
    public SetupForm(ClientView view, string? rom, bool canChooseRom)
        : base("Einrichtung", new Size(630, 474))
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 138));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(GameCard(rom, canChooseRom, (_, _) => this.DialogResult = DialogResult.Retry), 0, 0);
        layout.Controls.Add(Diagnostics(view), 0, 1);
        this.Body.Controls.Add(layout);
    }

    private static ToolCard GameCard(string? rom, bool canChooseRom, EventHandler change)
    {
        var card = new ToolCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 10) };
        var title = Heading("Spieldatei");
        title.Dock = DockStyle.Top;
        title.Height = 24;
        var details = new Label
        {
            Dock = DockStyle.Top, Height = 26, AutoEllipsis = true, AccessibleDescription = rom,
            ForeColor = DashboardTheme.Muted, Text = rom ?? "Für diesen Run wurde noch keine ROM gewählt.",
        };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 36, WrapContents = false };
        if (canChooseRom)
        {
            var choose = Action(rom is null ? "ROM auswählen" : "ROM wechseln", DashboardTheme.Accent);
            choose.Click += change;
            actions.Controls.Add(choose);
        }
        else
        {
            actions.Controls.Add(new Label
            {
                AutoSize = true, ForeColor = DashboardTheme.Gold, Padding = new Padding(0, 7, 0, 0),
                Text = "Schließe mGBA, um die ROM zu wechseln.",
            });
        }
        card.Controls.Add(actions);
        card.Controls.Add(details);
        card.Controls.Add(title);
        return card;
    }

    private static ToolCard Diagnostics(ClientView view)
    {
        var card = new ToolCard { Dock = DockStyle.Fill, Margin = new Padding(0) };
        var heading = Heading("Verbindung");
        heading.Dock = DockStyle.Top;
        heading.Height = 26;
        var status = new Label
        {
            Dock = DockStyle.Top, Height = 44, AutoEllipsis = true, ForeColor = DashboardTheme.Muted,
            Text = $"mGBA  ·  {view.Bridge}\nRun-Server  ·  {view.State}",
        };
        var details = new RichTextBox
        {
            ReadOnly = true, BorderStyle = BorderStyle.None, DetectUrls = false,
            BackColor = DashboardTheme.Background, ForeColor = DashboardTheme.Text, AccessibleName = "Verbindungsprotokoll",
            Text = view.Log.Length == 0 ? "Noch keine Diagnosemeldungen." : string.Join(Environment.NewLine, view.Log),
        };
        var diagnostics = DashboardScrollHost.ForText(details);
        diagnostics.Dock = DockStyle.Fill;
        card.Controls.Add(diagnostics);
        card.Controls.Add(status);
        card.Controls.Add(heading);
        return card;
    }
}
