using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Microsoft.Win32;

namespace MishkaHelper.Core;

public record WifiProfile(string Name, string Password);
public record CrashEvent(DateTime When, string Kind, string Details);
public record SensorUse(string App, string Device, bool Active, DateTime? LastUsed);

/// <summary>Small system utilities used by the Tools page, hotkeys and the command palette.</summary>
public static class Toolbox
{
    // ---------- windows ----------
    [DllImport("user32.dll")] static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [DllImport("user32.dll")] static extern bool GetLayeredWindowAttributes(IntPtr h, out uint key, out byte alpha, out uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern IntPtr PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern bool LockWorkStation();
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr h);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr h, IntPtr dc);
    [DllImport("gdi32.dll")] static extern uint GetPixel(IntPtr dc, int x, int y);
    [DllImport("kernel32.dll")] static extern uint SetThreadExecutionState(uint flags);
    [DllImport("user32.dll")] static extern short GetKeyState(int vk);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindow(string? cls, string? name);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? cls, string? name);
    [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("shell32.dll")] static extern uint SHAppBarMessage(uint msg, ref APPBARDATA d);
    [StructLayout(LayoutKind.Sequential)] struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage, uEdge; public int l, t, r, b; public IntPtr lParam; }

    const int GWL_EXSTYLE = -20, WS_EX_TOPMOST = 0x8, WS_EX_LAYERED = 0x80000;

    static string Title(IntPtr h) { var sb = new StringBuilder(200); GetWindowText(h, sb, 200); return sb.Length > 0 ? sb.ToString() : "окно"; }

    /// <summary>Pin/unpin the foreground window above everything.</summary>
    public static string ToggleTopmost()
    {
        var h = Native.GetForegroundWindow();
        bool on = (GetWindowLong(h, GWL_EXSTYLE) & WS_EX_TOPMOST) == 0;
        Native.SetWindowPos(h, new IntPtr(on ? -1 : -2), 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
        return (on ? "Поверх всех: " : "Обычное окно: ") + Title(h);
    }

    /// <summary>Cycle the foreground window opacity 100 → 85 → 70 → 55 → 100%.</summary>
    public static int CycleOpacity()
    {
        var h = Native.GetForegroundWindow();
        int ex = GetWindowLong(h, GWL_EXSTYLE);
        byte cur = 255;
        if ((ex & WS_EX_LAYERED) != 0 && GetLayeredWindowAttributes(h, out _, out var a, out var f) && (f & 2) != 0) cur = a;
        byte next = cur > 230 ? (byte)217 : cur > 200 ? (byte)178 : cur > 160 ? (byte)140 : (byte)255;
        SetWindowLong(h, GWL_EXSTYLE, ex | WS_EX_LAYERED);
        SetLayeredWindowAttributes(h, 0, next, 2);
        return next * 100 / 255;
    }

    /// <summary>Kill the app owning the foreground window (games that froze in fullscreen).</summary>
    public static string KillForeground()
    {
        GetWindowThreadProcessId(Native.GetForegroundWindow(), out var pid);
        using var p = Process.GetProcessById((int)pid);
        if (p.Id == Environment.ProcessId || p.ProcessName.Equals("explorer", StringComparison.OrdinalIgnoreCase)) return "Это окно не трогаю";
        var name = p.ProcessName;
        p.Kill(true);
        return "Закрыто: " + name;
    }

    public static (string Hex, System.Windows.Media.Color Color) PickColor()
    {
        Native.GetCursorPos(out var pt);
        var dc = GetDC(IntPtr.Zero);
        uint c = GetPixel(dc, pt.X, pt.Y);
        ReleaseDC(IntPtr.Zero, dc);
        var col = System.Windows.Media.Color.FromRgb((byte)(c & 0xFF), (byte)(c >> 8 & 0xFF), (byte)(c >> 16 & 0xFF));
        var hex = $"#{col.R:X2}{col.G:X2}{col.B:X2}";
        System.Windows.Clipboard.SetText(hex);
        return (hex, col);
    }

    // ---------- power ----------
    public static void ScreenOff() => PostMessage(new IntPtr(0xFFFF), 0x0112, new IntPtr(0xF170), new IntPtr(2));
    public static void Lock() => LockWorkStation();

    static bool _awake;
    public static bool KeepAwake
    {
        get => _awake;
        set
        {
            _awake = value;
            // ES_CONTINUOUS | ES_SYSTEM_REQUIRED | ES_DISPLAY_REQUIRED; called on the UI thread, which lives as long as the app.
            SetThreadExecutionState(value ? 0x80000003u : 0x80000000u);
        }
    }

    public static DateTime? TimerAt { get; private set; }
    public static string TimerKind { get; private set; } = "";
    static DispatcherTimer? _powerTimer;

    /// <summary>kind: shutdown / restart / sleep / hibernate. minutes &lt;= 0 cancels.</summary>
    public static void SchedulePower(string kind, int minutes)
    {
        _powerTimer?.Stop();
        TimerAt = null;
        if (minutes <= 0) return;
        TimerAt = DateTime.Now.AddMinutes(minutes);
        TimerKind = kind;
        _powerTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        bool warned = false;
        _powerTimer.Tick += (_, _) =>
        {
            var left = TimerAt!.Value - DateTime.Now;
            if (!warned && left.TotalSeconds <= 60) { warned = true; Toast.Show("\uE7E8", "Через минуту: " + PowerName(kind), "Отменить можно в «Инструментах»", () => App.Current.Navigate("Tools")); }
            if (left > TimeSpan.Zero) return;
            _powerTimer!.Stop();
            TimerAt = null;
            switch (kind)
            {
                case "shutdown": Shell.Run("shutdown", "/s /t 0"); break;
                case "restart": Shell.Run("shutdown", "/r /t 0"); break;
                case "hibernate": System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Hibernate, false, false); break;
                default: System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false); break;
            }
        };
        _powerTimer.Start();
    }

    public static string PowerName(string kind) => kind switch { "shutdown" => "выключение", "restart" => "перезагрузка", "hibernate" => "гибернация", _ => "сон" };

    public static bool Hibernation => File.Exists(@"C:\hiberfil.sys");
    public static long HiberSize { get { try { return new FileInfo(@"C:\hiberfil.sys").Length; } catch { return 0; } } }
    public static Task SetHibernation(bool on) => Shell.RunAsync("powercfg", on ? "/h on" : "/h off");

    public static async Task<string> BatteryReport()
    {
        var path = Path.Combine(Path.GetTempPath(), "battery-report.html");
        await Shell.RunAsync("powercfg", $"/batteryreport /output \"{path}\"");
        return path;
    }

    // ---------- shell ----------
    /// <summary>Explorer restarts itself (AutoRestartShell) — starting it from an elevated app would make it elevated.</summary>
    public static void RestartExplorer()
    {
        foreach (var p in Process.GetProcessesByName("explorer")) { try { p.Kill(); } catch { } p.Dispose(); }
    }

    public static bool TaskbarAutoHide
    {
        get { var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>() }; return (SHAppBarMessage(4, ref d) & 1) != 0; }
        set { var d = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), lParam = new IntPtr(value ? 1 : 2) }; SHAppBarMessage(10, ref d); }
    }

    /// <summary>Same as desktop right-click → View → Show desktop icons.</summary>
    public static void ToggleDesktopIcons()
    {
        var view = FindWindowEx(FindWindow("Progman", null), IntPtr.Zero, "SHELLDLL_DefView", null);
        if (view == IntPtr.Zero)
        {
            // With a slideshow wallpaper the view lives under one of the WorkerW windows.
            IntPtr w = IntPtr.Zero;
            while ((w = FindWindowEx(IntPtr.Zero, w, "WorkerW", null)) != IntPtr.Zero && view == IntPtr.Zero)
                view = FindWindowEx(w, IntPtr.Zero, "SHELLDLL_DefView", null);
        }
        if (view != IntPtr.Zero) SendMessage(view, 0x0111, new IntPtr(0x7402), IntPtr.Zero);
    }

    public static bool CapsLock => (GetKeyState(0x14) & 1) != 0;
    public static bool NumLock => (GetKeyState(0x90) & 1) != 0;

    public static void EmptyRecycleBin() => Native.SHEmptyRecycleBin(IntPtr.Zero, null, 7);

    /// <summary>Visible console so the user sees SFC/DISM/defrag progress.</summary>
    public static void RunVisible(string cmd) =>
        Process.Start(new ProcessStartInfo("cmd.exe", $"/k title Mishka Helper && {cmd}") { UseShellExecute = true });

    public static async Task<string> CreateRestorePoint()
    {
        var o = await Shell.RunAsync("powershell", "-NoProfile -Command \"try { Checkpoint-Computer -Description 'Mishka Helper' -RestorePointType MODIFY_SETTINGS -ErrorAction Stop; 'OK' } catch { $_.Exception.Message }\"", 5 * 60_000, Encoding.UTF8);
        return o.Trim();
    }

    // ---------- network ----------
    public static List<WifiProfile> WifiPasswords()
    {
        var res = new List<WifiProfile>();
        var names = Regex.Matches(Shell.Run("netsh", "wlan show profiles"), @"^\s*(?:All User Profile|Все профили пользователей|Профиль всех пользователей)\s*:\s*(.+)$", RegexOptions.Multiline)
            .Select(m => m.Groups[1].Value.Trim());
        foreach (var n in names)
        {
            var o = Shell.Run("netsh", $"wlan show profile name=\"{n}\" key=clear");
            var key = Regex.Match(o, @"^\s*(?:Key Content|Содержимое ключа)\s*:\s*(.+)$", RegexOptions.Multiline);
            res.Add(new(n, key.Success ? key.Groups[1].Value.Trim() : "(нет пароля)"));
        }
        return res;
    }

    public static async Task<long> Ping(string host)
    {
        try
        {
            using var p = new Ping();
            var r = await p.SendPingAsync(host, 2000);
            return r.Status == IPStatus.Success ? r.RoundtripTime : -1;
        }
        catch { return -1; }
    }

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>Download speed via Cloudflare's public speed endpoint (no key, ~25 MB).</summary>
    public static async Task<double> SpeedTestMbps(Action<string>? progress = null)
    {
        const int bytes = 25_000_000;
        var sw = Stopwatch.StartNew();
        using var resp = await Http.GetAsync($"https://speed.cloudflare.com/__down?bytes={bytes}", HttpCompletionOption.ResponseHeadersRead);
        await using var s = await resp.Content.ReadAsStreamAsync();
        var buf = new byte[81920];
        long total = 0;
        int n;
        while ((n = await s.ReadAsync(buf)) > 0)
        {
            total += n;
            if (total % (2_000_000) < n) progress?.Invoke($"{total * 100 / bytes}%");
        }
        return total * 8 / 1e6 / sw.Elapsed.TotalSeconds;
    }

    public static async Task<string> PublicIp()
    {
        try { return (await Http.GetStringAsync("https://api.ipify.org")).Trim(); } catch { return "—"; }
    }

    public static Task ResetNetwork() => Task.Run(() =>
    {
        Shell.Run("netsh", "winsock reset");
        Shell.Run("netsh", "int ip reset");
        Shell.Run("ipconfig", "/flushdns");
    });

    // ---------- diagnostics ----------
    public static List<CrashEvent> Crashes(int days = 30)
    {
        var res = new List<CrashEvent>();
        long ms = days * 86_400_000L;
        void Q(string log, string xpath, Func<EventRecord, CrashEvent?> map)
        {
            try
            {
                using var r = new EventLogReader(new EventLogQuery(log, PathType.LogName, xpath) { ReverseDirection = true });
                for (int i = 0; i < 50 && r.ReadEvent() is { } e; i++) using (e) if (map(e) is { } c) res.Add(c);
            }
            catch { }
        }
        Q("System", $"*[System[(EventID=41) and TimeCreated[timediff(@SystemTime) <= {ms}]]]",
            e => new(e.TimeCreated ?? default, "Внезапное выключение", "Питание пропало или ПК завис (Kernel-Power 41)"));
        Q("System", $"*[System[(EventID=1001) and Provider[@Name='Microsoft-Windows-WER-SystemErrorReporting'] and TimeCreated[timediff(@SystemTime) <= {ms}]]]",
            e => new(e.TimeCreated ?? default, "Синий экран (BSOD)", Short(e)));
        Q("Application", $"*[System[(EventID=1000) and TimeCreated[timediff(@SystemTime) <= {ms}]]]",
            e => new(e.TimeCreated ?? default, "Вылет программы", e.Properties.Count > 0 ? e.Properties[0].Value?.ToString() ?? "" : ""));
        return res.OrderByDescending(c => c.When).ToList();
    }

    static string Short(EventRecord e)
    {
        try { var d = e.FormatDescription() ?? ""; return d.Length > 160 ? d[..160] + "…" : d; } catch { return ""; }
    }

    /// <summary>Boot duration of recent starts from the Diagnostics-Performance log (event 100).</summary>
    public static List<(DateTime When, double Seconds)> BootTimes()
    {
        var res = new List<(DateTime, double)>();
        try
        {
            using var r = new EventLogReader(new EventLogQuery("Microsoft-Windows-Diagnostics-Performance/Operational", PathType.LogName, "*[System[(EventID=100)]]") { ReverseDirection = true });
            for (int i = 0; i < 10 && r.ReadEvent() is { } e; i++)
                using (e)
                    if (e.Properties.Count > 5 && double.TryParse(e.Properties[5].Value?.ToString(), out var bootMs))
                        res.Add((e.TimeCreated ?? default, bootMs / 1000));
        }
        catch { }
        return res;
    }

    // ---------- privacy ----------
    /// <summary>Which apps use (or recently used) the camera and microphone — the same data Windows shows in Privacy settings.</summary>
    public static List<SensorUse> SensorUsage()
    {
        var res = new List<SensorUse>();
        const string root = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\";
        foreach (var (cap, dev) in new[] { ("webcam", "Камера"), ("microphone", "Микрофон") })
        {
            void Scan(RegistryKey? k, bool nonPackaged)
            {
                if (k == null) return;
                foreach (var sub in k.GetSubKeyNames())
                {
                    if (sub == "NonPackaged") continue;
                    using var a = k.OpenSubKey(sub);
                    if (a?.GetValue("LastUsedTimeStart") is not long start) continue;
                    var stop = a.GetValue("LastUsedTimeStop") as long? ?? 0;
                    var name = nonPackaged ? Path.GetFileNameWithoutExtension(sub.Replace('#', '\\')) : sub.Split('_')[0];
                    res.Add(new(name, dev, stop == 0 && start > 0, start > 0 ? DateTime.FromFileTime(start) : null));
                }
            }
            using var k = Registry.CurrentUser.OpenSubKey(root + cap);
            Scan(k, false);
            using var np = Registry.CurrentUser.OpenSubKey(root + cap + @"\NonPackaged");
            Scan(np, true);
        }
        return res.OrderByDescending(r => r.Active).ThenByDescending(r => r.LastUsed).ToList();
    }

    // ---------- misc ----------
    public static string Password(int length, bool symbols)
    {
        const string letters = "abcdefghijkmnopqrstuvwxyzABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        var alphabet = letters + (symbols ? "!@#$%^&*-_=+?" : "");
        return new string(Enumerable.Range(0, length).Select(_ => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)]).ToArray());
    }

    public static async Task<(string Md5, string Sha1, string Sha256)> Hashes(string file) => await Task.Run(() =>
    {
        using var s = File.OpenRead(file);
        var md5 = Convert.ToHexString(MD5.HashData(s)); s.Position = 0;
        var sha1 = Convert.ToHexString(SHA1.HashData(s)); s.Position = 0;
        var sha256 = Convert.ToHexString(SHA256.HashData(s));
        return (md5, sha1, sha256);
    });

    public static string SystemReport()
    {
        var sb = new StringBuilder("Mishka Helper — сведения о ПК\n");
        foreach (var (k, v) in SystemInfo.Summary()) sb.AppendLine($"{k}: {v}");
        foreach (var d in SystemInfo.Drives()) sb.AppendLine($"Диск {d.Name} {Cleaner.Size(d.AvailableFreeSpace)} свободно из {Cleaner.Size(d.TotalSize)}");
        var b = Power.Battery();
        if (b.Present && b.DesignMwh > 0) sb.AppendLine($"Батарея: износ {b.Wear:0}%, циклов {b.Cycles}");
        return sb.ToString();
    }

    public static List<string> EmptyFolders(string root, CancellationToken ct)
    {
        var res = new List<string>();
        void Walk(string d)
        {
            ct.ThrowIfCancellationRequested();
            string[] subs;
            try { subs = Directory.GetDirectories(d); } catch { return; }
            foreach (var s in subs)
            {
                try { if ((File.GetAttributes(s) & (FileAttributes.ReparsePoint | FileAttributes.System)) != 0) continue; } catch { continue; }
                Walk(s);
                try { if (!Directory.EnumerateFileSystemEntries(s).Any()) res.Add(s); } catch { }
            }
        }
        Walk(root);
        return res;
    }
}
