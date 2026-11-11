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

    /// <summary>Replaces all mouse hotkeys. Without hotkeys and extras the hook is removed entirely.</summary>
    public static void Set(Dictionary<(uint Mods, string Button), string> map, bool extras = false)
    {
        lock (Map) { Map.Clear(); foreach (var kv in map) Map[kv.Key] = kv.Value; }
        bool need = map.Count > 0 || extras;
        if (need && _thread == 0)
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
        else if (!need && _thread != 0)
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

    // A press held back to see what it becomes: a wheel hold (menu), a side-button gesture, or, if neither, the plain click it was.
    static string? _held;
    static int _x0, _y0;
    static bool _moved, _fired;
    static System.Threading.Timer? _holdTimer;
    const int HoldMs = 350, Slop = 12, GestureMin = 60;

    static void Ui(Action a) => System.Windows.Application.Current?.Dispatcher.BeginInvoke(a);

    static IntPtr OnMouse(int code, IntPtr w, IntPtr l)
    {
        if (code < 0 || _paused) return CallNextHookEx(_hook, code, w, l);
        int msg = (int)w;
        if (msg == 0x200 && _held == null) return CallNextHookEx(_hook, code, w, l);   // the hot path: moves pass at once
        var info = Marshal.PtrToStructure<MSLL>(l);
        if ((info.Flags & 1) != 0) return CallNextHookEx(_hook, code, w, l);   // injected input: the phone remote, and our own replayed clicks
        var mx = Settings.Current.Mouse;

        if (msg == 0x200)
        {
            if (Math.Abs(info.X - _x0) > Slop || Math.Abs(info.Y - _y0) > Slop) _moved = true;
            // A wheel hold that moves is the browser's autoscroll: hand the press back and step aside.
            if (_moved && _held == Middle && !_fired) { Release(replayDown: true); }
            return CallNextHookEx(_hook, code, w, l);
        }
        if (msg == 0x20A)
        {
            if (!mx.TaskbarVolume || !OverTaskbar(info.X, info.Y)) return CallNextHookEx(_hook, code, w, l);
            int step = (short)(info.MouseData >> 16) > 0 ? 2 : -2;
            Ui(() => { Audio.Volume += step; Osd.Show("\uE767", "Громкость", Audio.Volume); });
            return (IntPtr)1;
        }
        if (Decode(msg, info.MouseData) is not { } b) return CallNextHookEx(_hook, code, w, l);
        if (!b.Down)
        {
            if (Swallowed.Remove(b.Button)) return (IntPtr)1;
            if (_held == b.Button)
            {
                if (_held != Middle && _moved && Gesture(info.X - _x0, info.Y - _y0) is { } dir
                    && mx.GestureActions.GetValueOrDefault(dir, "") is { Length: > 0 } act)
                    Ui(() => { Log.Info("mouse gesture " + dir); Actions.Run(act); });
                else if (!_fired) Replay(b.Button);
                _held = null; _holdTimer?.Dispose();
                return (IntPtr)1;
            }
            return CallNextHookEx(_hook, code, w, l);
        }
        string? action;
        lock (Map) Map.TryGetValue((Mods(), b.Button), out action);
        if (action != null)
        {
            Swallowed.Add(b.Button);
            Ui(() => { Log.Info("mouse hotkey " + action); Actions.Run(action); });
            return (IntPtr)1;
        }
        if (_held != null || Mods() != 0) return CallNextHookEx(_hook, code, w, l);
        bool hold = b.Button == Middle && mx.HoldWheelRadial;
        bool gesture = b.Button != Middle && mx.Gestures && b.Button == mx.GestureButton;
        if (!hold && !gesture) return CallNextHookEx(_hook, code, w, l);
        _held = b.Button; _x0 = info.X; _y0 = info.Y; _moved = _fired = false;
        if (hold)
            _holdTimer = new System.Threading.Timer(_ =>
            {
                if (_held != Middle || _moved) return;
                _fired = true;
                Ui(RadialMenu.Toggle);
            }, null, HoldMs, Timeout.Infinite);
        return (IntPtr)1;
    }

    /// <summary>Gives a held press back as it was: the down now; its up then passes on its own.</summary>
    static void Release(bool replayDown)
    {
        var b = _held; _held = null; _holdTimer?.Dispose();
        if (replayDown && b != null) Ui(() => InputSim.Button(b, down: true));
    }

    static void Replay(string button) => Ui(() => { InputSim.Button(button, true); InputSim.Button(button, false); });

    /// <summary>Which way a held side-button moved: the longer axis wins; too short is no gesture.</summary>
    internal static string? Gesture(int dx, int dy) =>
        Math.Max(Math.Abs(dx), Math.Abs(dy)) < GestureMin ? null
        : Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? "right" : "left") : (dy > 0 ? "down" : "up");

    [StructLayout(LayoutKind.Sequential)] struct POINT { public int X, Y; }
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);

    static bool OverTaskbar(int x, int y)
    {
        var root = GetAncestor(WindowFromPoint(new POINT { X = x, Y = y }), 2 /* GA_ROOT */);
        var sb = new System.Text.StringBuilder(64);
        GetClassName(root, sb, 64);
        return sb.ToString() is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd";
    }
}
