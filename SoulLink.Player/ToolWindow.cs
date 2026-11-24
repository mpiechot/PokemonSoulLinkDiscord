using System.Runtime.InteropServices;

namespace SoulLink.Player;

internal class ToolWindow : PlayerWindow
{
    private static readonly Font HeadingFont = new("Segoe UI Semibold", 11);
    private static readonly Font TitleFont = new("Segoe UI Semibold", 13);

    protected ToolWindow(string title, Size size)
    {
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
        var caption = Heading(title);
        caption.Dock = DockStyle.Fill;
        caption.Font = TitleFont;
        caption.TextAlign = ContentAlignment.MiddleLeft;
        caption.ForeColor = DashboardTheme.Accent;
        caption.MouseDown += (_, args) => { if (args.Button == MouseButtons.Left) DragWindow(this.Handle); };
        var close = DashboardTheme.IconButton(DashboardIcon.Close, "Schließen", DashboardTheme.Panel, DashboardTheme.Border, DashboardTheme.Text);
        close.Dock = DockStyle.Right;
        close.Width = 36;
        header.Padding = new Padding(0, 9, 0, 9);
        close.Click += (_, _) => this.Close();
        header.Controls.Add(caption);
        header.Controls.Add(close);

        this.Controls.Add(this.Body);
        this.Controls.Add(header);
    }

    protected Panel Body { get; } = new() { Dock = DockStyle.Fill };

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
