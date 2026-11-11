using System.IO;
using System.Windows;

namespace MishkaHelper.Core;

/// <summary>
/// Threshold alerts. Each one speaks once when the value crosses the line, then stays quiet until the value
/// has come back to normal (with a margin, so hovering around the line doesn't count), and never more than once an hour.
/// </summary>
public static class Alerts
{
    static readonly Dictionary<string, DateTime> Last = new();
    static readonly HashSet<string> Raised = new();
    static DateTime _diskCheck;

    public static void Check(Sample s)
    {
        var a = Settings.Current.Alerts;
        if (!a.Enabled) return;
        Watch("cpuT", s.CpuTemp, a.CpuTemp, 7, "CPU перегревается", $"{s.CpuTemp} °C — проверь вентиляцию или включи «Макс. вентиляторы»");
        Watch("gpuT", s.GpuTemp, a.GpuTemp, 7, "GPU перегревается", $"{s.GpuTemp} °C");
        Watch("ram", s.Ram, a.RamPercent, 8, "Мало оперативной памяти", $"Занято {s.Ram:0}% — закрой лишнее или нажми «Ускорить всё»");

        if ((s.Time - _diskCheck).TotalMinutes >= 10)
        {
            _diskCheck = s.Time;
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                if (d.AvailableFreeSpace / 1073741824.0 < a.DiskFreeGb)
                    Fire("disk" + d.Name, "", $"Мало места на {d.Name.TrimEnd('\\')}", $"Свободно {d.AvailableFreeSpace / 1073741824.0:0.0} ГБ — открой «Ускорение»", 24 * 60);
        }
    }

    /// <summary>Raised once on crossing; armed again only below limit − margin. Pure decision, for tests.</summary>
    internal static bool Step(HashSet<string> raised, string key, double value, double limit, double margin)
    {
        if (value >= limit) return raised.Add(key);
        if (value < limit - margin) raised.Remove(key);
        return false;
    }

    static void Watch(string key, double value, double limit, double margin, string title, string text)
    {
        bool cross;
        lock (Raised) cross = Step(Raised, key, value, limit, margin);
        if (cross) Fire(key, "", title, text, 60);
    }

    public static void Fire(string key, string icon, string title, string text, int cooldownMin = 15)
    {
        lock (Last)
        {
            if (Last.TryGetValue(key, out var t) && (DateTime.Now - t).TotalMinutes < cooldownMin) return;
            Last[key] = DateTime.Now;
        }
        Application.Current?.Dispatcher.BeginInvoke(() => Toast.Show(icon, title, text));
    }
}
