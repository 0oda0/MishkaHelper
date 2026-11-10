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
        var mouse = new Dictionary<(uint, string), string>();
        foreach (var (action, combo) in Settings.Current.Hotkeys)
        {
            if (Defaults.Available(action) && MouseHotkeys.TryParse(combo, out var mm, out var button)) { mouse[(mm, button)] = action; continue; }
            if (!Defaults.Available(action) || !TryParse(combo, out var mods, out var vk)) continue;
            int id = next++;
            // Volume/brightness repeat while held; everything else fires once.
            uint repeat = action is "volUp" or "volDown" or "brightUp" or "brightDown" ? 0 : Native.MOD_NOREPEAT;
            if (Native.RegisterHotKey(_src.Handle, id, mods | repeat, vk)) Ids[id] = action;
            else Failed.Add(action);
        }
        foreach (var sc in Settings.Current.Scenarios.Where(s => s.Enabled))
            foreach (var t in sc.Triggers.Where(t => t.Kind == "hotkey"))
                if (MouseHotkeys.TryParse(t.Value, out var sm, out var sb)) mouse[(sm, sb)] = "scenario:" + sc.Id;
                else if (TryParse(t.Value, out var m2, out var vk2) && Native.RegisterHotKey(_src.Handle, next, m2 | Native.MOD_NOREPEAT, vk2))
                    Ids[next++] = "scenario:" + sc.Id;
        MouseHotkeys.Set(mouse);
        MouseHotkeys.Pause(false);
        if (Failed.Count > 0) Log.Info("hotkeys taken by other apps: " + string.Join(", ", Failed.Select(a => $"{a}={Settings.Current.Hotkeys[a]}")));
    }

    /// <summary>Unregister everything while the recorder listens; Reload() brings them back.</summary>
    public static void Suspend()
    {
        if (_src == null) return;
        foreach (var id in Ids.Keys) Native.UnregisterHotKey(_src.Handle, id);
        Ids.Clear();
        MouseHotkeys.Pause(true);
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
        return vk != 0 && (mods != 0 || SingleAllowed(KeyInterop.KeyFromVirtualKey((int)vk)));
    }

    /// <summary>
    /// Keys that may be a hotkey on their own. Letters, digits, Space, Enter and the like are needed for typing:
    /// taken alone, they would stop working everywhere.
    /// </summary>
    public static bool SingleAllowed(Key k) =>
        k is >= Key.F1 and <= Key.F24 or Key.Pause or Key.Scroll or Key.Insert or Key.Apps or Key.PrintScreen
            or Key.MediaPlayPause or Key.MediaNextTrack or Key.MediaPreviousTrack or Key.MediaStop or Key.VolumeMute or Key.VolumeUp or Key.VolumeDown
            or Key.BrowserBack or Key.BrowserForward or Key.BrowserHome or Key.BrowserRefresh or Key.BrowserSearch or Key.BrowserFavorites or Key.BrowserStop
            or Key.LaunchMail or Key.LaunchApplication1 or Key.LaunchApplication2 or Key.SelectMedia;

    /// <summary>Human label for a stored combo ("Ctrl+Alt+OemPlus" → "Ctrl+Alt+=").</summary>
    public static string Pretty(string combo) => combo
        .Replace("MouseMiddle", "колёсико мыши").Replace("Mouse4", "мышь «назад»").Replace("Mouse5", "мышь «вперёд»").Replace("Scroll", "ScrollLock")
        .Replace("OemPlus", "=").Replace("OemMinus", "−").Replace("PageUp", "PgUp").Replace("PageDown", "PgDn")
        .Replace("Space", "Пробел").Replace("Return", "Enter");

    /// <summary>A mouse button for the recorder: the wheel click and the side buttons; left and right are too common to take.</summary>
    public static string? Format(MouseButton b)
    {
        var name = b switch { MouseButton.Middle => MouseHotkeys.Middle, MouseButton.XButton1 => MouseHotkeys.Back, MouseButton.XButton2 => MouseHotkeys.Forward, _ => null };
        if (name == null) return null;
        var m = Keyboard.Modifiers;
        var parts = new List<string>();
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        parts.Add(name);
        return string.Join("+", parts);
    }

    /// <summary>Format a key press from a WPF KeyDown for the recorder in the Hotkeys page.</summary>
    public static string? Format(KeyEventArgs e) => Format(e, out _);

    /// <summary>As Format; when a single key can't be a hotkey on its own, says why.</summary>
    public static string? Format(KeyEventArgs e, out string? why)
    {
        why = null;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin) return null;
        var m = Keyboard.Modifiers;
        var parts = new List<string>();
        if (m.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (m.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (m.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (m.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        if (parts.Count == 0 && !SingleAllowed(key))
        {
            why = "Одну эту клавишу нельзя: она нужна для набора. Добавь Ctrl, Alt, Shift или Win, или возьми F1–F24, Pause, ScrollLock, Insert, медиаклавиши";
            return null;
        }
        parts.Add(key.ToString());
        return string.Join("+", parts);
    }
}
