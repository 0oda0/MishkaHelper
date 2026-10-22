using System.Management;
using System.Text.RegularExpressions;

namespace MishkaHelper.Core;

public record BatteryInfo(bool Present, int Percent, bool OnAc, bool Charging, TimeSpan? Remaining,
    int DesignMwh, int FullMwh, int Cycles, int RateMw)
{
    public double Wear => DesignMwh > 0 ? Math.Max(0, 100 - FullMwh * 100.0 / DesignMwh) : 0;
}

public record PowerPlan(Guid Id, string Name, bool Active);

public static class Power
{
    // Windows 11 "Power mode" overlays
    public static readonly (string Key, string Name, Guid Id)[] Modes =
    {
        ("Saver", "Экономия", new Guid("961cc777-2547-4f9d-8174-7d86181b8a7a")),
        ("Balanced", "Баланс", Guid.Empty),
        ("Performance", "Производительность", new Guid("ded574b5-45a0-4f42-8737-46345c09c238")),
    };

    public static string Mode
    {
        get
        {
            Native.PowerGetEffectiveOverlayScheme(out var g);
            return Modes.FirstOrDefault(m => m.Id == g).Key ?? "Balanced";
        }
        set
        {
            var m = Modes.FirstOrDefault(x => x.Key == value);
            if (m.Key != null) Native.PowerSetActiveOverlayScheme(m.Id);
        }
    }

    public static string NextMode()
    {
        int i = Array.FindIndex(Modes, m => m.Key == Mode);
        var next = Modes[(i + 1) % Modes.Length];
        Mode = next.Key;
        return next.Name;
    }

    // Classic plans
    public static List<PowerPlan> Plans()
    {
        var res = new List<PowerPlan>();
        foreach (Match m in Regex.Matches(Shell.Run("powercfg", "/l"), @"([0-9a-f\-]{36})\s+\((.+?)\)\s*(\*)?"))
            res.Add(new(Guid.Parse(m.Groups[1].Value), m.Groups[2].Value, m.Groups[3].Success));
        return res;
    }

    public static void SetPlan(Guid id) => Shell.Run("powercfg", $"/setactive {id}");

    // Timeouts in minutes (0 = never)
    public static (int Ac, int Dc) ScreenTimeout => Query("SUB_VIDEO VIDEOIDLE");
    public static (int Ac, int Dc) SleepTimeout => Query("SUB_SLEEP STANDBYIDLE");

    static (int, int) Query(string what)
    {
        var hex = Regex.Matches(Shell.Run("powercfg", $"/q SCHEME_CURRENT {what}"), @":\s*0x([0-9a-fA-F]{8})").Select(m => Convert.ToInt32(m.Groups[1].Value, 16) / 60).ToList();
        return hex.Count >= 2 ? (hex[^2], hex[^1]) : (0, 0);
    }

    public static void SetScreenTimeout(int ac, int dc) { Shell.Run("powercfg", $"/change monitor-timeout-ac {ac}"); Shell.Run("powercfg", $"/change monitor-timeout-dc {dc}"); }
    public static void SetSleepTimeout(int ac, int dc) { Shell.Run("powercfg", $"/change standby-timeout-ac {ac}"); Shell.Run("powercfg", $"/change standby-timeout-dc {dc}"); }

    public static bool OnAc => System.Windows.Forms.SystemInformation.PowerStatus.PowerLineStatus != System.Windows.Forms.PowerLineStatus.Offline;

    static BatteryInfo? _bat;
    static DateTime _batAt;

    /// <summary>Cached for 5 s: four WMI queries are too slow for 1 s UI ticks.</summary>
    public static BatteryInfo Battery()
    {
        if (_bat != null && (DateTime.Now - _batAt).TotalSeconds < 5) return _bat;
        _batAt = DateTime.Now;
        return _bat = ReadBattery();
    }

    static BatteryInfo ReadBattery()
    {
        var ps = System.Windows.Forms.SystemInformation.PowerStatus;
        bool present = ps.BatteryChargeStatus != System.Windows.Forms.BatteryChargeStatus.NoSystemBattery;
        int design = 0, full = 0, cycles = 0, rate = 0;
        bool charging = false;
        try
        {
            design = WmiInt("BatteryStaticData", "DesignedCapacity");
            full = WmiInt("BatteryFullChargedCapacity", "FullChargedCapacity");
            cycles = WmiInt("BatteryCycleCount", "CycleCount");
            using var s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM BatteryStatus");
            foreach (ManagementObject o in s.Get())
            {
                charging = (bool)o["Charging"];
                rate = charging ? Convert.ToInt32(o["ChargeRate"]) : Convert.ToInt32(o["DischargeRate"]);
            }
        }
        catch { }
        var rem = ps.BatteryLifeRemaining > 0 ? TimeSpan.FromSeconds(ps.BatteryLifeRemaining) : (TimeSpan?)null;
        return new BatteryInfo(present, (int)Math.Round(ps.BatteryLifePercent * 100), ps.PowerLineStatus == System.Windows.Forms.PowerLineStatus.Online,
            charging, rem, design, full, cycles, rate);
    }

    static int WmiInt(string cls, string prop)
    {
        using var s = new ManagementObjectSearcher(@"root\wmi", $"SELECT {prop} FROM {cls}");
        foreach (ManagementObject o in s.Get()) return Convert.ToInt32(o[prop]);
        return 0;
    }
}
