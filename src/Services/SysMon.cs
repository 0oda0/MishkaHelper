using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;

namespace MishkaHelper.Core;

public record Sample(DateTime Time, double Cpu, double Ram, double Gpu, int CpuTemp, int GpuTemp, int SysTemp,
    int CpuFan, int GpuFan, double DiskPct, double DiskRead, double DiskWrite, double NetDown, double NetUp,
    double RamUsedGb, double RamTotalGb, double VramUsedGb, double CpuMhz);

/// <summary>
/// Samples system load. 1 s while a UI wants live data, 5 s in the tray.
/// Keeps per-minute history on disk for <see cref="AppSettings.HistoryDays"/> days.
/// </summary>
public static class SysMon
{
    public static Sample Last { get; private set; } = new(DateTime.Now, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    public static event Action<Sample>? Updated;

    /// <summary>Recent samples (~5 min) for live charts.</summary>
    public static readonly LinkedList<Sample> Recent = new();

    static int _liveUsers;
    static Timer? _timer;
    static long _idle, _kernel, _user;
    static long _netRx, _netTx;
    static DateTime _netAt;
    static PerformanceCounter? _diskPct, _diskRead, _diskWrite, _cpuPerf;
    static List<PerformanceCounter> _gpu = new(), _vram = new();
    static DateTime _gpuEnumAt;
    static List<PerformanceCounter> _thermal = new();
    static double _baseMhz;
    static readonly List<Sample> _minute = new();

    public static void Start()
    {
        Task.Run(() =>
        {
            try
            {
                _diskPct = new("PhysicalDisk", "% Idle Time", "_Total");
                _diskRead = new("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
                _diskWrite = new("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
                _cpuPerf = new("Processor Information", "% Processor Performance", "_Total");
                using var cpu = new System.Management.ManagementObjectSearcher("SELECT MaxClockSpeed FROM Win32_Processor");
                foreach (var o in cpu.Get()) _baseMhz = Convert.ToDouble(o["MaxClockSpeed"]);
                if (!Acer.Available && PerformanceCounterCategory.Exists("Thermal Zone Information"))
                    _thermal = new PerformanceCounterCategory("Thermal Zone Information").GetInstanceNames()
                        .Select(n => new PerformanceCounter("Thermal Zone Information", "Temperature", n)).ToList();
            }
            catch (Exception e) { Log.Error(e); }
            History.Load();
            _timer = new Timer(_ => Tick(), null, 500, 5000);
        });
    }

    public static void Stop() { _timer?.Dispose(); History.Flush(_minute); }

    /// <summary>Call from a page/widget that shows live data; dispose to release.</summary>
    public static IDisposable Live()
    {
        if (Interlocked.Increment(ref _liveUsers) == 1) _timer?.Change(0, 1000);
        return new Releaser();
    }

    sealed class Releaser : IDisposable
    {
        bool _done;
        public void Dispose()
        {
            if (_done) return;
            _done = true;
            if (Interlocked.Decrement(ref _liveUsers) == 0) _timer?.Change(5000, 5000);
        }
    }

    static readonly object TickLock = new();

    static void Tick()
    {
        if (!Monitor_TryEnter()) return;
        try
        {
            var s = Read();
            Last = s;
            lock (Recent)
            {
                Recent.AddLast(s);
                while (Recent.Count > 0 && (s.Time - Recent.First!.Value.Time).TotalMinutes > 5) Recent.RemoveFirst();
            }
            _minute.Add(s);
            if (_minute.Count > 0 && _minute[0].Time.Minute != s.Time.Minute) { History.Flush(_minute); _minute.Clear(); }
            Alerts.Check(s);
            Updated?.Invoke(s);
        }
        catch (Exception e) { Log.Error(e); }
        finally { System.Threading.Monitor.Exit(TickLock); }
    }

    static bool Monitor_TryEnter() => System.Threading.Monitor.TryEnter(TickLock);

    static Sample Read()
    {
        // CPU
        Native.GetSystemTimes(out var idle, out var kernel, out var user);
        long di = idle - _idle, dt = (kernel - _kernel) + (user - _user);
        double cpu = _kernel == 0 || dt == 0 ? 0 : Math.Clamp(100.0 * (dt - di) / dt, 0, 100);
        (_idle, _kernel, _user) = (idle, kernel, user);

        var mem = new Native.MEMORYSTATUSEX();
        Native.GlobalMemoryStatusEx(mem);
        double totalGb = mem.ullTotalPhys / 1073741824.0, usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1073741824.0;

        // Network
        long rx = 0, tx = 0;
        foreach (var n in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (n.OperationalStatus != OperationalStatus.Up || n.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            var st = n.GetIPv4Statistics();
            rx += st.BytesReceived; tx += st.BytesSent;
        }
        var now = DateTime.Now;
        double secs = (now - _netAt).TotalSeconds;
        double down = _netAt == default || secs <= 0 ? 0 : Math.Max(0, (rx - _netRx) / secs);
        double up = _netAt == default || secs <= 0 ? 0 : Math.Max(0, (tx - _netTx) / secs);
        (_netRx, _netTx, _netAt) = (rx, tx, now);

        // GPU: "GPU Engine" counters don't wake a sleeping dGPU (NVML would).
        if ((now - _gpuEnumAt).TotalSeconds > 15) EnumGpu(now);
        double gpu = 0, vram = 0;
        try
        {
            // Sum all processes per adapter (luid), report the busiest adapter.
            gpu = Math.Min(100, _gpu.GroupBy(c => Luid(c.InstanceName)).Select(g => g.Sum(c => Next(c))).DefaultIfEmpty(0).Max());
            vram = _vram.Sum(c => Next(c)) / 1073741824.0;
        }
        catch { _gpuEnumAt = default; }

        int cpuT = 0, gpuT = 0, sysT = 0, cpuFan = 0, gpuFan = 0;
        if (Acer.Available)
        {
            cpuT = Acer.CpuTemp; gpuT = Acer.GpuTemp; sysT = Acer.SysTemp; cpuFan = Acer.CpuFanRpm; gpuFan = Acer.GpuFanRpm;
        }
        else if (_thermal.Count > 0)
        {
            try { cpuT = (int)(_thermal.Max(c => c.NextValue()) - 273.15); } catch { }
        }

        double diskPct = 0, dr = 0, dw = 0, mhz = 0;
        try
        {
            diskPct = 100 - Math.Clamp(_diskPct?.NextValue() ?? 100, 0, 100);
            dr = _diskRead?.NextValue() ?? 0; dw = _diskWrite?.NextValue() ?? 0;
            mhz = _baseMhz * (_cpuPerf?.NextValue() ?? 0) / 100;
        }
        catch { }

        return new Sample(now, cpu, mem.dwMemoryLoad, gpu, cpuT, gpuT, sysT, cpuFan, gpuFan, diskPct, dr, dw, down, up,
            usedGb, totalGb, vram, mhz);
    }

    static float Next(PerformanceCounter c) { try { return c.NextValue(); } catch { return 0; } }

    // "pid_123_luid_0x0_0xC6A2_phys_0_eng_0_engtype_3D" → "luid_0x0_0xC6A2"
    static string Luid(string n)
    {
        int a = n.IndexOf("luid_"), b = n.IndexOf("_phys");
        return a >= 0 && b > a ? n[a..b] : n;
    }

    static void EnumGpu(DateTime now)
    {
        _gpuEnumAt = now;
        foreach (var c in _gpu.Concat(_vram)) c.Dispose();
        _gpu = new(); _vram = new();
        try
        {
            if (PerformanceCounterCategory.Exists("GPU Engine"))
                _gpu = new PerformanceCounterCategory("GPU Engine").GetInstanceNames()
                    .Where(n => n.EndsWith("engtype_3D"))
                    .Select(n => new PerformanceCounter("GPU Engine", "Utilization Percentage", n)).ToList();
            if (PerformanceCounterCategory.Exists("GPU Adapter Memory"))
                _vram = new PerformanceCounterCategory("GPU Adapter Memory").GetInstanceNames()
                    .Select(n => new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", n)).ToList();
            foreach (var c in _gpu) Next(c);   // prime; instances of exited processes vanish between enumerate and read
        }
        catch (Exception e) { Log.Error(e); }
    }

    public static string Speed(double bytesPerSec) => bytesPerSec switch
    {
        >= 1048576 => $"{bytesPerSec / 1048576:0.0} МБ/с",
        >= 1024 => $"{bytesPerSec / 1024:0} КБ/с",
        _ => $"{bytesPerSec:0} Б/с",
    };
}

/// <summary>Per-minute averages on disk: one CSV line per minute, trimmed on load.</summary>
public static class History
{
    static readonly string FilePath = Path.Combine(Settings.Dir, "history.csv");
    public record Point(DateTime Time, double Cpu, double Ram, double Gpu, int CpuTemp, int GpuTemp);

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var cutoff = DateTime.Now.AddDays(-Settings.Current.HistoryDays);
            var keep = File.ReadAllLines(FilePath).Where(l => Parse(l) is { } p && p.Time > cutoff).ToArray();
            File.WriteAllLines(FilePath, keep);
        }
        catch (Exception e) { Log.Error(e); }
    }

    public static void Flush(List<Sample> minute)
    {
        if (minute.Count == 0) return;
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            var t = minute[0].Time;
            File.AppendAllText(FilePath, string.Join(';',
                new DateTime(t.Year, t.Month, t.Day, t.Hour, t.Minute, 0).ToString("o"),
                (int)minute.Average(x => x.Cpu), (int)minute.Average(x => x.Ram), (int)minute.Average(x => x.Gpu),
                minute.Max(x => x.CpuTemp), minute.Max(x => x.GpuTemp)) + "\n");
        }
        catch (Exception e) { Log.Error(e); }
    }

    static Point? Parse(string l)
    {
        var p = l.Split(';');
        if (p.Length < 6 || !DateTime.TryParse(p[0], null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)) return null;
        return new Point(t, double.Parse(p[1]), double.Parse(p[2]), double.Parse(p[3]), int.Parse(p[4]), int.Parse(p[5]));
    }

    public static List<Point> Read(TimeSpan span)
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            var from = DateTime.Now - span;
            return File.ReadLines(FilePath).Select(Parse).Where(p => p != null && p.Time >= from).Cast<Point>().ToList();
        }
        catch { return new(); }
    }
}
