using System.Runtime.InteropServices;
using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class CatchCheckForm : ToolWindow
{
    private readonly TextBox query = RunDialogs.TextInput("Deutscher oder englischer Pokémon-Name", 80);
    private readonly CatchVerdictPanel outcome = new() { AccessibleName = "Catch-Check-Ergebnis", Margin = new Padding(0, 8, 0, 0) };
    private readonly Button check = DashboardTheme.Button("Prüfen");

    public CatchCheckForm(ClientRuntime runtime, Guid runId) : base("Catch-Check", new Size(460, 260))
    {
        this.query.AccessibleName = "Pokémon-Name";
        this.query.Anchor = AnchorStyles.Left | AnchorStyles.Right;
        var header = new TableLayoutPanel { Dock = DockStyle.Top, Height = 38, ColumnCount = 2, Margin = new Padding(0) };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(this.query, 0, 0);
        header.Controls.Add(this.check, 1, 0);
        this.check.Anchor = AnchorStyles.Right;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = new Padding(0) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(header, 0, 0);
        layout.Controls.Add(this.outcome, 0, 1);
        this.Body.Controls.Add(layout);
        this.AcceptButton = this.check;
        this.check.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(this.query.Text)) return;
            this.check.Enabled = false;
            this.outcome.SetResult(null, "");
            try
            {
                var result = await runtime.CheckCatchAsync(runId, this.query.Text);
                if (this.IsDisposed) return;
                this.outcome.SetResult(result.Pokemon is not null && result.PreviouslyCaught is null,
                    result.Pokemon is null ? "Pokémon unbekannt. Prüfe die Schreibweise."
                    : result.PreviouslyCaught is null ? $"{result.Pokemon} ist nach der Evolutionsfamilien-Regel erlaubt."
                    : $"{result.Pokemon} ist bereits abgedeckt.\n{result.Player} hat {result.PreviouslyCaught} in {result.Area} gefangen.");
            }
            catch (Exception error) when (RunDialogs.IsActionError(error))
            {
                if (!this.IsDisposed) this.outcome.SetResult(false, "Der Catch-Check ist gerade nicht verfügbar.");
            }
            finally { if (!this.IsDisposed) this.check.Enabled = true; }
        };
    }
}

internal sealed class CatchVerdictPanel : TableLayoutPanel
{
    private readonly Label icon = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleCenter,
        Font = new Font("Segoe UI Symbol", 18),
        Margin = new Padding(0),
    };
    private readonly Label detail = new()
    {
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.TopCenter,
        AutoEllipsis = true,
        Margin = new Padding(0, 0, 0, 3),
    };
    private readonly RowStyle detailRow = new(SizeType.Absolute, 34);

    public CatchVerdictPanel()
    {
        this.Dock = DockStyle.Fill;
        this.BackColor = DashboardTheme.Panel;
        this.Padding = new Padding(8, 0, 8, 0);
        this.ColumnCount = 1;
        this.RowCount = 4;
        this.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        this.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
        this.RowStyles.Add(this.detailRow);
        this.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        this.Controls.Add(this.icon, 0, 1);
        this.Controls.Add(this.detail, 0, 2);
        this.SizeChanged += (_, _) => this.MeasureDetail();
    }

    public void SetResult(bool? allowed, string message)
    {
        string symbol = allowed switch { true => "✓", false => "✕", _ => "" };
        if (this.detail.Text == message && this.icon.Text == symbol) return;
        this.icon.Text = symbol;
        this.icon.ForeColor = allowed == true ? DashboardTheme.Accent : DashboardTheme.Danger;
        this.detail.Text = message;
        this.AccessibleDescription = message;
        this.MeasureDetail();
    }

    private void MeasureDetail()
    {
        var bounds = new Size(Math.Max(1, this.ClientSize.Width - this.Padding.Horizontal), 0);
        int height = TextRenderer.MeasureText(this.detail.Text, this.detail.Font, bounds, TextFormatFlags.WordBreak).Height + 3;
        if (this.detailRow.Height != height) this.detailRow.Height = height;
    }
}

