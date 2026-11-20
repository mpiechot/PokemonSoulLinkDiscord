using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class SetupForm : Form
{
    private readonly ClientRuntime runtime;
    private readonly bool canChooseRom;

    public SetupForm(ClientRuntime runtime, bool canChooseRom)
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
        tabs.TabPages.Add(this.JoinPage());
        tabs.TabPages.Add(this.PackagePage());
        tabs.TabPages.Add(this.DiagnosticsPage());
        this.Controls.Add(tabs);
    }

    private TabPage GamePage()
    {
        var page = new TabPage("Spiel");
        AddText(page, "ROM für diesen Run", 22, 25, 530, 30, true);
        AddText(page, this.canChooseRom
            ? "SoulLink merkt sich die ROM für diese Spielerdatei. Beim Wechsel startet mGBA automatisch mit der neuen ROM."
            : "Schließe mGBA, um eine andere ROM für diesen Run zu wählen.", 22, 72, 530, 65);
        var choose = AddButton(page, "Andere ROM auswählen", 22, 145, (_, _) => this.DialogResult = DialogResult.Retry);
        choose.Enabled = this.canChooseRom;
        return page;
    }

    private TabPage JoinPage()
    {
        var page = new TabPage("Run verbinden");
        var view = this.runtime.Status();
        AddText(page, "Der Gastgeber kann diese Angaben schon in deiner Datei hinterlegen. Danach verbindet sich SoulLink bei jedem Start automatisch.", 22, 25, 530, 65);
        var server = Input(page, "Serveradresse", view.Registered ? view.Server : "", 105);
        var invitation = Input(page, "Einladung", "", 180);
        var name = Input(page, "Dein Spielername", view.Player, 255);
        var feedback = new Label { Bounds = new Rectangle(22, 378, 530, 50) };
        page.Controls.Add(feedback);
        var join = AddButton(page, "Verbinden", 22, 330, async (_, _) =>
        {
            try
            {
                var form = new JoinForm(server.Text.Trim(), invitation.Text.Trim(), name.Text.Trim());
                feedback.Text = "Verbinde …";
                await Task.Run(() => this.runtime.JoinAsync(form));
                feedback.Text = "Verbunden. Die Anmeldung ist für kommende Starts gespeichert.";
            }
            catch (Exception exception) { feedback.Text = exception.Message; }
        });
        join.Enabled = !view.Registered;
        if (view.Registered) feedback.Text = "Deine Anmeldung ist bereits gespeichert. Du musst hier nichts eingeben.";
        return page;
    }

    private TabPage PackagePage()
    {
        var page = new TabPage("Für Gastgeber");
        AddText(page, "Eine fertige Datei für einen Mitspieler", 22, 25, 530, 32, true);
        AddText(page, "Server, Einladung aus /run-init und Spielername eintragen. Du erhältst eine Kopie dieser EXE; dein Mitspieler startet nur diese Datei. Es werden keine ROMs, Spielstände oder Zugangstokens kopiert.", 22, 67, 530, 75);
        var server = Input(page, "Serveradresse", this.runtime.Status().Registered ? this.runtime.Status().Server : "", 153);
        var invitation = Input(page, "Einladung", "", 220);
        var name = Input(page, "Name des Mitspielers", "", 287);
        var feedback = new Label { Bounds = new Rectangle(22, 402, 530, 36) };
        page.Controls.Add(feedback);
        AddButton(page, "Spielerdatei erstellen", 22, 355, (_, _) =>
        {
            try
            {
                string source = Environment.ProcessPath ?? throw new InvalidOperationException("Anwendungspfad nicht gefunden.");
                if (Path.GetFileNameWithoutExtension(source).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Bitte diese Funktion in der veröffentlichten SoulLink.exe verwenden.");
                var profile = new PlayerProfile(Guid.NewGuid(), server.Text.Trim(), invitation.Text.Trim(), name.Text.Trim());
                PlayerBundle.Validate(profile);
                using var save = new SaveFileDialog
                {
                    Title = "Neue Spielerdatei speichern",
                    Filter = "SoulLink-Anwendung (*.exe)|*.exe",
                    FileName = "SoulLink-Spieler.exe",
                    OverwritePrompt = true
                };
                if (save.ShowDialog(this) != DialogResult.OK) return;
                PlayerBundle.Create(source, save.FileName, profile);
                feedback.Text = "Fertig. Gib deinem Mitspieler diese EXE weiter.";
            }
            catch (Exception exception) { feedback.Text = exception.Message; }
        });
        return page;
    }

    private TabPage DiagnosticsPage()
    {
        var page = new TabPage("Diagnose");
        var view = this.runtime.Status();
        AddText(page, "Nur bei Verbindungsproblemen benötigt", 22, 25, 530, 32, true);
        var details = new TextBox
        {
            Bounds = new Rectangle(22, 80, 530, 350),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Text = $"mGBA: {view.Bridge}\r\nRun: {view.State}\r\n\r\n" + string.Join("\r\n", view.Log),
        };
        page.Controls.Add(details);
        return page;
    }

    private static TextBox Input(Control parent, string label, string value, int y)
    {
        AddText(parent, label, 22, y, 530, 24);
        var field = new TextBox { Bounds = new Rectangle(22, y + 27, 530, 28), Text = value };
        parent.Controls.Add(field);
        return field;
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
