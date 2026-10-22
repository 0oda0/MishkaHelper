using System.IO;
using System.Windows;

namespace MishkaHelper.Core;

/// <summary>Threshold alerts with a cooldown so they don't spam.</summary>
public static class Alerts
{
    static readonly Dictionary<string, DateTime> Last = new();
    static DateTime _diskCheck;

    public static void Check(Sample s)
    {
        var a = Settings.Current.Alerts;
        if (!a.Enabled) return;
        if (s.CpuTemp >= a.CpuTemp) Fire("cpuT", "", "CPU перегревается", $"{s.CpuTemp} °C — проверь вентиляцию или включи «Макс. вентиляторы»");
        if (s.GpuTemp >= a.GpuTemp) Fire("gpuT", "", "GPU перегревается", $"{s.GpuTemp} °C");
        if (s.Ram >= a.RamPercent) Fire("ram", "", "Мало оперативной памяти", $"Занято {s.Ram:0}% — закрой лишнее или нажми «Ускорить всё»");

        if ((s.Time - _diskCheck).TotalMinutes >= 10)
        {
            _diskCheck = s.Time;
            foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
                if (d.AvailableFreeSpace / 1073741824.0 < a.DiskFreeGb)
                    Fire("disk" + d.Name, "", $"Мало места на {d.Name.TrimEnd('\\')}", $"Свободно {d.AvailableFreeSpace / 1073741824.0:0.0} ГБ — открой «Ускорение»");
        }
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
