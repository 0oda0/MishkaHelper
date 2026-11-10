using System.Runtime.InteropServices;

namespace MishkaHelper.Core;

/// <summary>
/// Hotkeys on mouse buttons: the wheel click and the two side buttons, alone or with Ctrl/Alt/Shift/Win.
/// Windows has no RegisterHotKey for the mouse, so this is a low-level hook on its own thread; it exists only while such a hotkey is set,
/// looks at button presses only, and swallows just the press it acts on, so the button doesn't also do its usual job.
/// </summary>
public static class MouseHotkeys
{
    public const string Middle = "MouseMiddle", Back = "Mouse4", Forward = "Mouse5";

    /// <summary>"Ctrl+Mouse4" → modifiers and button; false for anything that isn't a mouse hotkey.</summary>
    public static bool TryParse(string combo, out uint mods, out string button)
    {
        mods = 0; button = "";
        if (string.IsNullOrWhiteSpace(combo)) return false;
        foreach (var part in combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part.ToLowerInvariant())
            {
                case "ctrl": mods |= Native.MOD_CONTROL; break;
                case "alt": mods |= Native.MOD_ALT; break;
                case "shift": mods |= Native.MOD_SHIFT; break;
                case "win": mods |= Native.MOD_WIN; break;
                case "mousemiddle": button = Middle; break;
                case "mouse4": button = Back; break;
                case "mouse5": button = Forward; break;
                default: return false;
            }
        }
        return button.Length > 0;
    }

    static readonly Dictionary<(uint Mods, string Button), string> Map = new();
    static readonly HashSet<string> Swallowed = new();   // a press we took: its release is ours too
    delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    static readonly HookProc Proc = OnMouse;   // kept in a field: the garbage collector must not take what Windows calls back
    [StructLayout(LayoutKind.Sequential)] struct MSLL { public int X, Y; public uint MouseData, Flags, Time; public IntPtr Extra; }
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc f, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);
    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr Hwnd; public uint Message; public IntPtr W, L; public uint Time; public int X, Y; }
    static IntPtr _hook;
    static uint _thread;
    static volatile bool _paused;

    public static bool Active => _thread != 0;

    /// <summary>Replaces all mouse hotkeys. An empty map removes the hook entirely.</summary>
    public static void Set(Dictionary<(uint Mods, string Button), string> map)
    {
        lock (Map) { Map.Clear(); foreach (var kv in map) Map[kv.Key] = kv.Value; }
        if (map.Count > 0 && _thread == 0)
        {
            var started = new ManualResetEventSlim();
            new Thread(() =>
            {
                _thread = GetCurrentThreadId();
                _hook = SetWindowsHookEx(14, Proc, GetModuleHandle(null), 0);
                started.Set();
                while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
                UnhookWindowsHookEx(_hook);
                _hook = IntPtr.Zero;
            }) { IsBackground = true, Name = "Mouse hotkeys", Priority = ThreadPriority.AboveNormal }.Start();
            started.Wait(2000);
        }
        else if (map.Count == 0 && _thread != 0)
        {
            PostThreadMessage(_thread, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
            _thread = 0;
        }
    }

    /// <summary>While the hotkey recorder listens, the buttons must reach it instead of firing.</summary>
    public static void Pause(bool paused) => _paused = paused;

    static uint Mods()
    {
        uint m = 0;
        if (GetAsyncKeyState(0x11) < 0) m |= Native.MOD_CONTROL;
        if (GetAsyncKeyState(0x12) < 0) m |= Native.MOD_ALT;
        if (GetAsyncKeyState(0x10) < 0) m |= Native.MOD_SHIFT;
        if (GetAsyncKeyState(0x5B) < 0 || GetAsyncKeyState(0x5C) < 0) m |= Native.MOD_WIN;
        return m;
    }

    /// <summary>Which of our buttons a mouse message is about, and whether it's the press. Null for everything else (moves, wheel, left/right).</summary>
    internal static (string Button, bool Down)? Decode(int msg, uint mouseData) => msg switch
    {
        0x207 => (Middle, true), 0x208 => (Middle, false),
        0x20B => ((mouseData >> 16) == 1 ? Back : Forward, true),
        0x20C => ((mouseData >> 16) == 1 ? Back : Forward, false),
        _ => null,
    };

    static IntPtr OnMouse(int code, IntPtr w, IntPtr l)
    {
        if (code < 0 || _paused) return CallNextHookEx(_hook, code, w, l);
        int msg = (int)w;
        if (msg is not (0x207 or 0x208 or 0x20B or 0x20C)) return CallNextHookEx(_hook, code, w, l);   // the hot path: moves pass at once
        var info = Marshal.PtrToStructure<MSLL>(l);
        if ((info.Flags & 1) != 0) return CallNextHookEx(_hook, code, w, l);   // injected clicks, e.g. from the phone remote
        if (Decode(msg, info.MouseData) is not { } b) return CallNextHookEx(_hook, code, w, l);
        if (!b.Down) return Swallowed.Remove(b.Button) ? (IntPtr)1 : CallNextHookEx(_hook, code, w, l);
        string? action;
        lock (Map) Map.TryGetValue((Mods(), b.Button), out action);
        if (action == null) return CallNextHookEx(_hook, code, w, l);
        Swallowed.Add(b.Button);
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { Log.Info("mouse hotkey " + action); Actions.Run(action); });
        return (IntPtr)1;
    }
}
