using System.Text.Json;
using SoulLink;

namespace SoulLink.Player;

internal sealed class GroupTimelineForm : ToolWindow
{
    private readonly TrackBar progress = new() { Dock = DockStyle.Fill, Minimum = 0, Maximum = 1, TickStyle = TickStyle.None, Enabled = false, AccessibleName = "Ereignisschritt" };
    private readonly TimelineLanes lanes = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { Dock = DockStyle.Top, Height = 36, TextAlign = ContentAlignment.MiddleLeft, ForeColor = DashboardTheme.Muted };
    private readonly Button play = Action("Abspielen", DashboardTheme.Accent);
    private readonly Button back = Action("Zurück");
    private readonly Button next = Action("Weiter");
    private readonly Button speed = Action("1×");
    private readonly System.Windows.Forms.Timer animation = new() { Interval = 440 };
    private readonly CancellationTokenSource lifetime = new();
    private string groupName = "";

    public GroupTimelineForm(Func<CancellationToken, Task<GroupTimeline>> load) : base("Run-Timeline", new Size(1032, 715))
    {
        this.play.Enabled = this.back.Enabled = this.next.Enabled = this.speed.Enabled = false;
        this.Body.Padding = new Padding(5, 5, 5, 0);
        var toolbar = new Panel { Dock = DockStyle.Top, Height = 42 };
        var actions = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Left, WrapContents = false };
        actions.Controls.AddRange([this.play, this.back, this.next, this.speed]);
        toolbar.Controls.Add(this.progress);
        toolbar.Controls.Add(actions);
        this.Body.Controls.Add(this.lanes);
        this.Body.Controls.Add(this.status);
        this.Body.Controls.Add(toolbar);

        this.play.Click += (_, _) =>
        {
            if (this.animation.Enabled) this.Pause();
            else
            {
                if (this.progress.Value == this.progress.Maximum) this.progress.Value = 0;
                this.play.Text = "Pause";
                this.animation.Start();
            }
        };
        this.back.Click += (_, _) => this.Seek(this.progress.Value - 1);
        this.next.Click += (_, _) => this.Seek(this.progress.Value + 1);
        this.speed.Click += (_, _) =>
        {
            this.animation.Interval = this.animation.Interval switch { 440 => 220, 220 => 110, _ => 440 };
            this.speed.Text = $"{440 / this.animation.Interval}×";
        };
        this.animation.Tick += (_, _) =>
        {
            if (this.progress.Value < this.progress.Maximum) this.progress.Value++;
            if (this.progress.Value == this.progress.Maximum) this.Pause();
        };
        this.progress.ValueChanged += (_, _) => this.ShowStep();
        this.progress.Scroll += (_, _) => this.Pause();
        this.lanes.SeekRequested += this.Seek;
        this.Shown += async (_, _) =>
        {
            this.FitWorkingArea();
            await this.LoadAsync(load);
        };
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !this.IsDisposed)
        {
            this.animation.Dispose();
            this.lifetime.Cancel();
            this.lifetime.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task LoadAsync(Func<CancellationToken, Task<GroupTimeline>> load)
    {
        this.status.Text = "Ereignisse werden geladen …";
        try
        {
            var timeline = await load(this.lifetime.Token);
            if (this.IsDisposed) return;
            this.lanes.SetRuns(timeline.Runs);
            int events = this.lanes.StepCount;
            if (events == 0)
            {
                this.status.Text = $"Für {timeline.Group} sind noch keine Ereignisse erfasst.";
                return;
            }

            this.groupName = timeline.Group;
            this.progress.Maximum = events;
            this.progress.Enabled = this.play.Enabled = this.speed.Enabled = true;
            this.ShowStep();
        }
        catch (Exception error) when (RunDialogs.IsActionError(error) || error is HttpRequestException or JsonException)
        {
            if (!this.IsDisposed && !this.Disposing)
            {
                this.status.ForeColor = DashboardTheme.Danger;
                this.status.Text = error is HttpRequestException or OperationCanceledException
                    ? "Die Timeline konnte gerade nicht vom Run-Server geladen werden." : error.Message;
            }
        }
    }

    private void Seek(int step)
    {
        this.Pause();
        this.progress.Value = Math.Clamp(step, this.progress.Minimum, this.progress.Maximum);
    }

    private void Pause()
    {
        this.animation.Stop();
        this.play.Text = "Abspielen";
    }

    private void ShowStep()
    {
        int step = this.progress.Value;
        this.lanes.ShowStep(step);
        this.back.Enabled = step > 0;
        this.next.Enabled = step < this.progress.Maximum;
        this.status.Text = $"{this.groupName} · Ereignisschritt {step} / {this.progress.Maximum} · Ein Ereignis je Run und Schritt";
    }
}

