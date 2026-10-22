using System.IO;

namespace MishkaHelper.Core;

/// <summary>Plain-language health check used by voice ("почему тормозит") and given to the AI as context.</summary>
public static class Diagnostics
{
    public static string Status()
    {
        var s = SysMon.Last;
        var parts = new List<string> { $"Процессор загружен на {s.Cpu:0}%", $"память на {s.Ram:0}%" };
        if (s.CpuTemp > 0) parts.Add($"температура процессора {s.CpuTemp}°" + (s.GpuTemp > 0 ? $", видеокарты {s.GpuTemp}°" : ""));
        if (s.CpuFan > 0) parts.Add($"вентиляторы {s.CpuFan} оборотов");
        return string.Join(", ", parts) + "." + (Problems().Count == 0 ? " Всё в норме." : " Есть замечания — спроси «почему тормозит».");
    }

    public static List<string> Problems()
    {
        var s = SysMon.Last;
        var p = new List<string>();
        if (s.CpuTemp >= 90) p.Add($"процессор перегревается ({s.CpuTemp}°) — включи вентиляторы на максимум и проверь, не закрыты ли решётки");
        if (s.GpuTemp >= 85) p.Add($"видеокарта горячая ({s.GpuTemp}°)");
        if (s.Ram >= 88) p.Add($"почти кончилась оперативная память ({s.Ram:0}%) — закрой лишние вкладки и программы");
        if (s.Cpu >= 85)
        {
            var top = SystemInfo.TopProcesses(1).FirstOrDefault();
            p.Add($"процессор загружен на {s.Cpu:0}%" + (top != null ? $", больше всех ест «{top.Name}»" : ""));
        }
        if (s.DiskPct >= 90) p.Add("диск работает на пределе — что-то активно читает или пишет");
        foreach (var d in SystemInfo.Drives().Where(d => d.DriveType == DriveType.Fixed && d.AvailableFreeSpace < 10L << 30))
            p.Add($"на диске {d.Name.TrimEnd('\\')} осталось {Cleaner.Size(d.AvailableFreeSpace)} — сделай очистку");
        if (Power.OnAc && Power.Mode == "Saver") p.Add("Windows в режиме «Экономия», хотя ноутбук на зарядке — переключи на «Баланс» или «Производительность»");
        if (Acer.Available && Acer.Mode == 0 && Power.OnAc) p.Add("включён тихий режим Nitro — он ограничивает мощность");
        var startup = StartupApps.List().Count(x => x.Enabled);
        if (startup > 12) p.Add($"в автозагрузке {startup} программ — часть можно отключить в «Ускорении»");
        var up = TimeSpan.FromMilliseconds(Environment.TickCount64);
        if (up.TotalDays > 7) p.Add($"компьютер не перезагружался {up.Days} дней — перезагрузка часто помогает");
        var b = Power.Battery();
        if (b.Present && b.Wear > 30) p.Add($"батарея изношена на {b.Wear:0}%");
        return p;
    }

    public static string Report()
    {
        var p = Problems();
        return p.Count == 0
            ? "Я проверил: температура, память, процессор, диски и режим питания в норме. Если тормозит конкретная игра или программа — назови её."
            : "Вот что нашёл: " + string.Join("; ", p) + ".";
    }

    /// <summary>Compact snapshot for the AI prompt.</summary>
    public static string Snapshot()
    {
        var s = SysMon.Last;
        var b = Power.Battery();
        return $"CPU {s.Cpu:0}% {s.CpuTemp}°C, GPU {s.Gpu:0}% {s.GpuTemp}°C, RAM {s.RamUsedGb:0.0}/{s.RamTotalGb:0} ГБ, " +
               (b.Present ? $"батарея {b.Percent}%{(b.OnAc ? " (заряжается)" : "")}, " : "") +
               $"режим питания {Power.Mode}{(Acer.Available ? $", Nitro {Acer.AllModes.FirstOrDefault(m => m.Id == Acer.Mode).Name}" : "")}, " +
               $"звук {Audio.Volume}% через {Audio.DefaultName(NAudio.CoreAudioApi.DataFlow.Render)}";
    }
}
