using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MishkaHelper.Core;

/// <summary>
/// Game mode: Windows "Do not disturb", every other app muted (except the game and voice chats), phone toasts held back.
/// Turns on by itself when a full-screen game is in front and off when that game closes.
/// </summary>
public static class Focus
{
    public static bool On { get; private set; }
    public static string App { get; private set; } = "";
    public static DateTime Since { get; private set; }
    public static int HeldNotifications;
    public static event Action? Changed;

    static int _pid;
    static bool _auto, _dndWas;
    static readonly List<int> MutedPids = new();

    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    // Full-screen video in a browser or player is not a game.
    static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
        { "chrome", "msedge", "firefox", "browser", "opera", "yandex", "vlc", "mpc-hc64", "potplayermini64", "kmplayer", "explorer", "powerpnt", "MishkaHelper" };

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT Monitor, Work; public int Flags; }
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, int flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr m, ref MONITORINFO i);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr h, System.Text.StringBuilder s, int n);

    /// <summary>The window fills its whole monitor and isn't just maximized (a maximized window can overhang the screen when the taskbar auto-hides).</summary>
    internal static bool Fills(bool maximized, (int L, int T, int R, int B) w, (int L, int T, int R, int B) m) =>
        !maximized && w.L <= m.L && w.T <= m.T && w.R >= m.R && w.B >= m.B;

    /// <summary>
    /// A game, video or slide show on the whole screen. SHQueryUserNotificationState can't answer this alone:
    /// it says "busy" (2) for ordinary windows too, which silenced everything. Only its exclusive Direct3D state (3) is trusted.
    /// </summary>
    public static bool FullScreenNow()
    {
        if (SHQueryUserNotificationState(out var st) == 0 && st == 3) return true;
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return false;
        var c = new System.Text.StringBuilder(64);
        GetClassName(h, c, c.Capacity);
        if (c.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;   // the desktop itself covers the screen
        var i = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetWindowRect(h, out var r) || !GetMonitorInfo(MonitorFromWindow(h, 2), ref i)) return false;
        return Fills(IsZoomed(h), (r.Left, r.Top, r.Right, r.Bottom), (i.Monitor.Left, i.Monitor.Top, i.Monitor.Right, i.Monitor.Bottom));
    }

    static (int Pid, string Name) Foreground()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        try { using var p = Process.GetProcessById((int)pid); return ((int)pid, p.ProcessName); } catch { return (0, ""); }
    }

    public static void Toggle() => Set(!On);

    public static void Set(bool on, bool auto = false)
    {
        if (on == On) return;
        if (on)
        {
            (_pid, App) = Foreground();
            _auto = auto; Since = DateTime.Now; HeldNotifications = 0;
            _dndWas = Tweaks.DoNotDisturb;
            Tweaks.DoNotDisturb = true;
            var keep = Settings.Current.FocusKeepSound;
            foreach (var a in Audio.Apps())
            {
                if (a.Pid == _pid || a.Pid == 0 || a.Muted || a.Session.IsSystemSoundsSession) continue;
                string name; try { using var p = Process.GetProcessById(a.Pid); name = p.ProcessName; } catch { continue; }
                if (keep.Any(k => name.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;
                try { a.Session.SimpleAudioVolume.Mute = true; MutedPids.Add(a.Pid); } catch { }
            }
        }
        else
        {
            if (!_dndWas) Tweaks.DoNotDisturb = false;
            foreach (var a in Audio.Apps().Where(a => MutedPids.Contains(a.Pid)))
                try { a.Session.SimpleAudioVolume.Mute = false; } catch { }
            MutedPids.Clear();
        }
        On = on;
        Osd.Show("", on ? "Игровой режим: тишина" : "Игровой режим выключен");
        if (!on && HeldNotifications > 0)
            Toast.Show("", "Пока ты играл", $"С телефона пришло уведомлений: {HeldNotifications}. Список — на странице «Телефон».", () => MishkaHelper.App.Current.Navigate("Phone"));
        Changed?.Invoke();
    }

    /// <summary>Every 5 s from Services: auto on for a full-screen game, auto off when it closes.</summary>
    public static void Check()
    {
        if (On)
        {
            if (_auto && !Alive(_pid)) Set(false);
            return;
        }
        if (!Settings.Current.FocusAuto) return;
        if (!FullScreenNow()) return;
        var (pid, name) = Foreground();
        if (pid == 0 || NotGames.Contains(name)) return;
        // ponytail: "full screen + busy GPU" = game; a full-screen app that doesn't load the GPU stays untouched.
        SHQueryUserNotificationState(out var st);
        if (st != 3 && SysMon.Last.Gpu < 40) return;
        Set(true, auto: true);
    }

    static bool Alive(int pid) { try { using var p = Process.GetProcessById(pid); return !p.HasExited; } catch { return false; } }

    /// <summary>For the phone: what's running, for how long, and how hot.</summary>
    public static object Status()
    {
        var s = SysMon.Last;
        int today = ScreenTime.Top(1).FirstOrDefault(x => x.App.Equals(App, StringComparison.OrdinalIgnoreCase)).Seconds;
        return new
        {
            on = On, app = App, minutes = On ? (int)(DateTime.Now - Since).TotalMinutes : 0, today = today / 60,
            cpu = Math.Round(s.Cpu), gpu = Math.Round(s.Gpu), cpuTemp = s.CpuTemp, gpuTemp = s.GpuTemp, ram = Math.Round(s.Ram), held = HeldNotifications,
        };
    }
}
