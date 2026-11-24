namespace SoulLink.Player;

internal class PlayerWindow : Form
{
    private const int CornerRadius = 8;
    private const int SizeMessage = 0x0005;
    private (Size Size, int Dpi)? roundedBounds;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        this.UpdateWindowShape();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        this.UpdateWindowShape();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        this.UpdateWindowShape();
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (message.Msg == SizeMessage)
        {
            this.UpdateWindowShape();
        }
    }

    private void UpdateWindowShape()
    {
        if (!this.IsHandleCreated)
        {
            return;
        }

        if (this.WindowState != FormWindowState.Normal)
        {
            if (this.roundedBounds.HasValue)
            {
                this.roundedBounds = null;
                this.Region = null;
            }

            return;
        }

        var bounds = (this.Size, this.DeviceDpi);
        if (this.roundedBounds == bounds)
        {
            return;
        }

        this.roundedBounds = bounds;
        int radius = this.LogicalToDeviceUnits(CornerRadius);
        // Window regions start at the outer frame, including the caption and borders.
        using var outline = DashboardTheme.Rounded(new Rectangle(Point.Empty, this.Size), radius);
        this.Region = new Region(outline);
    }
}
