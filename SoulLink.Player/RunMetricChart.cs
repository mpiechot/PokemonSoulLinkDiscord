using System.Drawing.Drawing2D;
using SoulLink;

namespace SoulLink.Player;

internal sealed class RunMetricChart : Control
{
    private const string KeyboardHint = "Mit den Pfeiltasten zwischen den erfassten Run-Werten wechseln.";
    private readonly string metric;
    private readonly Color accent;
    private readonly Func<RunStatistics, int?> valueOf;
    private readonly int axisMinimum;
    private readonly ToolTip tip = new();
    private Sample[] samples = [];
    private PointF?[] points = [];
    private Rectangle plot;
    private int axisSteps;
    private int axisStep;
    private int active = -1;

    public RunMetricChart(string metric, Color accent, Func<RunStatistics, int?> valueOf, int axisMinimum = 0)
    {
        this.metric = metric;
        this.accent = accent;
        this.valueOf = valueOf;
        this.axisMinimum = axisMinimum;
        this.AccessibleName = metric + " je Run";
        this.AccessibleDescription = KeyboardHint;
        this.BackColor = DashboardTheme.Surface;
        this.DoubleBuffered = true;
        this.SetStyle(ControlStyles.Selectable, true);
        this.TabStop = true;
    }

    internal void SetRuns(RunStatistics[] chronological)
    {
        this.ClearPoint();
        this.samples = chronological.Select(run => new Sample(run, this.valueOf(run))).ToArray();
        this.LayoutPoints();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        this.ClearPoint();
        this.LayoutPoints();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (!this.points.Any(point => point.HasValue))
        {
            TextRenderer.DrawText(e.Graphics, "Keine Werte erfasst", this.Font, this.ClientRectangle, DashboardTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            return;
        }

        this.DrawAxes(e.Graphics);
        var canvas = e.Graphics;
        canvas.SmoothingMode = SmoothingMode.AntiAlias;
        using var line = new Pen(this.accent, this.LogicalToDeviceUnits(2)) { LineJoin = LineJoin.Round };
        using var dot = new SolidBrush(this.accent);
        float radius = this.LogicalToDeviceUnits(4);
        for (int i = 0; i < this.points.Length; i++)
        {
            if (this.points[i] is not { } point) continue;
            if (i > 0 && this.points[i - 1] is { } previous) canvas.DrawLine(line, previous, point);
            canvas.FillEllipse(dot, point.X - radius, point.Y - radius, radius * 2, radius * 2);
        }

        if (this.active >= 0 && this.points[this.active] is { } selected)
        {
            using var highlight = new Pen(DashboardTheme.Text, this.LogicalToDeviceUnits(1));
            float ring = radius + this.LogicalToDeviceUnits(3);
            canvas.DrawEllipse(highlight, selected.X - ring, selected.Y - ring, ring * 2, ring * 2);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int index = -1;
        int radius = this.LogicalToDeviceUnits(11);
        float nearest = radius * radius + 1;
        for (int i = 0; i < this.points.Length; i++)
        {
            if (this.points[i] is not { } point) continue;
            float dx = point.X - e.X;
            float dy = point.Y - e.Y;
            float distance = (dx * dx) + (dy * dy);
            if (distance >= nearest) continue;
            nearest = distance;
            index = i;
        }

        if (index >= 0) this.ShowPoint(index);
        else this.ClearPoint();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        this.ClearPoint();
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is not (Keys.Left or Keys.Right)) return;
        int direction = e.KeyCode == Keys.Left ? -1 : 1;
        int start = this.active < 0 && direction < 0 ? this.points.Length : this.active;
        for (int i = start + direction; i >= 0 && i < this.points.Length; i += direction)
        {
            if (!this.points[i].HasValue) continue;
            this.ShowPoint(i);
            break;
        }

        e.Handled = true;
    }

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        this.ClearPoint();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) this.tip.Dispose();
        base.Dispose(disposing);
    }

    private void LayoutPoints()
    {
        int maximum = Math.Max(this.axisMinimum, this.samples.Select(sample => sample.Value.GetValueOrDefault()).DefaultIfEmpty().Max());
        this.axisStep = Math.Max(1, (int)Math.Ceiling(maximum / 4d));
        this.axisSteps = Math.Max(1, (int)Math.Ceiling(maximum / (double)this.axisStep));
        long ceiling = (long)this.axisSteps * this.axisStep;
        int labelWidth = TextRenderer.MeasureText(ceiling.ToString(), this.Font).Width;
        int left = labelWidth + this.LogicalToDeviceUnits(4);
        int top = this.LogicalToDeviceUnits(11);
        this.plot = new Rectangle(left, top,
            Math.Max(1, this.ClientSize.Width - left - this.LogicalToDeviceUnits(12)),
            Math.Max(1, this.ClientSize.Height - top - this.LogicalToDeviceUnits(25)));
        this.points = new PointF?[this.samples.Length];
        for (int i = 0; i < this.samples.Length; i++)
        {
            if (this.samples[i].Value is int value)
                this.points[i] = new PointF(this.RunX(i), this.plot.Bottom - (float)(value / (double)ceiling * this.plot.Height));
        }

        this.Invalidate();
    }

    private float RunX(int index) => this.samples.Length == 1
        ? this.plot.Left + (this.plot.Width / 2f)
        : this.plot.Left + ((float)index / (this.samples.Length - 1) * this.plot.Width);

    private void DrawAxes(Graphics canvas)
    {
        using var grid = new Pen(DashboardTheme.Border);
        for (int i = 0; i <= this.axisSteps; i++)
        {
            int y = this.plot.Bottom - (i * this.plot.Height / this.axisSteps);
            canvas.DrawLine(grid, this.plot.Left, y, this.plot.Right, y);
            var label = new Rectangle(0, y - (this.Font.Height / 2), this.plot.Left - this.LogicalToDeviceUnits(5), this.Font.Height);
            TextRenderer.DrawText(canvas, ((long)i * this.axisStep).ToString(), this.Font, label, DashboardTheme.Muted,
                TextFormatFlags.Right | TextFormatFlags.NoPadding);
        }

        int labelWidth = TextRenderer.MeasureText(this.samples.Length.ToString(), this.Font).Width + this.LogicalToDeviceUnits(7);
        int visible = Math.Max(1, this.plot.Width / labelWidth);
        int stride = Math.Max(1, (this.samples.Length + visible - 1) / visible);
        for (int i = 0; i < this.samples.Length; i += stride)
        {
            var label = new Rectangle((int)this.RunX(i) - (labelWidth / 2), this.plot.Bottom + this.LogicalToDeviceUnits(5),
                labelWidth, this.Font.Height);
            TextRenderer.DrawText(canvas, (i + 1).ToString(), this.Font, label, DashboardTheme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding);
        }
    }

    private void ShowPoint(int index)
    {
        if (this.active == index || this.points[index] is not { } point) return;
        this.active = index;
        this.Cursor = Cursors.Cross;
        var sample = this.samples[index];
        string text = $"{sample.Run.Name}\n{sample.Run.Date.LocalDateTime:d} · {(sample.Run.DiscordArchive ? "Discord" : "SoulLink")}\n{this.metric}  {sample.Value}";
        this.AccessibleDescription = text;
        this.tip.Hide(this);
        this.tip.Show(text, this, (int)point.X + this.LogicalToDeviceUnits(12), (int)point.Y + this.LogicalToDeviceUnits(13), 10000);
        this.Invalidate();
    }

    private void ClearPoint()
    {
        if (this.active < 0) return;
        this.active = -1;
        this.Cursor = Cursors.Default;
        this.AccessibleDescription = KeyboardHint;
        this.tip.Hide(this);
        this.Invalidate();
    }

    private sealed record Sample(RunStatistics Run, int? Value);
}
