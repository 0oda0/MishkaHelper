using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MishkaHelper.Core;

public record DefenderStatus(bool Enabled, bool RealTime, string SigVersion, DateTime SigUpdated, DateTime LastQuick, DateTime LastFull, string OtherAv);
public record Threat(string Name, string Severity, string Resources, DateTime When, bool Resolved, long Id);
public record Suspect(string Name, string Path, string Where, List<string> Reasons, int Score, StartupItem? Startup = null, int? Pid = null)
{
    public string ReasonText => string.Join(" · ", Reasons);
}
public record VtResult(bool Found, int Malicious, int Suspicious, int Clean, string Link, string? Error = null);

public static class Security
{
    static string MpCmd
    {
        get
        {
            // The platform folder carries the newest MpCmdRun; Program Files is the fallback.
            var plat = Environment.ExpandEnvironmentVariables(@"%ProgramData%\Microsoft\Windows Defender\Platform");
            if (Directory.Exists(plat))
            {
                var newest = Directory.GetDirectories(plat).OrderByDescending(d => d).Select(d => Path.Combine(d, "MpCmdRun.exe")).FirstOrDefault(File.Exists);
                if (newest != null) return newest;
            }
            return Environment.ExpandEnvironmentVariables(@"%ProgramFiles%\Windows Defender\MpCmdRun.exe");
        }
    }

    public static DefenderStatus Status()
    {
        bool en = false, rt = false;
        string ver = "?";
        DateTime upd = default, q = default, f = default;
        try
        {
            using var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT * FROM MSFT_MpComputerStatus");
            foreach (ManagementObject o in s.Get())
            {
                en = (bool)o["AntivirusEnabled"];
                rt = (bool)o["RealTimeProtectionEnabled"];
                ver = o["AntivirusSignatureVersion"]?.ToString() ?? "?";
                upd = Dmtf(o["AntivirusSignatureLastUpdated"]);
                q = Dmtf(o["QuickScanEndTime"]);
                f = Dmtf(o["FullScanEndTime"]);
            }
        }
        catch (Exception e) { Log.Error(e); }

        var other = new List<string>();
        try
        {
            using var s = new ManagementObjectSearcher(@"root\SecurityCenter2", "SELECT displayName FROM AntiVirusProduct");
            foreach (ManagementObject o in s.Get())
                if (o["displayName"] is string n && !n.Contains("Defender", StringComparison.OrdinalIgnoreCase) && !n.Contains("Microsoft")) other.Add(n);
        }
        catch { }
        return new(en, rt, ver, upd, q, f, string.Join(", ", other));
    }

    static DateTime Dmtf(object? v) => v is string s && s.Length > 0 ? ManagementDateTimeConverter.ToDateTime(s) : default;

    public enum ScanKind { Quick = 1, Full = 2, Custom = 3 }

    /// <summary>Runs a Defender scan; returns (threatsFound, raw output).</summary>
    public static async Task<(bool Threats, string Output)> Scan(ScanKind kind, string? path = null)
    {
        var args = $"-Scan -ScanType {(int)kind}" + (kind == ScanKind.Custom ? $" -File \"{path}\"" : "");
        var outp = await Shell.RunAsync(MpCmd, args, 24 * 3600_000, System.Text.Encoding.Unicode);
        if (string.IsNullOrWhiteSpace(outp)) outp = await Shell.RunAsync(MpCmd, args, 24 * 3600_000);
        bool threats = Regex.IsMatch(outp, @"found \d+ threats|Threat\s+:", RegexOptions.IgnoreCase) && !outp.Contains("found no threats", StringComparison.OrdinalIgnoreCase);
        if (kind != ScanKind.Custom) { Settings.Current.LastScan = DateTime.Now; Settings.Save(); }
        return (threats, outp);
    }

    public static Task CancelScan() => Shell.RunAsync(MpCmd, "-Cancel");
    public static Task<string> UpdateSignatures() => Shell.RunAsync(MpCmd, "-SignatureUpdate", 10 * 60_000);

