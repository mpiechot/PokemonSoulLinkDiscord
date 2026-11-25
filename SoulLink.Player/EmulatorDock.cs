using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Automation;

namespace SoulLink.Player;

/// <summary>Hosts only the main window of the mGBA process started by this player.</summary>
internal sealed class EmulatorDock(Panel surface) : IDisposable
{
    private const int StyleIndex = -16;
    private const int Child = 0x40000000;
    private const int Popup = unchecked((int)0x80000000);
    private const int WindowFrame = 0x00CF0000;
    private const uint NoActivate = 0x0010;
    private const uint NoZOrder = 0x0004;
    private const uint FrameChanged = 0x0020;
    private const uint DiscardOldPixels = 0x0100;
    private const uint InvalidateDescendants = 0x0081;
    private const uint CloseWindow = 0x0010;
    private nint window;
    private int originalStyle;
    private Rectangle originalBounds;

    public bool Attached => this.window != 0 && IsWindow(this.window) && GetParent(this.window) == surface.Handle;

    internal Rectangle? ClientBounds
    {
        get
        {
            var origin = Point.Empty;
            return this.Attached && GetClientRect(this.window, out var bounds) && ClientToScreen(this.window, ref origin)
                ? new Rectangle(origin, new Size(bounds.Right, bounds.Bottom)) : null;
        }
    }

    public bool IsFocused
    {
        get
        {
            nint focused = GetFocus();
            return this.Attached && (focused == this.window || IsChild(this.window, focused));
        }
    }

    public bool TryAttach(Process process)
    {
        if (this.Attached) return true;
        if (process.HasExited || !surface.IsHandleCreated) return false;
        nint candidate = FindMainWindow((uint)process.Id);
        if (candidate == 0 || !GetWindowRect(candidate, out var bounds)) return false;

        int style = GetWindowLong(candidate, StyleIndex);
        if (style == 0) return false;
        SetWindowLong(candidate, StyleIndex, (style & ~(Popup | WindowFrame)) | Child);
        SetParent(candidate, surface.Handle);
        if (GetParent(candidate) != surface.Handle)
        {
            SetWindowLong(candidate, StyleIndex, style);
            return false;
        }

        this.window = candidate;
        this.originalStyle = style;
        this.originalBounds = Rectangle.FromLTRB(bounds.Left, bounds.Top, bounds.Right, bounds.Bottom);
        this.Resize(FrameChanged);
        return true;
    }

    public void Resize() => this.Resize(0);

    private void Resize(uint flags)
    {
        if (!this.Attached || surface.ClientSize is not { Width: > 0, Height: > 0 } size) return;
        if (SetWindowPos(this.window, 0, 0, 0, size.Width, size.Height, NoActivate | NoZOrder | DiscardOldPixels | flags))
            RedrawWindow(this.window, 0, 0, InvalidateDescendants);
    }

    public bool FocusGame()
    {
        if (!this.Attached) return false;
        if (this.IsFocused) return true;
        SetFocus(this.window);
        return this.IsFocused;
    }

    internal void RestoreFocusAfterClosing(Form tool)
    {
        if (surface.FindForm() is not { } owner) return;
        bool toolWasActive = false;
        tool.FormClosing += (_, _) => toolWasActive = tool.IsHandleCreated && GetActiveWindow() == tool.Handle;
        tool.FormClosed += (_, args) =>
        {
            if (!toolWasActive || args.CloseReason == CloseReason.FormOwnerClosing || !owner.IsHandleCreated || owner.Disposing) return;
            owner.BeginInvoke(() =>
            {
                if (owner.Disposing || !owner.IsHandleCreated || !this.Attached) return;
                nint active = GetActiveWindow();
                if (active != 0 && active != owner.Handle) return;
                if (active == 0) owner.Activate();
                this.FocusGame();
            });
        };
    }

    internal Task<bool> OpenInputSettingsAsync(Process process)
    {
        if (process.HasExited) return Task.FromResult(false);
        uint processId = (uint)process.Id;
        nint settings = FindInputSettingsWindow(processId);
        if (settings != 0)
        {
            SetForegroundWindow(settings);
            return Task.FromResult(true);
        }

        nint target = this.Attached ? this.window : FindMainWindow(processId);
        if (target == 0 || GetWindowThreadProcessId(target, out uint owner) == 0 || owner != processId)
            return Task.FromResult(false);
        return Task.Run(() => OpenSettingsMenu(target, processId));
    }

    internal static bool CloseInputSettingsWindow(Process process)
    {
        nint settings = FindInputSettingsWindow((uint)process.Id);
        return settings != 0 && PostMessage(settings, CloseWindow, 0, 0);
    }

