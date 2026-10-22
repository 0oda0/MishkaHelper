using System.Windows.Input;
using System.Windows.Interop;

namespace MishkaHelper.Core;

/// <summary>Global hotkeys via RegisterHotKey on a message-only window.</summary>
public static class Hotkeys
{
    static HwndSource? _src;
    static readonly Dictionary<int, string> Ids = new();

    /// <summary>Actions whose combo couldn't be registered (taken by another app).</summary>
    public static readonly HashSet<string> Failed = new();

    public static void Start()
    {
        _src = new HwndSource(new HwndSourceParameters("MishkaHotkeys") { ParentWindow = new IntPtr(-3) });   // HWND_MESSAGE
        _src.AddHook((IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) =>
        {
            if (msg == Native.WM_HOTKEY && Ids.TryGetValue(w.ToInt32(), out var action)) { Log.Info("hotkey " + action); Actions.Run(action); handled = true; }
            return IntPtr.Zero;
        });
        Reload();
    }

    public static void Reload()
    {
        if (_src == null) return;
        foreach (var id in Ids.Keys) Native.UnregisterHotKey(_src.Handle, id);
        Ids.Clear(); Failed.Clear();
        int next = 1;
        foreach (var (action, combo) in Settings.Current.Hotkeys)
        {
            if (!TryParse(combo, out var mods, out var vk)) continue;
            int id = next++;
            // Volume/brightness repeat while held; everything else fires once.
            uint repeat = action is "volUp" or "volDown" or "brightUp" or "brightDown" ? 0 : Native.MOD_NOREPEAT;
            if (Native.RegisterHotKey(_src.Handle, id, mods | repeat, vk)) Ids[id] = action;
            else Failed.Add(action);
        }
        if (Failed.Count > 0) Log.Info("hotkeys taken by other apps: " + string.Join(", ", Failed.Select(a => $"{a}={Settings.Current.Hotkeys[a]}")));
    }

    /// <summary>Unregister everything while the recorder listens; Reload() brings them back.</summary>
    public static void Suspend()
    {
        if (_src == null) return;
        foreach (var id in Ids.Keys) Native.UnregisterHotKey(_src.Handle, id);
        Ids.Clear();
    }

    public static void Stop() { if (_src != null) foreach (var id in Ids.Keys) Native.UnregisterHotKey(_src.Handle, id); _src?.Dispose(); }

    public static bool TryParse(string combo, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        if (string.IsNullOrWhiteSpace(combo)) return false;
        foreach (var part in combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": mods |= Native.MOD_CONTROL; break;
                case "alt": mods |= Native.MOD_ALT; break;
                case "shift": mods |= Native.MOD_SHIFT; break;
                case "win": mods |= Native.MOD_WIN; break;
                default:
                    if (!Enum.TryParse<Key>(part, true, out var key)) return false;
                    vk = (uint)KeyInterop.VirtualKeyFromKey(key);
                    break;
            }
        }
        return vk != 0 && mods != 0;
    }

    /// <summary>Human label for a stored combo ("Ctrl+Alt+OemPlus" → "Ctrl+Alt+=").</summary>
    public static string Pretty(string combo) => combo
        .Replace("OemPlus", "=").Replace("OemMinus", "−").Replace("PageUp", "PgUp").Replace("PageDown", "PgDn")
        .Replace("Space", "Пробел").Replace("Return", "Enter");

    /// <summary>Format a key press from a WPF KeyDown for the recorder in the Hotkeys page.</summary>
    public static string? Format(KeyEventArgs e)
    {
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return null;
        var m = Keyboard.Modifiers;
        var parts = new List<string>();
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (parts.Count == 0) return null;
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }
}
