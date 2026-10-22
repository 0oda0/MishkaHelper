using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows.Threading;

namespace MishkaHelper.Core;

/// <summary>Which program was in front and for how long, per day (30 days). Idle time (no input for 3 min) isn't counted.</summary>
public static class ScreenTime
{
    static readonly string FilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MishkaHelper", "screentime.json");
    /// <summary>"yyyy-MM-dd" → process name → seconds.</summary>
    static Dictionary<string, Dictionary<string, int>> _days = new();
    static DispatcherTimer? _t;
    static int _ticks;
    const int Step = 5;

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO i);

    public static void Start()
    {
        try { if (File.Exists(FilePath)) _days = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, int>>>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) { Log.Error(e); }
        _t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(Step) };
        _t.Tick += (_, _) => Sample();
        _t.Start();
    }

    static uint IdleMs()
    {
        var i = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref i) ? (uint)Environment.TickCount - i.dwTime : 0;
    }

    static void Sample()
    {
        if (!Settings.Current.ScreenTime) return;
        // ponytail: idle = no keyboard/mouse; watching a film without touching anything stops counting after 3 min.
        if (IdleMs() > 180_000) return;
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        string name;
        try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; } catch { return; }
        if (name is "Idle" or "LockApp") return;
        var day = Today();
        day[name] = day.GetValueOrDefault(name) + Step;
        if (++_ticks % 60 == 0) Save();   // every 5 min
    }

    static Dictionary<string, int> Today()
    {
        var key = DateTime.Today.ToString("yyyy-MM-dd");
        if (!_days.TryGetValue(key, out var d)) { _days[key] = d = new(); Trim(); }
        return d;
    }

    static void Trim()
    {
        var cut = DateTime.Today.AddDays(-30).ToString("yyyy-MM-dd");
        foreach (var k in _days.Keys.Where(k => string.CompareOrdinal(k, cut) < 0).ToList()) _days.Remove(k);
    }

    public static void Save()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!); File.WriteAllText(FilePath, JsonSerializer.Serialize(_days)); }
        catch (Exception e) { Log.Error(e); }
    }

    /// <summary>Seconds per program over the last N days (1 = today).</summary>
    public static List<(string App, int Seconds)> Top(int days = 1)
    {
        var from = DateTime.Today.AddDays(1 - days).ToString("yyyy-MM-dd");
        return _days.Where(d => string.CompareOrdinal(d.Key, from) >= 0).SelectMany(d => d.Value)
            .GroupBy(kv => kv.Key).Select(g => (g.Key, g.Sum(kv => kv.Value))).OrderByDescending(x => x.Item2).ToList();
    }

    public static string Format(int sec) => sec >= 3600 ? $"{sec / 3600} ч {sec % 3600 / 60} мин" : $"{Math.Max(1, sec / 60)} мин";

    public static string Summary(int days = 1)
    {
        var top = Top(days);
        if (top.Count == 0) return days == 1 ? "Сегодня я ещё ничего не насчитал." : "Данных пока нет.";
        return $"{(days == 1 ? "Сегодня" : $"За {days} дн.")} за компьютером {Format(top.Sum(t => t.Seconds))}. Больше всего: " +
            string.Join(", ", top.Take(3).Select(t => $"{t.App} — {Format(t.Seconds)}")) + ".";
    }
}
