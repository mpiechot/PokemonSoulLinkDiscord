using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class RulesForm : ToolWindow
{
    private static readonly string[] Basics =
    [
        "Pro Fanggebiet zählt die erste wilde Begegnung mit einem Pokéball im Beutel, deren Evolutionsfamilie im Run noch frei ist.",
        "Die Fänge aller Spieler aus demselben Gebiet bilden einen Link. Setzt ihr den Link ein, kommen alle Partner ins Team.",
        "Stirbt ein Pokémon, ist der ganze Link tot. Tote Pokémon dürfen nicht mehr eingesetzt werden.",
        "Der Catch-Check zeigt, ob eine Evolutionsfamilie im Run schon gefangen wurde.",
    ];

    private readonly ClientRuntime runtime;
    private readonly ToolTextStack document = new() { BackColor = DashboardTheme.Background };
    private readonly DashboardScrollHost scroll;
    private readonly TextBox input = RunDialogs.TextInput("Zusatzregel für diesen Run", 240);
    private readonly Button add = Action("Hinzufügen", DashboardTheme.Accent);
    private readonly Label feedback = new() { Dock = DockStyle.Bottom, Height = 31, TextAlign = ContentAlignment.MiddleLeft };
    private readonly System.Windows.Forms.Timer refresh = new() { Interval = 750 };
    private ClientRunView? displayed;
    private bool rendered;
    private bool saving;

    public RulesForm(ClientRuntime runtime) : base("SoulLink-Regeln", new Size(676, 549))
    {
        this.runtime = runtime;
        var editor = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 42, ColumnCount = 2, RowCount = 1 };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        this.input.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        editor.Controls.Add(this.input, 0, 0);
        editor.Controls.Add(this.add, 1, 0);
        this.scroll = new DashboardScrollHost(this.document, this.document.Measure) { Dock = DockStyle.Fill, AccessibleName = "Regelliste" };
        this.Body.Controls.Add(this.scroll);
        this.Body.Controls.Add(editor);
        this.Body.Controls.Add(this.feedback);
        this.input.TextChanged += (_, _) => this.UpdateEditor();
        this.add.Click += async (_, _) => await this.SaveAsync(id => this.runtime.AddRuleAsync(id, this.input.Text), "Zusatzregel hinzugefügt.", clearInput: true);
        this.AcceptButton = this.add;
        this.refresh.Tick += (_, _) => this.RenderRules();
        this.refresh.Start();
        this.RenderRules();
    }

    private bool CanEdit => this.displayed is { Phase: not RunPhase.Finished };

    private void RenderRules()
    {
        var run = this.runtime.Status().Run;
        if (run?.Id != this.displayed?.Id)
        {
            this.feedback.Text = "";
            this.input.Clear();
        }
        string[] extra = run?.AdditionalRules ?? [];
        if (!this.rendered || this.displayed?.Id != run?.Id || this.displayed?.Phase != run?.Phase ||
            !(this.displayed?.AdditionalRules ?? []).SequenceEqual(extra))
        {
            this.rendered = true;
            this.displayed = run;
            this.document.SetRows(this.RuleRows(extra));
            this.scroll.ResizeDocument();
        }
        this.UpdateEditor();
        if (run is null)
        {
            this.feedback.ForeColor = DashboardTheme.Muted;
            this.feedback.Text = "Starte einen lokalen Spieltest oder tritt einem Run bei.";
        }
    }

    private IEnumerable<Control> RuleRows(string[] extra)
    {
        var heading = Heading("Grundregeln");
        heading.Margin = new Padding(0, 3, 0, 7);
        yield return heading;
        foreach (string rule in Basics)
            yield return new Label { Text = $"•  {rule}", Margin = new Padding(3, 0, 7, 7), ForeColor = DashboardTheme.Text };

        heading = Heading(this.displayed is { } run ? $"Zusatzregeln · {run.Name}" : "Zusatzregeln");
        heading.Margin = new Padding(0, 13, 0, 8);
        yield return heading;
        if (extra.Length == 0)
            yield return new Label { Text = "Noch keine Zusatzregeln.", ForeColor = DashboardTheme.Muted };
        foreach (string rule in extra)
            yield return new RuleRow(rule, this.CanEdit ? async () => await this.EditAsync(rule) : null,
                this.CanEdit ? async () => await this.DeleteAsync(rule) : null);
    }

    private void UpdateEditor()
    {
        this.input.Enabled = this.CanEdit && !this.saving;
        this.add.Enabled = this.input.Enabled && !string.IsNullOrWhiteSpace(this.input.Text);
    }

    private async Task EditAsync(string rule)
    {
        if (this.saving || this.displayed?.Id is not Guid run) return;
        if (Prompt(this, "Zusatzregel bearbeiten", "Zusatzregel", 240, rule) is { } replacement && this.displayed?.Id == run)
            await this.SaveAsync(id => this.runtime.ChangeRuleAsync(id, rule, replacement), "Zusatzregel geändert.");
    }

    private async Task DeleteAsync(string rule)
    {
        if (this.saving || this.displayed?.Id is not Guid run) return;
        if (MessageBox.Show(this, $"Diese Zusatzregel löschen?\n\n{rule}", "Zusatzregel löschen",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes && this.displayed?.Id == run)
            await this.SaveAsync(id => this.runtime.ChangeRuleAsync(id, rule, null), "Zusatzregel gelöscht.");
    }

    private async Task SaveAsync(Func<Guid, Task> change, string success, bool clearInput = false)
    {
        if (this.saving || !this.CanEdit || this.displayed?.Id is not Guid id) return;
        this.saving = true;
        this.UpdateEditor();
        try
        {
            await change(id);
            if (this.IsDisposed || this.displayed?.Id != id) return;
            if (clearInput) this.input.Clear();
            this.feedback.ForeColor = DashboardTheme.Accent;
            this.feedback.Text = success;
        }
        catch (Exception error) when (RunDialogs.IsActionError(error))
        {
            if (this.IsDisposed || this.displayed?.Id != id) return;
            this.feedback.ForeColor = DashboardTheme.Danger;
            this.feedback.Text = error.Message;
        }
        finally
        {
            this.saving = false;
            if (!this.IsDisposed) this.RenderRules();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) this.refresh.Dispose();
        base.Dispose(disposing);
    }
}

