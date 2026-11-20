using SoulLink;
using SoulLink.Client;

namespace SoulLink.Player;

internal sealed class CatchCheckForm : Form
{
    private readonly TextBox query = new() { Dock = DockStyle.Top, MaxLength = 80 };
    private readonly Label outcome = new() { Dock = DockStyle.Fill, Padding = new Padding(0, 12, 0, 0) };
    private readonly Button check = DashboardTheme.Button("Prüfen");

    public CatchCheckForm(ClientRuntime runtime)
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
                var result = await runtime.CheckCatchAsync(this.query.Text);
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

    public DeathReasonForm(PendingDeath pending)
    {
        RunDialogs.Configure(this, "Todesfall prüfen", new Size(465, 223));
        this.reason.AccessibleName = "Todesursache";
        var details = new Label { Dock = DockStyle.Top, Height = 93, Padding = new Padding(0, 0, 0, 12) };
        var trace = pending.Battle;
        string opponent = trace?.OpponentSpecies is int foe ? RubyNames.Pokemon(foe) : "unbekannt";
        details.Text = $"{RubyNames.Pokemon(pending.Species)} hat 0 KP.\n" +
            $"Gegner: {opponent}{(string.IsNullOrEmpty(trace?.Trainer) ? "" : $" · Trainer {trace.Trainer}")}\n" +
            $"Letzte erfasste Attacke: {RubyNames.Move(trace?.Move) ?? "unbekannt"}";
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 41, FlowDirection = FlowDirection.RightToLeft };
        var confirm = DashboardTheme.Button("Tod bestätigen");
        confirm.DialogResult = DialogResult.Yes;
        var dismiss = DashboardTheme.Button("Als lebend belassen");
        dismiss.DialogResult = DialogResult.No;
        var later = DashboardTheme.Button("Später");
        later.DialogResult = DialogResult.Cancel;
        actions.Controls.AddRange([confirm, dismiss, later]);
        this.CancelButton = later;
        this.Controls.Add(this.reason);
        this.Controls.Add(details);
        this.Controls.Add(actions);
    }

    public string Reason => this.reason.Text.Trim();
}

internal static class RunDialogs
{
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
