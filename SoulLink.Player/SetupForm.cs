using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class SetupForm : Form
{
    private readonly ClientRuntime runtime;
    private readonly bool canChooseRom;

    public SetupForm(ClientRuntime runtime, bool canChooseRom, bool canSwitchRun)
    {
        this.runtime = runtime;
        this.canChooseRom = canChooseRom;
        this.Text = "SoulLink – Einrichtung";
        this.ClientSize = new Size(590, 500);
        this.FormBorderStyle = FormBorderStyle.FixedDialog;
        this.MaximizeBox = false;
        this.MinimizeBox = false;
        this.StartPosition = FormStartPosition.CenterParent;
        this.Font = new Font("Segoe UI", 10);
        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(14, 10) };
        tabs.TabPages.Add(this.GamePage());
        if (canSwitchRun) tabs.TabPages.Add(this.JoinPage());
        tabs.TabPages.Add(this.DiagnosticsPage());
        this.Controls.Add(tabs);
    }

    private TabPage GamePage()
    {
        var page = new TabPage("Spiel");
        AddText(page, "ROM für diesen Run", 22, 25, 530, 30, true);
        AddText(page, this.canChooseRom
            ? "SoulLink merkt sich die ROM für diesen Run. Beim Wechsel startet mGBA automatisch mit der neuen ROM."
            : "Schließe mGBA, um eine andere ROM für diesen Run zu wählen.", 22, 72, 530, 65);
        var choose = AddButton(page, "Andere ROM auswählen", 22, 145, (_, _) => this.DialogResult = DialogResult.Retry);
        choose.Enabled = this.canChooseRom;
        return page;
    }

    private TabPage JoinPage()
    {
        var page = new TabPage("Run verbinden");
        AddText(page, "Füge eine neue Einladung ein oder öffne einen gespeicherten Run.", 22, 25, 530, 56);
        AddButton(page, "Run beitreten / wechseln", 22, 92, (_, _) => this.DialogResult = DialogResult.Yes);
        return page;
    }

    private TabPage DiagnosticsPage()
    {
        var page = new TabPage("Diagnose");
        var view = this.runtime.Status();
        AddText(page, "Nur bei Verbindungsproblemen benötigt", 22, 25, 530, 32, true);
        var details = new RichTextBox
        {
            Multiline = true,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = DashboardTheme.Background,
            ForeColor = DashboardTheme.Text,
            DetectUrls = false,
            Text = $"mGBA: {view.Bridge}\r\nRun: {view.State}\r\n\r\n" + string.Join("\r\n", view.Log),
        };
        var diagnostics = DashboardScrollHost.ForText(details);
        diagnostics.Bounds = new Rectangle(22, 80, 530, 350);
        page.Controls.Add(diagnostics);
        return page;
    }

    private static void AddText(Control parent, string text, int x, int y, int width, int height, bool bold = false)
    {
        parent.Controls.Add(new Label
        {
            Bounds = new Rectangle(x, y, width, height),
            Text = text,
            Font = new Font("Segoe UI", bold ? 13 : 10, bold ? FontStyle.Bold : FontStyle.Regular)
        });
    }

    private static Button AddButton(Control parent, string text, int x, int y, EventHandler click)
    {
        var button = new Button { Bounds = new Rectangle(x, y, 225, 36), Text = text };
        button.Click += click;
        parent.Controls.Add(button);
        return button;
    }
}
