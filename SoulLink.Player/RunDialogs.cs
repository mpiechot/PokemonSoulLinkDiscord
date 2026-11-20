using System.Runtime.InteropServices;
using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class CatchCheckForm : Form
{
    private readonly TextBox query = new() { Dock = DockStyle.Top, MaxLength = 80 };
    private readonly Label outcome = new() { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0) };
    private readonly Button check = DashboardTheme.Button("Prüfen");

    public CatchCheckForm(ClientRuntime runtime, Guid runId)
    {
        RunDialogs.Configure(this, "Catch-Check", new Size(410, 190));
        this.query.PlaceholderText = "Deutscher oder englischer Pokémon-Name";
        this.query.AccessibleName = "Pokémon-Name";
        this.check.Dock = DockStyle.Bottom;
        this.AcceptButton = this.check;
        this.Controls.Add(this.outcome);
        this.Controls.Add(this.query);
        this.Controls.Add(this.check);
        this.check.Click += async (_, _) =>
        {
            if (string.IsNullOrWhiteSpace(this.query.Text)) return;
            this.check.Enabled = false;
            try
            {
                var result = await runtime.CheckCatchAsync(runId, this.query.Text);
                if (this.IsDisposed) return;
                this.outcome.ForeColor = result.Pokemon is null ? DashboardTheme.Gold
                    : result.PreviouslyCaught is null ? DashboardTheme.Accent : DashboardTheme.Danger;
                this.outcome.Text = result.Pokemon is null ? "Pokémon unbekannt. Prüfe die Schreibweise."
                    : result.PreviouslyCaught is null ? $"{result.Pokemon} ist nach der Evolutionsfamilien-Regel erlaubt."
                    : $"{result.Pokemon} ist bereits abgedeckt.\n{result.Player} hat {result.PreviouslyCaught} in {result.Area} gefangen.";
            }
            catch (Exception error) when (RunDialogs.IsActionError(error))
            {
                if (!this.IsDisposed) this.outcome.Text = "Der Catch-Check ist gerade nicht verfügbar.";
            }
            finally { if (!this.IsDisposed) this.check.Enabled = true; }
        };
    }
}

internal sealed class DeathReasonForm : Form
{
    private readonly TextBox reason = new() { Dock = DockStyle.Top, MaxLength = 250, PlaceholderText = "Grund (optional)" };
    private readonly Label details = new() { Dock = DockStyle.Top, Height = 94, Padding = new Padding(0, 0, 0, 12) };

    public DeathReasonForm(PendingDeath pending)
    {
        RunDialogs.Configure(this, "Todesfall prüfen", new Size(465, 223));
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
        this.Controls.Add(this.reason);
        this.Controls.Add(this.details);
        this.Controls.Add(actions);
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

    public static void Configure(Form form, string title, Size size)
    {
        form.Text = title;
        form.ClientSize = size;
        form.StartPosition = FormStartPosition.CenterParent;
        form.FormBorderStyle = FormBorderStyle.FixedDialog;
        form.MinimizeBox = false;
        form.MaximizeBox = false;
        form.ShowInTaskbar = false;
        form.BackColor = DashboardTheme.Surface;
        form.ForeColor = DashboardTheme.Text;
        form.Padding = new Padding(15);
    }

    public static bool IsActionError(Exception error) =>
        error is InvalidOperationException or InvalidDataException or IOException or UnauthorizedAccessException or
            Microsoft.AspNetCore.SignalR.HubException or OperationCanceledException;
}
