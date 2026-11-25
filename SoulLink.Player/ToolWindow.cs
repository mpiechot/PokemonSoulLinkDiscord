using System.Runtime.InteropServices;

namespace SoulLink.Player;

internal class ToolWindow : PlayerWindow
{
    private static readonly Font HeadingFont = new("Segoe UI Semibold", 11);
    private static readonly Font TitleFont = new("Segoe UI Semibold", 13);
    private readonly Label caption;
    private readonly bool resizable;

    internal ToolWindow(string title, Size size, bool resizable = false)
    {
        this.resizable = resizable;
        this.Text = title;
        this.ClientSize = size;
        this.FormBorderStyle = FormBorderStyle.None;
        this.StartPosition = FormStartPosition.CenterParent;
        this.ShowInTaskbar = false;
        this.AutoScaleMode = AutoScaleMode.Dpi;
        this.BackColor = DashboardTheme.Background;
        this.ForeColor = DashboardTheme.Text;
        this.Font = new Font("Segoe UI", 10);
        this.Padding = new Padding(16, 0, 16, 16);

        var header = new Panel { Dock = DockStyle.Top, Height = 54 };
        this.caption = Heading(title);
        this.caption.Dock = DockStyle.Fill;
        this.caption.Font = TitleFont;
        this.caption.TextAlign = ContentAlignment.MiddleLeft;
        this.caption.ForeColor = DashboardTheme.Accent;
        this.caption.MouseDown += (_, args) => { if (args.Button == MouseButtons.Left) DragWindow(this.Handle); };
        var close = DashboardTheme.IconButton(DashboardIcon.Close, "Schließen", DashboardTheme.Panel, DashboardTheme.Border, DashboardTheme.Text);
        close.Dock = DockStyle.Right;
        close.Width = 36;
        header.Padding = new Padding(0, 9, 0, 9);
        close.Click += (_, _) => this.Close();
        header.Controls.Add(this.caption);
        header.Controls.Add(close);

        this.Controls.Add(this.Body);
        this.Controls.Add(header);
    }

    internal Panel Body { get; } = new() { Dock = DockStyle.Fill };

