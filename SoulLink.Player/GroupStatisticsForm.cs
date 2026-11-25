using System.Text.Json;
using SoulLink;

namespace SoulLink.Player;

internal sealed class GroupStatisticsForm : ToolWindow
{
    private static readonly Font MetricFont = new("Segoe UI Semibold", 20);
    private readonly Func<CancellationToken, Task<GroupStatistics>> load;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Label groupName = Heading("");
    private readonly Label[] metrics = new Label[4];
    private readonly RunMetricChart[] charts =
    [
        new("Fänge", DashboardTheme.Accent, run => run.Catches),
        new("Pokémon-Tode", DashboardTheme.Danger, run => run.Deaths),
        new("Arenen", DashboardTheme.Gold, run => run.Arenas, axisMinimum: 8),
    ];

    private readonly Button refresh = Action("Aktualisieren");
    private readonly Button timeline = Action("Timeline", DashboardTheme.Accent);
    private GroupTimelineForm? openTimeline;
    private readonly DataGridView runs = new();
    private readonly Label details = new() { Dock = DockStyle.Bottom, Height = 36, AutoEllipsis = true };
    private readonly Label feedback = new() { Dock = DockStyle.Bottom, Height = 28, ForeColor = DashboardTheme.Muted };

    public GroupStatisticsForm(Func<CancellationToken, Task<GroupStatistics>> load, Func<CancellationToken, Task<GroupTimeline>> loadTimeline)
        : base("Gruppenstatistik", new Size(1032, 758))
    {
        this.load = load;
        this.Body.Padding = new Padding(5, 4, 5, 0);
        var header = new Panel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(0, 3, 0, 10) };
        this.groupName.Dock = DockStyle.Fill;
        this.groupName.TextAlign = ContentAlignment.MiddleLeft;
        this.refresh.Dock = DockStyle.Right;
        this.timeline.Dock = DockStyle.Right;
        this.timeline.Click += (_, _) =>
        {
            if (this.openTimeline is null || this.openTimeline.IsDisposed)
            {
                this.openTimeline = new GroupTimelineForm(loadTimeline);
                this.openTimeline.Show(this);
            }
            else this.openTimeline.Activate();
        };
        header.Controls.AddRange([this.groupName, this.timeline, this.refresh]);

