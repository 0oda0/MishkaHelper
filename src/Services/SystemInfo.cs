using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace MishkaHelper.Core;

public record DiskHealth(string Name, string Media, string Health, double SizeGb, int? Temp, int? Wear, int? Hours);
public record ProcInfo(string Name, int Pid, double Cpu, double RamMb);

/// <summary>Static hardware/OS info and slower queries (disk health, top processes).</summary>
public static class SystemInfo
{
    static string? _model;

    static string Wmi(string query, string prop, string ns = @"root\cimv2")
    {
        try
        {
            using var s = new ManagementObjectSearcher(ns, query);
            return string.Join(", ", s.Get().Cast<ManagementObject>().Select(o => o[prop]?.ToString()?.Trim()).Where(x => !string.IsNullOrEmpty(x)));
        }
        catch { return "—"; }
    }

    public static string Model() => _model ??= $"{Wmi("SELECT Manufacturer FROM Win32_ComputerSystem", "Manufacturer")} {Wmi("SELECT Model FROM Win32_ComputerSystem", "Model")}";

    public static List<(string Key, string Value)> Summary()
    {
        var mem = new Native.MEMORYSTATUSEX();
        Native.GlobalMemoryStatusEx(mem);
        var os = Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Windows NT\CurrentVersion", "DisplayVersion", "") as string;
        var build = Environment.OSVersion.Version.Build;
        return new()
        {
            ("Устройство", Model()),
            ("Процессор", Wmi("SELECT Name FROM Win32_Processor", "Name")),
            ("Ядра / потоки", $"{Wmi("SELECT NumberOfCores FROM Win32_Processor", "NumberOfCores")} / {Environment.ProcessorCount}"),
            ("Видеокарты", Wmi("SELECT Name FROM Win32_VideoController", "Name")),
            ("Оперативная память", $"{mem.ullTotalPhys / 1073741824.0:0.0} ГБ"),
            ("Система", $"Windows {(build >= 22000 ? "11" : "10")} {os} (сборка {build})"),
            ("BIOS", Wmi("SELECT SMBIOSBIOSVersion FROM Win32_BIOS", "SMBIOSBIOSVersion")),
            ("Материнская плата", Wmi("SELECT Product FROM Win32_BaseBoard", "Product")),
            ("Время работы", Uptime()),
            ("IP-адрес", string.Join(", ", LocalIps())),
        };
    }

    static string Uptime()
    {
        var t = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return t.Days > 0 ? $"{t.Days} д {t.Hours} ч {t.Minutes} мин" : $"{t.Hours} ч {t.Minutes} мин";
    }

    static IEnumerable<string> LocalIps() =>
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString());

    public static List<DriveInfo> Drives() => DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable).ToList();

    /// <summary>Storage health from Windows' own reliability counters (no driver needed).</summary>
    public static List<DiskHealth> Disks()
    {
        var res = new List<DiskHealth>();
        try
        {
            const string ns = @"root\Microsoft\Windows\Storage";
            var rel = new Dictionary<string, ManagementObject>();
            using (var r = new ManagementObjectSearcher(ns, "SELECT * FROM MSFT_StorageReliabilityCounter"))
                foreach (ManagementObject o in r.Get()) rel[o["DeviceId"]?.ToString() ?? ""] = o;
            using var s = new ManagementObjectSearcher(ns, "SELECT * FROM MSFT_PhysicalDisk");
            foreach (ManagementObject o in s.Get())
            {
                var id = o["DeviceId"]?.ToString() ?? "";
                rel.TryGetValue(id, out var rc);
                int? I(string p) => rc?[p] is { } v && Convert.ToInt64(v) is var l && l > 0 ? (int)l : null;
                var media = Convert.ToInt32(o["MediaType"]) switch { 3 => "HDD", 4 => "SSD", 5 => "SCM", _ => "Диск" };
                if (Convert.ToInt32(o["BusType"]) == 17) media = "NVMe SSD";
                var health = Convert.ToInt32(o["HealthStatus"]) switch { 0 => "Исправен", 1 => "Внимание", 2 => "Неисправен", _ => "Неизвестно" };
                res.Add(new(o["FriendlyName"]?.ToString() ?? "?", media, health, Convert.ToDouble(o["Size"]) / 1e9,
                    I("Temperature"), I("Wear"), I("PowerOnHours")));
            }
        }
        catch (Exception e) { Log.Error(e); }
        return res;
    }

    static Dictionary<int, TimeSpan> _cpuPrev = new();
    static DateTime _cpuAt;

    /// <summary>Top processes by CPU since last call (call every ~2 s).</summary>
    public static List<ProcInfo> TopProcesses(int n = 12)
    {
        var now = DateTime.Now;
        double secs = Math.Max(0.5, (now - _cpuAt).TotalSeconds) * Environment.ProcessorCount;
        var cur = new Dictionary<int, TimeSpan>();
        var res = new List<ProcInfo>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == 0) continue;
                var t = p.TotalProcessorTime;
                cur[p.Id] = t;
                double cpu = _cpuPrev.TryGetValue(p.Id, out var prev) ? (t - prev).TotalSeconds / secs * 100 : 0;
                res.Add(new(p.ProcessName, p.Id, cpu, p.WorkingSet64 / 1048576.0));
            }
            catch { }
            finally { p.Dispose(); }
        }
        _cpuPrev = cur; _cpuAt = now;
        return res.OrderByDescending(p => p.Cpu).ThenByDescending(p => p.RamMb).Take(n).ToList();
    }
}
