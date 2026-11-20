using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SoulLink.Player;

internal class DashboardScrollView : Control
{
    private static readonly Color ThumbColor = Color.FromArgb(89, 113, 140);
    private static readonly Color HoverColor = Color.FromArgb(132, 159, 190);

    private Size contentSize;
    private Point scrollPosition;
    private bool? draggingVertical;
    private int dragOffset;
    private Point pointer = new(-1, -1);

    public DashboardScrollView()
    {
        this.DoubleBuffered = true;
        this.TabStop = true;
        this.BackColor = DashboardTheme.Surface;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal Size ContentSize
    {
        get => this.contentSize;
        set
        {
            if (this.contentSize == value) return;
            this.contentSize = value;
            this.UpdateScroll(this.scrollPosition);
            this.Invalidate();
        }
    }

    internal Point ScrollPosition => this.scrollPosition;

    internal Rectangle Viewport
    {
        get
        {
            int width = this.ClientSize.Width;
            int height = this.ClientSize.Height;
            bool vertical = this.contentSize.Height > height;
            bool horizontal = this.contentSize.Width > width - (vertical ? this.RailSize : 0);
            vertical |= this.contentSize.Height > height - (horizontal ? this.RailSize : 0);
            return new Rectangle(0, 0, Math.Max(0, width - (vertical ? this.RailSize : 0)),
                Math.Max(0, height - (horizontal ? this.RailSize : 0)));
        }
    }

    protected int RailSize => Math.Max(11, 11 * this.DeviceDpi / 96);

    internal Rectangle VerticalRail => this.Rail(vertical: true);

    internal Rectangle HorizontalRail => this.Rail(vertical: false);

    protected sealed override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var canvas = e.Graphics;
        GraphicsState state = canvas.Save();
        canvas.SetClip(this.Viewport, CombineMode.Intersect);
        this.PaintViewport(e);
        canvas.Restore(state);
        this.DrawRail(canvas, vertical: true);
        this.DrawRail(canvas, vertical: false);
        if (!this.VerticalRail.IsEmpty && !this.HorizontalRail.IsEmpty)
            DashboardTheme.Fill(canvas, new Rectangle(this.Viewport.Right, this.Viewport.Bottom, this.RailSize, this.RailSize), DashboardTheme.Background, 2);
    }