    private static bool OpenSettingsMenu(nint window, uint processId)
    {
        try
        {
            var main = AutomationElement.FromHandle(window);
            var tools = FindNamedControl(main, ControlType.MenuItem, "Tools", "Werkzeuge");
            if (tools is null || !tools.TryGetCurrentPattern(ExpandCollapsePattern.Pattern, out object pattern)) return false;
            var menu = (ExpandCollapsePattern)pattern;
            menu.Expand();
            try
            {
                var settings = FindNamedControl(main, ControlType.MenuItem, "Settings...", "Settings…", "Einstellungen...", "Einstellungen…");
                if (settings is null || !settings.TryGetCurrentPattern(InvokePattern.Pattern, out object action)) return false;
                ((InvokePattern)action).Invoke();
            }
            finally
            {
                if (menu.Current.ExpandCollapseState == ExpandCollapseState.Expanded) menu.Collapse();
            }
            nint dialog = 0;
            if (!SpinWait.SpinUntil(() => (dialog = FindInputSettingsWindow(processId)) != 0, TimeSpan.FromSeconds(1))) return false;
            var gamepads = FindNamedControl(AutomationElement.FromHandle(dialog), ControlType.ListItem, "Gamepads", "Controllers");
            if (gamepads is not null && gamepads.TryGetCurrentPattern(SelectionItemPattern.Pattern, out object selection))
                ((SelectionItemPattern)selection).Select();
            return true;
        }
        catch (Exception error) when (error is ElementNotAvailableException or InvalidOperationException or COMException)
        {
            return false;
        }
    }

    private static AutomationElement? FindNamedControl(AutomationElement parent, ControlType type, params string[] names) =>
        parent.FindFirst(TreeScope.Descendants, new AndCondition(
            new PropertyCondition(AutomationElement.ControlTypeProperty, type),
            new OrCondition(names.Select(name => (Condition)new PropertyCondition(AutomationElement.NameProperty, name)).ToArray())));

    private static nint FindInputSettingsWindow(uint processId) =>
        FindWindow(processId, title => title is "Settings" or "Einstellungen");

    public async Task CloseGameAsync(Process process, CancellationToken cancellationToken)
    {
        while (!process.HasExited && !this.RequestGameClose(process))
            await Task.Delay(100, cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
    }

    public bool RequestGameClose(Process process)
    {
        if (process.HasExited) return false;
        uint processId = (uint)process.Id;
        nint target;
        if (this.Attached && GetWindowThreadProcessId(this.window, out uint owner) != 0 && owner == processId)
        {
            // Restore the emulator's top-level window so Qt handles this as an application close.
            target = this.window;
            this.Detach();
        }
        else
        {
            target = FindMainWindow(processId);
        }
        return target != 0 && PostMessage(target, CloseWindow, 0, 0);
    }

    public static bool CloseStartupScriptingWindow(Process process)
    {
        if (process.HasExited) return false;
        nint view = ScriptingWindow(process);
        return view != 0 && PostMessage(view, CloseWindow, 0, 0);
    }

    public static bool HasScriptingWindow(Process process) => !process.HasExited && ScriptingWindow(process) != 0;

    private static nint ScriptingWindow(Process process) =>
        FindWindow((uint)process.Id, title => title is "Scripting" or "Skripting" or "Skriptverwaltung");

    public bool Detach()
    {
        nint handle = this.window;
        this.window = 0;
        if (handle == 0 || !IsWindow(handle)) return false;
        SetParent(handle, 0);
        SetWindowLong(handle, StyleIndex, this.originalStyle);
        var bounds = this.originalBounds;
        SetWindowPos(handle, 0, bounds.X, bounds.Y, bounds.Width, bounds.Height, NoActivate | NoZOrder | FrameChanged);
        return GetParent(handle) == 0 && GetWindowLong(handle, StyleIndex) == this.originalStyle;
    }

    public void Dispose() => this.Detach();

    private static nint FindMainWindow(uint processId) =>
        FindWindow(processId, title => title.StartsWith("mGBA", StringComparison.OrdinalIgnoreCase));

    private static nint FindWindow(uint processId, Func<string, bool> matchesTitle)
    {
        nint result = 0;
        EnumWindows((handle, _) =>
        {
            GetWindowThreadProcessId(handle, out uint owner);
            if (owner != processId || !IsWindowVisible(handle)) return true;
            var title = new StringBuilder(256);
            GetWindowText(handle, title, title.Capacity);
            if (!matchesTitle(title.ToString())) return true;
            result = handle;
            return false;
        }, 0);
        return result;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool EnumWindow(nint handle, nint data);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindow callback, nint data);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint process);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint window, StringBuilder title, int capacity);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint window, out NativeRect bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetClientRect(nint window, out NativeRect bounds);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ClientToScreen(nint window, ref Point origin);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RedrawWindow(nint window, nint bounds, nint region, uint flags);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(nint window, int index, int value);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint SetParent(nint child, nint parent);

    [DllImport("user32.dll")]
    private static extern nint GetParent(nint window);

    [DllImport("user32.dll")]
    private static extern nint SetFocus(nint window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(nint window);

    [DllImport("user32.dll")]
    private static extern nint GetFocus();

    [DllImport("user32.dll")]
    private static extern nint GetActiveWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(nint parent, nint window);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(nint window, uint message, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}

internal sealed class EmulatorSurface : Panel
{
    private const int MouseActivate = 0x0021;
    private const int ParentNotify = 0x0210;
    private const int LeftButtonDown = 0x0201;
    private const int ClipChildren = 0x02000000;

    public event Action? GameClicked;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // Keep WinForms background paints out of the native Qt child during a resize.
            parameters.Style |= ClipChildren;
            return parameters;
        }
    }

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        // Qt receives the actual click. This is the parent notification for the native child.
        if ((message.Msg == ParentNotify && ((long)message.WParam & 0xFFFF) == LeftButtonDown) ||
            (message.Msg == MouseActivate && ((long)message.LParam >> 16 & 0xFFFF) == LeftButtonDown))
            this.GameClicked?.Invoke();
    }
}
