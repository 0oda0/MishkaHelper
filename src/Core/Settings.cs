using System.IO;
using System.Text.Json;

namespace MishkaHelper.Core;

public class Profile
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public int? Volume { get; set; }
    public int? Brightness { get; set; }
    public string? PowerMode { get; set; }      // Windows overlay: Saver/Balanced/Performance
    public int? AcerMode { get; set; }          // NitroSense thermal profile id
    public int? RefreshRate { get; set; }
    public bool? DoNotDisturb { get; set; }
    public bool? NightLight { get; set; }
    public string? AudioDevice { get; set; }    // endpoint id
    public bool CleanOnApply { get; set; }
}

public class AppRule
{
    public string Process { get; set; } = "";   // e.g. "cs2"
    public string Profile { get; set; } = "";
}

public class Launcher
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Args { get; set; } = "";
}

public class WidgetConfig
{
    public string Kind { get; set; } = "";
    public double Left { get; set; } = 100;
    public double Top { get; set; } = 100;
    public double Opacity { get; set; } = 0.92;
    public bool Topmost { get; set; }
    public bool Visible { get; set; }
}

public class AlertSettings
{
    public bool Enabled { get; set; } = true;
    public int CpuTemp { get; set; } = 92;
    public int GpuTemp { get; set; } = 87;
    public int DiskFreeGb { get; set; } = 10;
    public int HeadsetBattery { get; set; } = 15;
    public int RamPercent { get; set; } = 92;
}

public class AppSettings
{
    public string Accent { get; set; } = "Purple";
    public bool Glass { get; set; }
    public bool StartWithWindows { get; set; } = true;
    public bool StartMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool Osd { get; set; } = true;
    public bool AutoSwitchAudio { get; set; } = true;
    public int HistoryDays { get; set; } = 7;

    public Dictionary<string, string> Hotkeys { get; set; } = new();
    public List<Profile> Profiles { get; set; } = new();
    public bool AutoProfiles { get; set; }
    public string? OnBatteryProfile { get; set; }
    public string? OnAcProfile { get; set; }
    public List<AppRule> AppRules { get; set; } = new();
    public List<Launcher> Launchers { get; set; } = new();
    public List<WidgetConfig> Widgets { get; set; } = new();
    public AlertSettings Alerts { get; set; } = new();

    public string VirusTotalKey { get; set; } = "";
    public int ScanIntervalDays { get; set; }
    public DateTime LastScan { get; set; }
    public int CleanIntervalDays { get; set; }
    public DateTime LastClean { get; set; }
    public List<string> CleanTargets { get; set; } = new();

    public string WeatherCity { get; set; } = "Москва";
}

public static class Settings
{
    public static readonly string Dir = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MishkaHelper");
    static readonly string FilePath = System.IO.Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch
        {
            // Corrupt file: keep a copy so the user doesn't silently lose it, start fresh.
            try { File.Copy(FilePath, FilePath + ".bad", true); } catch { }
            Current = new();
        }
        Defaults.Fill(Current);
    }

    public static void Save()
    {
        Directory.CreateDirectory(Dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
        File.Move(tmp, FilePath, true);
    }
}