    protected virtual void PaintViewport(PaintEventArgs e) { }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        this.UpdateScroll(this.scrollPosition);
        this.Invalidate();
    }

    protected virtual void OnScrollPositionChanged() => this.Invalidate();

    protected void ScrollWheel(MouseEventArgs args)
    {
        bool horizontal = (ModifierKeys & Keys.Shift) != 0 || this.VerticalRail.IsEmpty;
        if ((horizontal ? this.HorizontalRail : this.VerticalRail).IsEmpty) return;
        int step = SystemInformation.MouseWheelScrollLines < 0 ? this.PageLength(!horizontal) :
            SystemInformation.MouseWheelScrollLines * Math.Max(16, this.Font.Height);
        int amount = (int)Math.Round(args.Delta * step / 120d);
        this.ScrollBy(horizontal ? -amount : 0, horizontal ? 0 : -amount);
        if (args is HandledMouseEventArgs handled) handled.Handled = true;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e is not HandledMouseEventArgs { Handled: true }) this.ScrollWheel(e);
    }

    protected override bool IsInputKey(Keys keyData) => IsScrollKey(keyData & Keys.KeyCode) || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled) this.ScrollKey(e);
    }

    protected void ScrollKey(KeyEventArgs e)
    {
        if (!IsScrollKey(e.KeyCode) || e.Alt) return;
        int step = Math.Max(20, this.Font.Height);
        switch (e.KeyCode)
        {
            case Keys.Left: this.ScrollBy(-step, 0); break;
            case Keys.Right: this.ScrollBy(step, 0); break;
            case Keys.Up: this.ScrollBy(0, -step); break;
            case Keys.Down: this.ScrollBy(0, step); break;
            case Keys.PageUp: this.ScrollBy(0, -this.PageLength(true)); break;
            case Keys.PageDown: this.ScrollBy(0, this.PageLength(true)); break;
            case Keys.Home: this.UpdateScroll(new Point(this.scrollPosition.X, 0)); break;
            case Keys.End: this.UpdateScroll(new Point(this.scrollPosition.X, this.contentSize.Height)); break;
        }
        e.SuppressKeyPress = true;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        bool vertical = this.VerticalRail.Contains(e.Location);
        if (!vertical && !this.HorizontalRail.Contains(e.Location)) return;
        this.Focus();
        var thumb = this.Thumb(vertical);
        if (!thumb.Contains(e.Location))
        {
            int direction = Axis(e.Location, vertical) < Start(thumb, vertical) ? -1 : 1;
            int amount = direction * this.PageLength(vertical);
            this.ScrollBy(vertical ? 0 : amount, vertical ? amount : 0);
            return;
        }
        this.draggingVertical = vertical;
        this.dragOffset = Axis(e.Location, vertical) - Start(thumb, vertical);
        this.Capture = true;
        this.Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        bool hovered = this.VerticalRail.Contains(this.pointer) || this.HorizontalRail.Contains(this.pointer);
        this.pointer = e.Location;
        if (this.draggingVertical is bool vertical)
        {
            var track = this.Rail(vertical);
            int movable = Length(track, vertical) - Length(this.Thumb(vertical), vertical);
            if (movable > 0)
            {
                int length = (vertical ? this.contentSize.Height : this.contentSize.Width) - Length(this.Viewport, vertical);
                int distance = Axis(e.Location, vertical) - this.dragOffset - Start(track, vertical);
                int offset = (int)Math.Round(length * (Math.Clamp(distance, 0, movable) / (double)movable));
                this.UpdateScroll(vertical ? new Point(this.scrollPosition.X, offset) : new Point(offset, this.scrollPosition.Y));
            }
        }
        else if (hovered || this.VerticalRail.Contains(this.pointer) || this.HorizontalRail.Contains(this.pointer))
        {
            this.Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        this.pointer = new Point(-1, -1);
        this.Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (this.draggingVertical is not null && e.Button == MouseButtons.Left) this.Capture = false;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        base.OnMouseCaptureChanged(e);
        if (this.Capture) return;
        this.draggingVertical = null;
        this.Invalidate();
    }

    private static bool IsScrollKey(Keys key) =>
        key is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End;

    private int PageLength(bool vertical) => Math.Max(20, Length(this.Viewport, vertical) - this.Font.Height);

    private void ScrollBy(int x, int y) => this.UpdateScroll(new Point(this.scrollPosition.X + x, this.scrollPosition.Y + y));

    private void UpdateScroll(Point point)
    {
        var viewport = this.Viewport;
        var next = new Point(Math.Clamp(point.X, 0, Math.Max(0, this.contentSize.Width - viewport.Width)),
            Math.Clamp(point.Y, 0, Math.Max(0, this.contentSize.Height - viewport.Height)));
        if (next == this.scrollPosition) return;
        this.scrollPosition = next;
        this.OnScrollPositionChanged();
    }

    private Rectangle Rail(bool vertical)
    {
        var view = this.Viewport;
        if (vertical && view.Right < this.ClientSize.Width && view.Height > 0)
            return new Rectangle(view.Right, 0, this.ClientSize.Width - view.Right, view.Height);
        if (!vertical && view.Bottom < this.ClientSize.Height && view.Width > 0)
            return new Rectangle(0, view.Bottom, view.Width, this.ClientSize.Height - view.Bottom);
        return Rectangle.Empty;
    }

    private Rectangle Thumb(bool vertical)
    {
        var rail = this.Rail(vertical);
        int available = Length(rail, vertical);
        int content = vertical ? this.contentSize.Height : this.contentSize.Width;
        if (available == 0 || content <= available) return Rectangle.Empty;
        int minimum = Math.Min(available, 2 * this.RailSize);
        int length = Math.Clamp((int)Math.Round(available * (available / (double)content)), minimum, available);
        int offset = (int)Math.Round((available - length) * (Axis(this.scrollPosition, vertical) / (double)(content - available)));
        return vertical ? new Rectangle(rail.X, rail.Y + offset, rail.Width, length) : new Rectangle(rail.X + offset, rail.Y, length, rail.Height);
    }

    private void DrawRail(Graphics canvas, bool vertical)
    {
        var track = this.Rail(vertical);
        if (track.IsEmpty) return;
        TrackBackground(canvas, track);
        var thumb = this.Thumb(vertical);
        var body = Rectangle.Inflate(thumb, vertical ? -3 : -1, vertical ? -1 : -3);
        if (body.Width > 0 && body.Height > 0)
            DashboardTheme.Fill(canvas, body, this.draggingVertical == vertical || track.Contains(this.pointer) ? HoverColor : ThumbColor, 5);
    }

    private static void TrackBackground(Graphics canvas, Rectangle track)
    {
        using var brush = new SolidBrush(DashboardTheme.Background);
        canvas.FillRectangle(brush, track);
    }

    private static int Axis(Point point, bool vertical) => vertical ? point.Y : point.X;

    private static int Start(Rectangle rectangle, bool vertical) => vertical ? rectangle.Y : rectangle.X;

    private static int Length(Rectangle rectangle, bool vertical) => vertical ? rectangle.Height : rectangle.Width;
}