    public static List<Threat> Threats()
    {
        var res = new List<Threat>();
        try
        {
            var names = new Dictionary<long, (string, string)>();
            using (var s = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT * FROM MSFT_MpThreat"))
                foreach (ManagementObject o in s.Get())
                    names[Convert.ToInt64(o["ThreatID"])] = (o["ThreatName"]?.ToString() ?? "?", Severity(Convert.ToInt32(o["SeverityID"])));
            using var d = new ManagementObjectSearcher(@"root\Microsoft\Windows\Defender", "SELECT * FROM MSFT_MpThreatDetection");
            foreach (ManagementObject o in d.Get())
            {
                var id = Convert.ToInt64(o["ThreatID"]);
                var (n, sev) = names.TryGetValue(id, out var x) ? x : ("?", "?");
                var resources = o["Resources"] is string[] r ? string.Join("\n", r.Select(z => Regex.Replace(z, @"^\w+:_", ""))) : "";
                res.Add(new(n, sev, resources, Dmtf(o["InitialDetectionTime"]), (bool)(o["ActionSuccess"] ?? false), id));
            }
        }
        catch (Exception e) { Log.Error(e); }
        return res.OrderByDescending(t => t.When).ToList();
    }

    static string Severity(int id) => id switch { 1 => "Низкая", 2 => "Средняя", 4 => "Высокая", 5 => "Критическая", _ => "Неизвестно" };

    public static Task RemoveThreat(long id) => Shell.RunAsync("powershell", $"-NoProfile -Command \"Remove-MpThreat -ThreatID {id}\"");

    public static async Task<List<string>> Quarantine()
    {
        var o = await Shell.RunAsync(MpCmd, "-Restore -ListAll", 60_000, System.Text.Encoding.Unicode);
        if (!o.Contains("ThreatName", StringComparison.OrdinalIgnoreCase)) o = await Shell.RunAsync(MpCmd, "-Restore -ListAll", 60_000);
        return Regex.Matches(o, @"ThreatName\s*=\s*(.+)").Select(m => m.Groups[1].Value.Trim()).Distinct().ToList();
    }

    public static Task<string> Restore(string threatName) => Shell.RunAsync(MpCmd, $"-Restore -Name \"{threatName}\"");

    // VirusTotal: hash lookup only; the file itself never leaves the PC.
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<VtResult> VirusTotal(string file)
    {
        var hash = await Task.Run(() => Cleaner.Hash(file)).ConfigureAwait(false);
        var link = $"https://www.virustotal.com/gui/file/{hash}";
        var key = Settings.Current.VirusTotalKey;
        if (string.IsNullOrWhiteSpace(key)) return new(false, 0, 0, 0, link, "Нет ключа VirusTotal (Настройки → Защита)");
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"https://www.virustotal.com/api/v3/files/{hash}");
            req.Headers.Add("x-apikey", key.Trim());
            using var resp = await Http.SendAsync(req).ConfigureAwait(false);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return new(false, 0, 0, 0, link);
            if (!resp.IsSuccessStatusCode) return new(false, 0, 0, 0, link, $"VirusTotal: {(int)resp.StatusCode} {resp.ReasonPhrase}");
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync().ConfigureAwait(false));
            var st = doc.RootElement.GetProperty("data").GetProperty("attributes").GetProperty("last_analysis_stats");
            return new(true, st.GetProperty("malicious").GetInt32(), st.GetProperty("suspicious").GetInt32(),
                st.GetProperty("undetected").GetInt32() + st.GetProperty("harmless").GetInt32(), link);
        }
        catch (Exception e) { return new(false, 0, 0, 0, link, e.Message); }
    }

    // Heuristic sweep: autostart, scheduled tasks, services, running processes.
    static readonly string[] SystemNames = { "svchost.exe", "lsass.exe", "csrss.exe", "winlogon.exe", "explorer.exe", "services.exe", "smss.exe", "dwm.exe", "conhost.exe", "rundll32.exe", "taskhostw.exe", "spoolsv.exe" };

    public static async Task<List<Suspect>> Sweep(Action<string>? progress = null) => await Task.Run(() =>
    {
        var res = new Dictionary<string, Suspect>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, string? path, string where, StartupItem? st = null, int? pid = null)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            path = Environment.ExpandEnvironmentVariables(path.Trim('"'));
            if (!File.Exists(path) || res.ContainsKey(path)) return;
            var (score, reasons) = Rate(path);
            if (score > 0) res[path] = new(name, path, where, reasons, score, st, pid);
        }

        progress?.Invoke("Автозагрузка…");
        foreach (var s in StartupApps.List()) Add(s.Name, s.FilePath, "Автозагрузка: " + s.Source, s);

        progress?.Invoke("Планировщик задач…");
        foreach (var (name, exe) in Tasks()) Add(name, exe, "Планировщик задач");

        progress?.Invoke("Службы…");
        try
        {
            using var s = new ManagementObjectSearcher("SELECT Name, DisplayName, PathName FROM Win32_Service WHERE StartMode='Auto'");
            foreach (ManagementObject o in s.Get())
                Add(o["DisplayName"]?.ToString() ?? "", StartupApps.ExeFromCommand(o["PathName"]?.ToString() ?? ""), "Служба");
        }
        catch { }

        progress?.Invoke("Процессы (в т.ч. в трее)…");
        foreach (var p in Process.GetProcesses())
        {
            try { Add(p.ProcessName, p.MainModule?.FileName, "Запущенный процесс", pid: p.Id); } catch { }
            finally { p.Dispose(); }
        }
        return res.Values.OrderByDescending(s => s.Score).ToList();
    });

    static IEnumerable<(string, string?)> Tasks()
    {
        var csv = Shell.Run("schtasks", "/query /fo csv /v", 60_000);
        foreach (var line in csv.Split('\n').Skip(1))
        {
            var cols = Regex.Matches(line, "\"((?:[^\"]|\"\")*)\"").Select(m => m.Groups[1].Value).ToArray();
            if (cols.Length < 9 || cols[1].StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase)) continue;
            yield return (cols[1].TrimStart('\\'), StartupApps.ExeFromCommand(cols[8]));
        }
    }

    static (int, List<string>) Rate(string path)
    {
        var reasons = new List<string>();
        int score = 0;
        var lower = path.ToLowerInvariant();
        var name = Path.GetFileName(lower);
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows).ToLowerInvariant();
        bool inWindows = lower.StartsWith(win);

        if (!inWindows && SystemNames.Contains(name)) { score += 5; reasons.Add("Системное имя вне папки Windows"); }
        if (lower.Contains(@"\temp\") || lower.Contains(@"\tmp\")) { score += 3; reasons.Add("Запуск из временной папки"); }
        if (lower.Contains(@"\downloads\") || lower.Contains(@"\users\public\")) { score += 2; reasons.Add("Запуск из Загрузок / Public"); }
        if (lower.Contains(@"\appdata\roaming\") && !lower.Contains(@"\microsoft\")) { score += 1; reasons.Add("Из AppData\\Roaming"); }
        try { if ((File.GetAttributes(path) & FileAttributes.Hidden) != 0) { score += 2; reasons.Add("Скрытый файл"); } } catch { }

        // Catalog-signed Windows binaries fail a plain file signature check; trust their location instead.
        if (!inWindows)
        {
            var sig = Signature(path);
            if (sig == null) { score += 2; reasons.Add("Нет цифровой подписи"); }
            else if (sig == "") { score += 3; reasons.Add("Подпись недействительна"); }
        }
        // Signed and in a normal place → not interesting.
        return score >= 2 ? (score, reasons) : (0, reasons);
    }

    /// <summary>null = unsigned, "" = signature present but invalid, otherwise signer name.</summary>
    public static string? Signature(string path)
    {
        int r = Verify(path);
        if (r == unchecked((int)0x800B0100)) return null;   // TRUST_E_NOSIGNATURE
        if (r != 0) return "";
        try
        {
#pragma warning disable SYSLIB0057
            var c = System.Security.Cryptography.X509Certificates.X509Certificate.CreateFromSignedFile(path);
#pragma warning restore SYSLIB0057
            var m = Regex.Match(c.Subject, "CN=\"?([^\",]+)");
            return m.Success ? m.Groups[1].Value : c.Subject;
        }
        catch { return "?"; }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WINTRUST_FILE_INFO { public int cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }

    [StructLayout(LayoutKind.Sequential)]
    struct WINTRUST_DATA
    {
        public int cbStruct; public IntPtr pPolicyCallbackData, pSIPClientData; public int dwUIChoice, fdwRevocationChecks, dwUnionChoice;
        public IntPtr pFile; public int dwStateAction; public IntPtr hWVTStateData; public IntPtr pwszURLReference; public int dwProvFlags, dwUIContext; public IntPtr pSignatureSettings;
    }

    [DllImport("wintrust.dll", CharSet = CharSet.Unicode)]
    static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, ref WINTRUST_DATA data);

    static int Verify(string path)
    {
        var fi = new WINTRUST_FILE_INFO { cbStruct = Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
        var pFile = Marshal.AllocHGlobal(fi.cbStruct);
        try
        {
            Marshal.StructureToPtr(fi, pFile, false);
            var d = new WINTRUST_DATA
            {
                cbStruct = Marshal.SizeOf<WINTRUST_DATA>(), dwUIChoice = 2 /* none */, fdwRevocationChecks = 0, dwUnionChoice = 1 /* file */,
                pFile = pFile, dwProvFlags = 0x1000 /* cache only url retrieval */,
            };
            var r = WinVerifyTrust(IntPtr.Zero, new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"), ref d);
            d.dwStateAction = 2; // close
            WinVerifyTrust(IntPtr.Zero, new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"), ref d);
            return r;
        }
        finally { Marshal.FreeHGlobal(pFile); }
    }
}