internal sealed class TimelineLanes : DashboardScrollView
{
    private readonly ToolTip hint = new();
    private RunTimeline[] runs = [];
    private int step;
    private (int Run, int Event)? hovered;

    public TimelineLanes()
    {
        this.BackColor = DashboardTheme.Background;
        this.AccessibleName = "Parallele Run-Timelines";
    }

    public int StepCount { get; private set; }

    public event Action<int>? SeekRequested;

    internal void SetRuns(RunTimeline[] timeline)
    {
        this.runs = timeline;
        this.StepCount = this.runs.Select(run => run.Events.Length).DefaultIfEmpty().Max();
        this.ContentSize = new Size(0, this.runs.Length * this.LaneHeight);
    }

    internal void ShowStep(int step)
    {
        this.step = step;
        this.AccessibleDescription = string.Join(Environment.NewLine, this.runs.Select(run => $"{run.Name} · {CurrentText(run, step)}"));
        this.Invalidate();
    }

    protected override void PaintViewport(PaintEventArgs e)
    {
        int laneHeight = this.LaneHeight;
        int first = this.ScrollPosition.Y / laneHeight;
        int last = Math.Min(this.runs.Length, first + this.Viewport.Height / laneHeight + 2);
        for (int lane = first; lane < last; lane++) this.PaintLane(e.Graphics, lane);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hit = this.EventAt(e.Location);
        if (hit == this.hovered) return;
        this.HideHint();
        if (hit is not { } point) return;
        this.hovered = point;
        var run = this.runs[point.Run];
        this.Cursor = Cursors.Hand;
        this.hint.Show($"{run.Name}\n{EventText(run.Events[point.Event])}", this, e.X + 14, e.Y + 16, 10000);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        base.OnMouseClick(e);
        if (e.Button == MouseButtons.Left && this.EventAt(e.Location) is { } point) this.SeekRequested?.Invoke(point.Event + 1);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        this.HideHint();
    }