        var summary = new TableLayoutPanel { Dock = DockStyle.Top, Height = 98, ColumnCount = 4, RowCount = 1 };
        string[] labels = ["Runs · davon abgeschlossen", "Erfasste Fänge", "Tote Pokémon", "Meiste Arenen in einem Run"];
        for (int i = 0; i < labels.Length; i++)
        {
            summary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var card = new ToolCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, i == labels.Length - 1 ? 0 : 12, 10) };
            var metric = new Label { Text = "–", Dock = DockStyle.Top, Height = 37, Font = MetricFont, ForeColor = DashboardTheme.Accent };
            this.metrics[i] = metric;
            card.Controls.Add(new Label { Text = labels[i], Dock = DockStyle.Fill, ForeColor = DashboardTheme.Muted });
            card.Controls.Add(metric);
            summary.Controls.Add(card, i, 0);
        }

        this.ConfigureRunList();
        this.details.Padding = new Padding(0, 10, 0, 0);
        var list = new ToolCard { Dock = DockStyle.Fill };
        list.Controls.Add(this.runs);
        list.Controls.Add(this.details);
        list.Controls.Add(SectionHeading("Run-Liste", 31));

        var trends = this.BuildCharts();
        this.Body.SizeChanged += (_, _) =>
        {
            trends.Height = Math.Clamp(this.Body.ClientSize.Height * 36 / 100,
                this.LogicalToDeviceUnits(156), this.LogicalToDeviceUnits(244));
        };
        this.Body.Controls.Add(list);
        this.Body.Controls.Add(this.feedback);
        this.Body.Controls.Add(trends);
        this.Body.Controls.Add(SectionHeading("Verlauf je Run · ältester zuerst", 33));
        this.Body.Controls.Add(summary);
        this.Body.Controls.Add(header);
        this.Shown += async (_, _) =>
        {
            this.FitWorkingArea();
            await this.ReloadAsync();
        };
        this.refresh.Click += async (_, _) => await this.ReloadAsync();
    }

    private TableLayoutPanel BuildCharts()
    {
        var table = new TableLayoutPanel { Dock = DockStyle.Top, Height = 244, ColumnCount = this.charts.Length, RowCount = 1 };
        string[] labels = ["Fänge", "Pokémon-Tode", "Arena-Fortschritt"];
        for (int i = 0; i < this.charts.Length; i++)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / this.charts.Length));
            var card = new ToolCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, i == this.charts.Length - 1 ? 0 : 12, 16) };
            this.charts[i].Dock = DockStyle.Fill;
            card.Controls.Add(this.charts[i]);
            card.Controls.Add(SectionHeading(labels[i], 26));
            table.Controls.Add(card, i, 0);
        }

        return table;
    }

    private static Label SectionHeading(string text, int height)
    {
        var heading = Heading(text);
        heading.Dock = DockStyle.Top;
        heading.Height = height;
        heading.TextAlign = ContentAlignment.MiddleLeft;
        return heading;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !this.IsDisposed)
        {
            this.lifetime.Cancel();
            this.lifetime.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task ReloadAsync()
    {
        this.refresh.Enabled = false;
        this.feedback.ForeColor = DashboardTheme.Muted;
        this.feedback.Text = "Gruppenstatistik wird geladen …";
        try
        {
            var statistics = await this.load(this.lifetime.Token);
            if (!this.IsDisposed) this.ShowStatistics(statistics);
        }
        catch (Exception error) when (RunDialogs.IsActionError(error) || error is HttpRequestException or JsonException)
        {
            if (!this.IsDisposed && !this.Disposing)
            {
                this.feedback.ForeColor = DashboardTheme.Danger;
                this.feedback.Text = error is HttpRequestException or OperationCanceledException
                    ? "Die Statistik konnte gerade nicht vom Run-Server geladen werden."
                    : error.Message;
            }
        }
        finally
        {
            if (!this.IsDisposed) this.refresh.Enabled = true;
        }
    }

    private void ShowStatistics(GroupStatistics statistics)
    {
        this.groupName.Text = statistics.Group;
        var records = statistics.Runs;
        this.metrics[0].Text = $"{records.Length} / {records.Count(run => run.Phase == RunPhase.Finished)}";
        this.metrics[1].Text = Metric(SumRecorded(records.Select(run => run.Catches)));
        this.metrics[2].Text = Metric(SumRecorded(records.Select(run => run.Deaths)));
        this.metrics[3].Text = Metric(records.Select(run => run.Arenas).DefaultIfEmpty().Max());

        var chronological = records.OrderBy(run => run.Date).ToArray();
        foreach (var chart in this.charts) chart.SetRuns(chronological);

        this.runs.Rows.Clear();
        foreach (var run in records)
        {
            int row = this.runs.Rows.Add(run.Name, run.DiscordArchive ? "Discord" : "SoulLink", RunStatus(run),
                run.Date.LocalDateTime, (object?)run.Catches ?? DBNull.Value, (object?)run.Deaths ?? DBNull.Value, (object?)run.Arenas ?? DBNull.Value);
            this.runs.Rows[row].Tag = run;
        }

        this.ShowRunDetails();
        this.feedback.Text = records.Length == 0
            ? "Für diese Gruppe sind noch keine Runs erfasst."
            : "Fänge ohne Starter. Pokémon-Tode zählen auch Linkpartner. – steht für fehlende Daten.";
    }

    private void ConfigureRunList()
    {
        this.runs.Dock = DockStyle.Fill;
        this.runs.AccessibleName = "Runs der Gruppe";
        this.runs.ReadOnly = true;
        this.runs.AllowUserToAddRows = false;
        this.runs.AllowUserToDeleteRows = false;
        this.runs.AllowUserToResizeRows = false;
        this.runs.RowHeadersVisible = false;
        this.runs.MultiSelect = false;
        this.runs.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        this.runs.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        this.runs.BorderStyle = BorderStyle.None;
        this.runs.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        this.runs.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        this.runs.EnableHeadersVisualStyles = false;
        this.runs.BackgroundColor = DashboardTheme.Surface;
        this.runs.GridColor = DashboardTheme.Border;
        this.runs.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle { BackColor = DashboardTheme.Panel, ForeColor = DashboardTheme.Text };
        this.runs.DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = DashboardTheme.Surface,
            ForeColor = DashboardTheme.Text,
            SelectionBackColor = DashboardTheme.Border,
            SelectionForeColor = DashboardTheme.Text,
            NullValue = "–",
        };
        this.runs.RowTemplate.Height = 30;
        this.runs.Columns.AddRange(Column("Run", 26), Column("Quelle", 11), Column("Status", 16),
            Column("Datum", 13, typeof(DateTime), "d"), Column("Fänge", 9, typeof(int)),
            Column("Tode", 9, typeof(int)), Column("Arenen", 10, typeof(int)));
        this.runs.CurrentCellChanged += (_, _) => this.ShowRunDetails();
    }

    private void ShowRunDetails()
    {
        if (this.runs.CurrentRow?.Tag is not RunStatistics run)
        {
            this.details.Text = "";
            return;
        }

        string dateLabel = run.DiscordArchive ? "Gestartet" : "Angelegt";
        var details = new List<string> { $"{dateLabel} {run.Date.LocalDateTime:g}" };
        if (run.EndedAt is DateTimeOffset end) details.Add($"Beendet {end.LocalDateTime:g}");
        if (!string.IsNullOrWhiteSpace(run.EndReason)) details.Add($"Endgrund: {run.EndReason}");
        this.details.Text = string.Join("  ·  ", details);
    }

    private static DataGridViewTextBoxColumn Column(string title, float weight, Type? type = null, string format = "") =>
        new()
        {
            HeaderText = title,
            FillWeight = weight,
            ValueType = type ?? typeof(string),
            SortMode = DataGridViewColumnSortMode.Automatic,
            DefaultCellStyle = new DataGridViewCellStyle { Format = format },
        };

    private static int? SumRecorded(IEnumerable<int?> values)
    {
        int[] recorded = values.OfType<int>().ToArray();
        return recorded.Length == 0 ? null : recorded.Sum();
    }

    private static string Metric(int? value) => value?.ToString() ?? "–";

    private static string RunStatus(RunStatistics run) => run.Phase switch
    {
        RunPhase.Finished => "Abgeschlossen",
        RunPhase.Active => "Aktiv",
        RunPhase.Paused => "Pausiert",
        RunPhase.Lobby => "Lobby",
        _ => "Offen beim Import",
    };
}