internal sealed class DashboardScrollHost : DashboardScrollView
{
    private readonly Panel clip = new();
    private readonly Control document;
    private readonly Func<int, int> measure;

    public DashboardScrollHost(Control document, Func<int, int> measure)
    {
        this.document = document;
        this.measure = measure;
        this.document.Dock = DockStyle.None;
        this.BackColor = document.BackColor;
        this.clip.BackColor = this.BackColor;
        this.Controls.Add(this.clip);
        this.clip.Controls.Add(document);
        this.document.MouseWheel += (_, e) => this.ScrollWheel(e);
        this.clip.MouseWheel += (_, e) => this.ScrollWheel(e);
        this.document.TextChanged += (_, _) => this.ResizeDocument();
        this.document.FontChanged += (_, _) => this.ResizeDocument();
        this.document.KeyDown += (_, e) =>
        {
            if (!e.Shift && e.KeyCode is Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End)
                this.ScrollKey(e);
        };
    }

    public static DashboardScrollHost ForText(RichTextBox document)
    {
        document.ScrollBars = RichTextBoxScrollBars.None;
        return new DashboardScrollHost(document, width =>
        {
            document.Width = Math.Max(1, width);
            int lines = document.GetLineFromCharIndex(document.TextLength) + 1;
            return Math.Max(1, lines) * document.Font.Height + 6;
        });
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        this.ResizeDocument();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        this.ResizeDocument();
    }

    protected override void OnScrollPositionChanged()
    {
        base.OnScrollPositionChanged();
        this.PositionDocument();
    }

    private void ResizeDocument()
    {
        if (this.document is null || !this.IsHandleCreated || this.ClientSize.Width < 1) return;
        int height = this.measure(this.ClientSize.Width);
        if (height > this.ClientSize.Height) height = this.measure(Math.Max(1, this.ClientSize.Width - this.RailSize));
        this.ContentSize = new Size(0, height);
        this.PositionDocument();
        this.Invalidate();
    }

    private void PositionDocument()
    {
        this.clip.Bounds = this.Viewport;
        this.document.SetBounds(0, -this.ScrollPosition.Y, this.Viewport.Width, Math.Max(this.Viewport.Height, this.ContentSize.Height));
    }
}
