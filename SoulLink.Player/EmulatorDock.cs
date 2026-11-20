using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

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
    private const uint CloseWindow = 0x0010;
    private nint window;
    private int originalStyle;
    private Rectangle originalBounds;

    public bool Attached => this.window != 0 && IsWindow(this.window) && GetParent(this.window) == surface.Handle;

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
        this.Resize();
        return true;
    }

    public void Resize()
    {
        if (!this.Attached) return;
        SetWindowPos(this.window, 0, 0, 0, surface.ClientSize.Width, surface.ClientSize.Height, NoActivate | NoZOrder | FrameChanged);
    }

    public bool FocusGame()
    {
        if (!this.Attached) return false;
        if (this.IsFocused) return true;
        SetFocus(this.window);
        return this.IsFocused;
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
    private static extern nint GetFocus();

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

    public event Action? GameClicked;

    protected override void WndProc(ref Message message)
    {
        base.WndProc(ref message);
        // Qt receives the actual click. This is the parent notification for the native child.
        if ((message.Msg == ParentNotify && ((long)message.WParam & 0xFFFF) == LeftButtonDown) ||
            (message.Msg == MouseActivate && ((long)message.LParam >> 16 & 0xFFFF) == LeftButtonDown))
            this.GameClicked?.Invoke();
    }
}
