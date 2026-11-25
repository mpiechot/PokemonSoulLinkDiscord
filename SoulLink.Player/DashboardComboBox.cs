using System.Drawing.Drawing2D;

namespace SoulLink.Player;

internal sealed class DashboardComboBox : ComboBox
{
    private readonly Func<object, Color>? swatch;

    public DashboardComboBox(Func<object, Color>? swatch = null)
    {
        this.swatch = swatch;
        this.BackColor = DashboardTheme.Background;
        this.ForeColor = DashboardTheme.Text;
        this.FlatStyle = FlatStyle.Flat;
        this.DropDownStyle = ComboBoxStyle.DropDownList;
        this.DrawMode = DrawMode.OwnerDrawFixed;
        this.ItemHeight = 24;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        bool selected = e.State.HasFlag(DrawItemState.Selected);
        using var background = new SolidBrush(selected ? DashboardTheme.Panel : DashboardTheme.Background);
        e.Graphics.FillRectangle(background, e.Bounds);
        if (e.Index >= 0 && e.Index < this.Items.Count && this.Items[e.Index] is { } item)
            this.DrawOption(e.Graphics, e.Bounds, item);
        e.DrawFocusRectangle();
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        const int paint = 0x000F, print = 0x0317, printClient = 0x0318;
        if (message.Msg is not (paint or print or printClient) || this.ClientSize.Width <= 1 || this.ClientSize.Height <= 1 ||
            (message.Msg != paint && message.WParam == 0)) return;
        using var graphics = message.Msg == paint ? Graphics.FromHwnd(this.Handle) : Graphics.FromHdc(message.WParam);
        var bounds = this.ClientRectangle;
        using var background = new SolidBrush(this.Parent?.BackColor ?? DashboardTheme.Surface);
        graphics.FillRectangle(background, bounds);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        DashboardTheme.Fill(graphics, bounds, this.Enabled ? DashboardTheme.Background : DashboardTheme.Surface, 6);
        using var outline = DashboardTheme.Rounded(new Rectangle(0, 0, bounds.Width - 1, bounds.Height - 1), 6);
        using var border = new Pen(this.Focused ? DashboardTheme.Accent : DashboardTheme.Border);
        graphics.DrawPath(border, outline);

        int arrowSpace = this.LogicalToDeviceUnits(28);
        if (this.SelectedItem is { } item)
            this.DrawOption(graphics, new Rectangle(7, 0, Math.Max(1, bounds.Width - arrowSpace - 7), bounds.Height), item);
        int arrowX = bounds.Right - arrowSpace / 2, arrowY = bounds.Height / 2;
        int wing = this.LogicalToDeviceUnits(4);
        using var arrow = new Pen(this.Enabled ? DashboardTheme.Muted : DashboardTheme.Border, 1.4f);
        graphics.DrawLines(arrow, new[] { new Point(arrowX - wing, arrowY - 2), new Point(arrowX, arrowY + 2), new Point(arrowX + wing, arrowY - 2) });
    }

    private void DrawOption(Graphics graphics, Rectangle bounds, object item)
    {
        if (this.swatch is { } chooseColor)
        {
            int chip = this.LogicalToDeviceUnits(13);
            using var fill = new SolidBrush(chooseColor(item));
            graphics.FillRectangle(fill, bounds.Left + 3, bounds.Top + (bounds.Height - chip) / 2, chip, chip);
            bounds.X += chip + 10;
            bounds.Width = Math.Max(1, bounds.Width - chip - 10);
        }
        TextRenderer.DrawText(graphics, this.GetItemText(item), this.Font, bounds, this.Enabled ? DashboardTheme.Text : DashboardTheme.Muted,
            TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
    }
}