    protected void SetTitle(string title) => this.Text = this.caption.Text = title;

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        if (!this.resizable || message.Msg != 0x84 || message.Result != 1) return;
        long packed = message.LParam.ToInt64();
        var point = this.PointToClient(new Point((short)packed, (short)(packed >> 16)));
        int edge = this.LogicalToDeviceUnits(7);
        int horizontal = point.X < edge ? -1 : point.X >= this.Width - edge ? 1 : 0;
        int vertical = point.Y < edge ? -1 : point.Y >= this.Height - edge ? 1 : 0;
        message.Result = (horizontal, vertical) switch
        {
            (-1, -1) => 13, (0, -1) => 12, (1, -1) => 14,
            (-1, 0) => 10, (1, 0) => 11,
            (-1, 1) => 16, (0, 1) => 15, (1, 1) => 17,
            _ => 1,
        };
    }

    protected void FitWorkingArea()
    {
        var area = Screen.FromControl(this).WorkingArea;
        int margin = this.LogicalToDeviceUnits(12);
        this.Size = new Size(Math.Min(this.Width, Math.Max(1, area.Width - (margin * 2))),
            Math.Min(this.Height, Math.Max(1, area.Height - (margin * 2))));
        this.Location = new Point(Math.Clamp(this.Left, area.Left + margin, Math.Max(area.Left + margin, area.Right - this.Width - margin)),
            Math.Clamp(this.Top, area.Top + margin, Math.Max(area.Top + margin, area.Bottom - this.Height - margin)));
    }

    internal static Label Heading(string text) => new() { Text = text, Font = HeadingFont, ForeColor = DashboardTheme.Text };

    internal static Button Action(string text, Color? accent = null)
    {
        var button = DashboardTheme.Button(text);
        button.MinimumSize = new Size(0, 32);
        button.ForeColor = accent ?? DashboardTheme.Text;
        return button;
    }

    internal static string? Prompt(Form owner, string title, string field, int limit, string value = "")
    {
        using var dialog = new ToolWindow(title, new Size(490, 176));
        var input = RunDialogs.TextInput(field, limit);
        input.Dock = DockStyle.Top;
        input.Text = value;
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 40, FlowDirection = FlowDirection.RightToLeft, WrapContents = false };
        var save = Action("Speichern", DashboardTheme.Accent);
        var cancel = Action("Abbrechen");
        save.Enabled = !string.IsNullOrWhiteSpace(value);
        input.TextChanged += (_, _) => save.Enabled = !string.IsNullOrWhiteSpace(input.Text);
        save.Click += (_, _) => dialog.DialogResult = DialogResult.OK;
        cancel.Click += (_, _) => dialog.DialogResult = DialogResult.Cancel;
        actions.Controls.AddRange([save, cancel]);
        var card = new ToolCard { Dock = DockStyle.Fill };
        card.Controls.Add(actions);
        card.Controls.Add(input);
        dialog.Body.Controls.Add(card);
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        dialog.Shown += (_, _) => { input.Focus(); input.SelectAll(); };
        return dialog.ShowDialog(owner) == DialogResult.OK ? input.Text.Trim() : null;
    }

    internal static void Notice(Form? owner, string title, string text) => ShowMessage(owner, title, text, null);

    internal static bool Confirm(Form owner, string title, string text, string confirm) =>
        ShowMessage(owner, title, text, confirm) == DialogResult.OK;

    private static DialogResult ShowMessage(Form? owner, string title, string text, string? confirm)
    {
        using var dialog = new ToolWindow(title, new Size(500, 230));
        if (owner is null) dialog.StartPosition = FormStartPosition.CenterScreen;
        var card = new ToolCard { Dock = DockStyle.Fill };
        var message = new Label { Text = text, Dock = DockStyle.Fill, AutoEllipsis = true };
        var actions = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 37, FlowDirection = FlowDirection.RightToLeft };
        var accept = Action(confirm ?? "Schließen", DashboardTheme.Accent);
        accept.DialogResult = DialogResult.OK;
        actions.Controls.Add(accept);
        if (confirm is not null)
        {
            var cancel = Action("Abbrechen");
            cancel.DialogResult = DialogResult.Cancel;
            actions.Controls.Add(cancel);
            dialog.AcceptButton = dialog.CancelButton = cancel;
            dialog.Shown += (_, _) => cancel.Select();
        }
        else dialog.AcceptButton = dialog.CancelButton = accept;
        card.Controls.Add(message);
        card.Controls.Add(actions);
        dialog.Body.Controls.Add(card);
        return owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
    }

    internal static void DragWindow(nint handle)
    {
        ReleaseCapture();
        SendMessage(handle, 0x00A1, 2, 0);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint window, uint message, nint wParam, nint lParam);
}

internal class ToolCard : Panel
{
    public ToolCard()
    {
        this.DoubleBuffered = true;
        this.ResizeRedraw = true;
        this.BackColor = DashboardTheme.Surface;
        this.Padding = new Padding(14);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(this.Parent?.BackColor ?? DashboardTheme.Background);
        DashboardTheme.Fill(e.Graphics, this.ClientRectangle, this.BackColor, 12);
    }
}

internal sealed class ToolTextStack : Panel
{
    public int Measure(int width)
    {
        int top = 0;
        foreach (Control row in this.Controls)
        {
            int available = Math.Max(1, width - row.Margin.Horizontal);
            int height = row.GetPreferredSize(new Size(available, 0)).Height;
            top += row.Margin.Top;
            row.SetBounds(row.Margin.Left, top, available, height);
            top += height + row.Margin.Bottom;
        }
        return top;
    }

    public void SetRows(IEnumerable<Control> rows)
    {
        this.SuspendLayout();
        foreach (Control row in this.Controls.Cast<Control>().ToArray()) row.Dispose();
        foreach (Control row in rows) this.Controls.Add(row);
        this.ResumeLayout();
    }
}
