using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SoulLink.Player;

internal sealed class MgbaKeyboardBindings(string home)
{
    private const string KeyboardSection = "[gba.input.QT_K]";
    private const int Special = 0x01000000;
    private readonly string pendingPath = Path.Combine(home, "keyboard-pending.json");
    private readonly object gate = new();
    private static readonly Dictionary<Keys, (int Offset, string Name)> SpecialKeys = new()
    {
        [Keys.Escape] = (0x00, "Esc"), [Keys.Tab] = (0x01, "Tab"), [Keys.Back] = (0x03, "Rücktaste"),
        [Keys.Enter] = (0x04, "Enter"), [Keys.Insert] = (0x06, "Einfg"), [Keys.Delete] = (0x07, "Entf"),
        [Keys.Home] = (0x10, "Pos1"), [Keys.End] = (0x11, "Ende"), [Keys.Left] = (0x12, "←"),
        [Keys.Up] = (0x13, "↑"), [Keys.Right] = (0x14, "→"), [Keys.Down] = (0x15, "↓"),
        [Keys.PageUp] = (0x16, "Bild ↑"), [Keys.PageDown] = (0x17, "Bild ↓"),
        [Keys.ShiftKey] = (0x20, "Umschalt"), [Keys.ControlKey] = (0x21, "Strg"), [Keys.Menu] = (0x23, "Alt"),
    };

    private static readonly Dictionary<string, int> Defaults = new(StringComparer.Ordinal)
    {
        ["Up"] = Special + 0x13, ["Left"] = Special + 0x12, ["Right"] = Special + 0x14, ["Down"] = Special + 0x15,
        ["L"] = 'A', ["R"] = 'S', ["Select"] = Special + 0x03, ["Start"] = Special + 0x04, ["B"] = 'Z', ["A"] = 'X',
    };

    internal Dictionary<string, int> Read(string? executable)
    {
        lock (this.gate) return this.ReadPending() ?? ReadConfig(executable);
    }

    private static Dictionary<string, int> ReadConfig(string? executable)
    {
        var bindings = new Dictionary<string, int>(Defaults, StringComparer.Ordinal);
        if (executable is null) return bindings;
        string path = ConfigPath(executable);
        if (!File.Exists(path)) return bindings;
        bool keyboard = false;
        foreach (string line in File.ReadLines(path))
        {
            string entry = line.Trim();
            if (entry.StartsWith('['))
            {
                keyboard = entry == KeyboardSection;
                continue;
            }
            int separator = entry.IndexOf('=');
            if (!keyboard || separator < 0) continue;
            string? key = BindingName(entry[..separator].TrimEnd());
            if (key is not null && int.TryParse(entry.AsSpan(separator + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
                bindings[key] = value;
        }
        return bindings;
    }

    internal void Save(Dictionary<string, int> bindings)
    {
        if (!Valid(bindings)) throw new InvalidDataException("Die Tastenbelegung ist unvollständig.");
        if (bindings.Values.Where(key => key != -1).GroupBy(key => key).Any(group => group.Count() > 1))
            throw new InvalidDataException("Eine Taste ist mehrfach belegt.");
        lock (this.gate) WriteAtomic(this.pendingPath, JsonSerializer.Serialize(bindings));
    }

    internal void ApplyPending(string executable)
    {
        lock (this.gate)
        {
            if (this.ReadPending() is not { } bindings) return;
            WriteConfig(executable, bindings);
            File.Delete(this.pendingPath);
        }
    }

    private static void WriteConfig(string executable, Dictionary<string, int> bindings)
    {
        string path = ConfigPath(executable);
        var lines = new List<string>();
        bool keyboard = false;
        bool written = false;
        foreach (string line in File.Exists(path) ? File.ReadAllLines(path) : [])
        {
            string entry = line.Trim();
            if (entry.StartsWith('['))
            {
                if (keyboard && !written) AppendBindings();
                keyboard = entry == KeyboardSection;
            }
            else if (keyboard)
            {
                int separator = entry.IndexOf('=');
                if (separator >= 0 && BindingName(entry[..separator].TrimEnd()) is not null) continue;
            }
            lines.Add(line);
        }
        if (!written)
        {
            if (!keyboard) lines.Add(KeyboardSection);
            AppendBindings();
        }
        WriteAtomic(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);

        void AppendBindings()
        {
            foreach (var (name, value) in bindings)
                lines.Add(FormattableString.Invariant($"key{name}={value}"));
            written = true;
        }
    }

    private Dictionary<string, int>? ReadPending()
    {
        if (!File.Exists(this.pendingPath)) return null;
        try
        {
            var bindings = JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(this.pendingPath));
            return bindings is not null && Valid(bindings) ? bindings : null;
        }
        catch (JsonException) { return null; }
    }

    private static bool Valid(Dictionary<string, int> bindings) =>
        bindings.Count == Defaults.Count && bindings.All(pair => Defaults.ContainsKey(pair.Key) && pair.Value is >= -1 and <= 0x01ffffff);

    private static string ConfigPath(string executable) => Path.Combine(Path.GetDirectoryName(executable)!, "config.ini");

    private static string? BindingName(string key) =>
        key.StartsWith("key", StringComparison.Ordinal) && Defaults.ContainsKey(key[3..]) ? key[3..] : null;

    private static void WriteAtomic(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, contents);
            File.Move(temporary, path, overwrite: true);
        }
        finally { File.Delete(temporary); }
    }

    internal static int? QtKey(Keys key)
    {
        if (SpecialKeys.TryGetValue(key, out var special)) return Special + special.Offset;
        if (key is >= Keys.F1 and <= Keys.F24) return Special + 0x30 + (key - Keys.F1);
        if (key is >= Keys.NumPad0 and <= Keys.NumPad9) return '0' + (key - Keys.NumPad0);
        uint character = MapVirtualKey((uint)key, 2);
        return character is >= 32 and < 0xd800 ? char.ToUpperInvariant((char)character) : null;
    }

    internal static string Label(int value)
    {
        if (value == -1) return "–";
        if (value == ' ') return "Leertaste";
        if (value is >= 33 and < 0xd800) return ((char)value).ToString();
        if (value is >= Special + 0x30 and <= Special + 0x47) return $"F{value - Special - 0x2f}";
        return SpecialKeys.Values.FirstOrDefault(key => Special + key.Offset == value).Name ?? $"Qt {value}";
    }

    [DllImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static extern uint MapVirtualKey(uint code, uint mapType);
}