    protected override void OnScrollPositionChanged()
    {
        base.OnScrollPositionChanged();
        this.HideHint();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) this.hint.Dispose();
        base.Dispose(disposing);
    }

    private int LaneHeight => this.LogicalToDeviceUnits(104);
    private int LeftEdge => this.LogicalToDeviceUnits(18);
    private int TrackWidth => Math.Max(1, this.Viewport.Width - (2 * this.LeftEdge));
    private int TrackY(int lane) => (lane * this.LaneHeight) - this.ScrollPosition.Y + this.LogicalToDeviceUnits(50);
    private float TrackX(int index) => this.LeftEdge + ((float)index / Math.Max(1, this.StepCount - 1) * this.TrackWidth);

    private void PaintLane(Graphics canvas, int lane)
    {
        var run = this.runs[lane];
        int y = this.TrackY(lane);
        int top = y - this.LogicalToDeviceUnits(50);
        DashboardTheme.Fill(canvas, new Rectangle(0, top, this.Viewport.Width, this.LaneHeight - this.LogicalToDeviceUnits(10)), DashboardTheme.Surface, 12);
        var title = new Rectangle(this.LeftEdge, top + this.LogicalToDeviceUnits(8), Math.Max(1, this.TrackWidth - this.LogicalToDeviceUnits(100)), this.LogicalToDeviceUnits(24));
        DrawText($"{run.Name} · {(run.DiscordArchive ? "Discord" : "SoulLink")}", title, DashboardTheme.Text);
        var counter = new Rectangle(this.LeftEdge, title.Top, this.TrackWidth, title.Height);
        TextRenderer.DrawText(canvas, $"{Math.Min(this.step, run.Events.Length)} / {run.Events.Length}", this.Font, counter, DashboardTheme.Muted,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

        if (run.Events.Length > 0)
        {
            float end = this.TrackX(run.Events.Length - 1);
            using var track = new Pen(DashboardTheme.Border, this.LogicalToDeviceUnits(2));
            canvas.DrawLine(track, this.LeftEdge, y, end, y);
            int current = Math.Min(this.step, run.Events.Length) - 1;
            if (current >= 0)
            {
                using var played = new Pen(DashboardTheme.Muted, this.LogicalToDeviceUnits(2));
                canvas.DrawLine(played, this.LeftEdge, y, this.TrackX(current), y);
            }

            float radius = this.LogicalToDeviceUnits(4);
            using var dot = new SolidBrush(DashboardTheme.Border);
            for (int i = 0; i < run.Events.Length; i++)
            {
                dot.Color = i <= current ? EventColor(run.Events[i].Kind) : DashboardTheme.Border;
                canvas.FillEllipse(dot, this.TrackX(i) - radius, y - radius, radius * 2, radius * 2);
            }

            if (current >= 0)
            {
                using var ring = new Pen(DashboardTheme.Text);
                float x = this.TrackX(current);
                float outline = radius + this.LogicalToDeviceUnits(3);
                canvas.DrawEllipse(ring, x - outline, y - outline, outline * 2, outline * 2);
            }
        }

        DrawText(CurrentText(run, this.step), new Rectangle(this.LeftEdge, y + this.LogicalToDeviceUnits(12), this.TrackWidth, this.LogicalToDeviceUnits(24)), DashboardTheme.Muted);

        void DrawText(string text, Rectangle bounds, Color color) => TextRenderer.DrawText(canvas, text, this.Font, bounds, color,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }

    private (int Run, int Event)? EventAt(Point point)
    {
        if (!this.Viewport.Contains(point) || this.StepCount == 0) return null;
        int lane = (point.Y + this.ScrollPosition.Y) / this.LaneHeight;
        int hitRadius = this.LogicalToDeviceUnits(10);
        if (lane >= this.runs.Length || Math.Abs(point.Y - this.TrackY(lane)) > hitRadius || this.runs[lane].Events.Length == 0) return null;
        int index = (int)Math.Round((point.X - this.LeftEdge) * (this.StepCount - 1d) / this.TrackWidth);
        index = Math.Clamp(index, 0, this.runs[lane].Events.Length - 1);
        return Math.Abs(point.X - this.TrackX(index)) <= hitRadius ? (lane, index) : null;
    }

    private void HideHint()
    {
        if (!this.hovered.HasValue) return;
        this.hovered = null;
        this.hint.Hide(this);
        this.Cursor = Cursors.Default;
    }

    private static string CurrentText(RunTimeline run, int step) =>
        step == 0 || run.Events.Length == 0 ? "Bereit zur Wiedergabe" : EventText(run.Events[Math.Min(step, run.Events.Length) - 1]);

    private static string EventText(RunTimelineEvent item) =>
        $"{(item.At is DateTimeOffset at ? at.LocalDateTime.ToString("g") : "Zeitpunkt unbekannt")} · {item.Text}";

    private static Color EventColor(TimelineEventKind kind) => kind switch
    {
        TimelineEventKind.Catch => DashboardTheme.Accent,
        TimelineEventKind.Death => DashboardTheme.Danger,
        TimelineEventKind.Arena => DashboardTheme.Gold,
        TimelineEventKind.Start or TimelineEventKind.End => DashboardTheme.Text,
        _ => Color.LightSkyBlue,
    };
}