internal sealed class RuleRow : ToolCard
{
    private const int ActionWidth = 169;
    private readonly Label text;
    private readonly FlowLayoutPanel? actions;

    public RuleRow(string rule, Func<Task>? edit, Func<Task>? delete)
    {
        this.Margin = new Padding(0, 0, 6, 8);
        this.Padding = new Padding(12, 8, 10, 8);
        this.AccessibleName = rule;
        this.text = new Label
        {
            Dock = DockStyle.Fill, Text = rule, TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = DashboardTheme.Text,
            Padding = new Padding(0, 0, edit is null ? 0 : ActionWidth, 0),
        };
        this.Controls.Add(this.text);
        if (edit is null || delete is null) return;

        this.SetStyle(ControlStyles.Selectable, true);
        this.TabStop = true;
        this.actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, Width = ActionWidth, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Visible = false,
        };
        var remove = ToolWindow.Action("Löschen", DashboardTheme.Danger);
        var change = ToolWindow.Action("Bearbeiten");
        remove.Click += async (_, _) => await delete();
        change.Click += async (_, _) => await edit();
        this.actions.Controls.AddRange([remove, change]);
        this.Controls.Add(this.actions);
        foreach (var control in new Control[] { this, this.text, this.actions, remove, change })
        {
            control.MouseEnter += (_, _) => this.ShowActions(true);
            control.MouseLeave += (_, _) => this.RefreshActions();
            control.Enter += (_, _) => this.ShowActions(true);
            control.Leave += (_, _) => this.RefreshActions();
        }
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int available = Math.Max(1, proposedSize.Width - this.Padding.Horizontal - (this.actions?.Width ?? 0) - 5);
        int height = TextRenderer.MeasureText(this.text.Text, this.Font, new Size(available, int.MaxValue),
            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix).Height;
        return new Size(proposedSize.Width, Math.Max(this.actions is null ? 24 : 36, height) + this.Padding.Vertical);
    }

    private void RefreshActions()
    {
        if (!this.IsHandleCreated || this.IsDisposed) return;
        this.BeginInvoke((Action)(() =>
        {
            if (!this.IsDisposed) this.ShowActions(this.ContainsFocus || this.ClientRectangle.Contains(this.PointToClient(Cursor.Position)));
        }));
    }

    private void ShowActions(bool show)
    {
        if (this.actions is null) return;
        this.actions.Visible = show;
        this.BackColor = show ? DashboardTheme.Panel : DashboardTheme.Surface;
        this.text.Padding = new Padding(0, 0, show ? 0 : this.actions.Width, 0);
    }
}