internal sealed class RunEndForm : ToolWindow
{
    public RunEndForm(Guid incidentId, Func<string, Task> complete) : base("Run abschließen", new Size(480, 245))
    {
        this.IncidentId = incidentId;
        var explanation = new Label
        {
            Dock = DockStyle.Top,
            Height = 27,
            Text = "Der letzte lebende Link ist gestorben.",
        };
        var reason = RunDialogs.TextInput("Grund für das Run-Ende (optional)", 250);
        reason.Dock = DockStyle.Top;
        var feedback = new Label { Dock = DockStyle.Fill, ForeColor = DashboardTheme.Danger, TextAlign = ContentAlignment.MiddleLeft };
        var card = new ToolCard { Dock = DockStyle.Fill };
        var button = Action("Run abschließen", DashboardTheme.Danger);
        var later = Action("Später");
        later.Click += (_, _) => this.Close();
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft };
        actions.Controls.AddRange([button, later]);
        card.Controls.Add(feedback);
        card.Controls.Add(reason);
        card.Controls.Add(new Label { Dock = DockStyle.Top, Height = 24, Text = "Grund (optional)", ForeColor = DashboardTheme.Muted });
        card.Controls.Add(explanation);
        card.Controls.Add(actions);
        this.Body.Controls.Add(card);
        this.AcceptButton = button;
        this.CancelButton = later;
        button.Click += async (_, _) =>
        {
            button.Enabled = false;
            try
            {
                await complete(reason.Text.Trim());
                if (!this.IsDisposed) this.Close();
            }
            catch (Exception error) when (RunDialogs.IsActionError(error))
            {
                if (this.IsDisposed) return;
                feedback.Text = error.Message;
                button.Enabled = true;
            }
        };
    }

    public Guid IncidentId { get; }

    protected override bool ShowWithoutActivation => true;
}

internal sealed class DeathReasonForm : ToolWindow
{
    private readonly TextBox reason = RunDialogs.TextInput("Grund (optional)", 250);
    private readonly Label details = new() { Dock = DockStyle.Top, Height = 94, Padding = new Padding(0, 0, 0, 12) };

    public DeathReasonForm(PendingDeath pending) : base("Todesfall prüfen", new Size(485, 265))
    {
        this.reason.Dock = DockStyle.Top;
        this.IncidentId = pending.IncidentId;
        this.reason.AccessibleName = "Todesursache";
        this.UpdateTrace(pending);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 41, FlowDirection = FlowDirection.RightToLeft };
        var confirm = DashboardTheme.Button("Tod bestätigen");
        var dismiss = DashboardTheme.Button("Als lebend belassen");
        var later = DashboardTheme.Button("Später");
        confirm.Click += (_, _) => this.Finish(DialogResult.Yes);
        dismiss.Click += (_, _) => this.Finish(DialogResult.No);
        later.Click += (_, _) => this.Close();
        actions.Controls.AddRange([confirm, dismiss, later]);
        this.CancelButton = later;
        var card = new ToolCard { Dock = DockStyle.Fill };
        card.Controls.Add(this.reason);
        card.Controls.Add(this.details);
        card.Controls.Add(actions);
        this.Body.Controls.Add(card);
    }

    public Guid IncidentId { get; }
    public DialogResult Decision { get; private set; }
    public string Reason { get; private set; } = "";
    protected override bool ShowWithoutActivation => true;

    public void UpdateTrace(PendingDeath pending)
    {
        var trace = pending.Battle;
        string opponent = trace?.OpponentSpecies is int foe ? RubyNames.Pokemon(foe) : "unbekannt";
        var (label, move) = RunDialogs.BattleMove(trace);
        this.details.Text = $"{RubyNames.Pokemon(pending.Species)} ist kampfunfähig.\n" +
            $"Gegner: {opponent}{(string.IsNullOrEmpty(trace?.Trainer) ? "" : $" (Trainer: {trace.Trainer})")}\n" +
            $"{label}: {move}";
    }

    protected override void WndProc(ref Message message)
    {
        // Do not steal a key-up from the emulator when a player clicks the notice while holding a key.
        if (message.Msg == 0x0021 && Enumerable.Range(8, 247).Any(key => GetAsyncKeyState(key) < 0))
        {
            message.Result = 4;
            return;
        }
        base.WndProc(ref message);
    }

    private void Finish(DialogResult choice)
    {
        this.Decision = choice;
        this.Reason = this.reason.Text.Trim();
        this.Close();
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int key);
}

internal static class RunDialogs
{
    internal static (string Label, string Name) BattleMove(BattleTrace? battle)
    {
        if (RubyNames.Move(battle?.Move) is not { } name) return ("Attacke", "Unbekannt");
        return (battle!.FinishingBlow ? "K.-o.-Attacke" : "Letzte erfasste Attacke", name);
    }

    public static TextBox TextInput(string placeholder, int maxLength) => new()
    {
        PlaceholderText = placeholder,
        AccessibleName = placeholder,
        MaxLength = maxLength,
        BorderStyle = BorderStyle.FixedSingle,
        BackColor = DashboardTheme.Background,
        ForeColor = DashboardTheme.Text,
        Margin = new Padding(0, 3, 7, 3),
    };

    public static bool IsActionError(Exception error) =>
        error is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException or
            Microsoft.AspNetCore.SignalR.HubException or OperationCanceledException;
}
