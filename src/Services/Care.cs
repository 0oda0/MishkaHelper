using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MishkaHelper.Core;

/// <summary>
/// Looking after the PC: pending Windows and driver updates, program updates (winget), a new-PC program set,
/// a dust hint from CPU temperature under load over months, and battery wear by month.
/// </summary>
public static class Care
{
    // ---------------- Windows Update (drivers included) ----------------
    public record Pending(string Title, bool Driver);

    /// <summary>What Windows Update would install now. The agent's own COM API: the same list as Settings → Windows Update, drivers included.</summary>
    public static Task<List<Pending>> WindowsUpdates() => Task.Run(() =>
    {
        var list = new List<Pending>();
        dynamic session = Activator.CreateInstance(Type.GetTypeFromProgID("Microsoft.Update.Session")!)!;
        dynamic result = session.CreateUpdateSearcher().Search("IsInstalled=0 and IsHidden=0");
        foreach (dynamic u in result.Updates) list.Add(new((string)u.Title, (int)u.Type == 2));
        return list;
    });

    // ---------------- winget ----------------
    /// <summary>
    /// winget.exe inside its protected package folder. The usual "winget" on PATH is an alias in the user's own folder,
    /// and an elevated app must not run anything from there.
    /// </summary>
    public static string? Winget()
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps");
        try
        {
            var exe = Directory.GetDirectories(root, "Microsoft.DesktopAppInstaller_*_x64__8wekyb3d8bbwe")
                .OrderByDescending(d => Version.TryParse(Path.GetFileName(d).Split('_')[1], out var v) ? v : new Version())
                .Select(d => Path.Combine(d, "winget.exe")).FirstOrDefault(File.Exists);
            if (exe != null) return exe;
        }
        catch { }
        return AppPaths.Elevated ? null : "winget";   // dev copy, not elevated: the alias is fine
    }

    public record Upgrade(string Name, string Id, string Version, string Available);

    public static Task<List<Upgrade>> Upgrades() => Task.Run(() =>
    {
        var exe = Winget() ?? throw new InvalidOperationException("winget не найден. Поставь «Установщик приложений» из Microsoft Store");
        var psi = new ProcessStartInfo(exe, "upgrade --accept-source-agreements --disable-interactivity")
        { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
        using var p = Process.Start(psi)!;
        var text = p.StandardOutput.ReadToEnd();
        p.WaitForExit(120000);
        return ParseTable(text);
    });

    /// <summary>
    /// winget's table: columns start where the header words start (the header is in the system language).
    /// Spinner frames before it are separated by \r; the table ends at the first line that doesn't fit it.
    /// </summary>
    internal static List<Upgrade> ParseTable(string text)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n').Select(l => l.Split('\r')[^1]).ToList();
        int dash = lines.FindIndex(l => l.Length > 20 && l.Trim().All(c => c == '-'));
        var res = new List<Upgrade>();
        if (dash < 1) return res;
        var head = lines[dash - 1];
        var starts = Regex.Matches(head, @"(?<=^|\s)\S").Select(m => m.Index).ToList();
        if (starts.Count < 4) return res;
        string Col(string l, int i) => starts[i] >= l.Length ? "" : (i + 1 < starts.Count ? l[starts[i]..Math.Min(l.Length, starts[i + 1])] : l[starts[i]..]).Trim();
        foreach (var l in lines.Skip(dash + 1))
        {
            if (l.Length <= starts[3] || l.TrimStart().StartsWith('-')) break;
            var id = Col(l, 1);
            if (id.Length == 0 || id.Contains(' ')) break;
            res.Add(new(Col(l, 0), id, Col(l, 2), Col(l, 3)));
        }
        return res;
    }

    static readonly Regex SafeId = new(@"^[A-Za-z0-9][A-Za-z0-9.\-_+]*$");
    public static bool IsSafeId(string id) => id.Length < 100 && SafeId.IsMatch(id);

    /// <summary>Runs winget in a console the user can watch. Ids are checked: they end up on a command line.</summary>
    public static void RunVisible(IEnumerable<string> wingetArgs)
    {
        var exe = Winget() ?? throw new InvalidOperationException("winget не найден. Поставь «Установщик приложений» из Microsoft Store");
        var cmd = string.Join(" & ", wingetArgs.Select(a => $"\"{exe}\" {a}")) + " & echo. & echo Готово. & pause";
        Process.Start(new ProcessStartInfo(Shell.Sys("cmd.exe"), "/c chcp 65001 >nul & " + cmd) { UseShellExecute = false });
    }

    public static void UpgradeAll() => RunVisible(new[] { "upgrade --all --silent --accept-package-agreements --accept-source-agreements --disable-interactivity" });

    public static void Install(IEnumerable<string> ids) =>
        RunVisible(ids.Where(IsSafeId).Select(id => $"install --id {id} -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity"));

    /// <summary>A ready set for a fresh Windows: name, winget id, group.</summary>
    public static readonly (string Name, string Id, string Group)[] Kit =
    {
        ("Google Chrome", "Google.Chrome", "Браузеры"), ("Firefox", "Mozilla.Firefox", "Браузеры"), ("Яндекс Браузер", "Yandex.Browser", "Браузеры"), ("Brave", "Brave.Brave", "Браузеры"),
        ("Telegram", "Telegram.TelegramDesktop", "Общение"), ("Discord", "Discord.Discord", "Общение"), ("Zoom", "Zoom.Zoom", "Общение"),
        ("Steam", "Valve.Steam", "Игры"), ("Epic Games", "EpicGames.EpicGamesLauncher", "Игры"),
        ("VLC", "VideoLAN.VLC", "Медиа"), ("OBS Studio", "OBSProject.OBSStudio", "Медиа"), ("AIMP", "AIMP.AIMP", "Медиа"),
        ("7-Zip", "7zip.7zip", "Полезное"), ("Notepad++", "Notepad++.Notepad++", "Полезное"), ("Everything (поиск файлов)", "voidtools.Everything", "Полезное"),
        ("PowerToys", "Microsoft.PowerToys", "Полезное"), ("ShareX (скриншоты)", "ShareX.ShareX", "Полезное"), ("qBittorrent", "qBittorrent.qBittorrent", "Полезное"),
        ("SumatraPDF", "SumatraPDF.SumatraPDF", "Документы"), ("LibreOffice", "TheDocumentFoundation.LibreOffice", "Документы"),
        ("VS Code", "Microsoft.VisualStudioCode", "Для разработки"), ("Git", "Git.Git", "Для разработки"),
        ("RustDesk (удалённый доступ)", "RustDesk.RustDesk", "Полезное"),
    };

    /// <summary>A set as text to send to a friend: ids, one per line or comma-separated. Unknown or unsafe ids are dropped.</summary>
    public static List<string> ParseSet(string text) =>
        text.Split(new[] { ',', ';', '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(IsSafeId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    // ---------------- dust: CPU temperature under load, by day ----------------
    public class Data
    {
        public Dictionary<string, double[]> LoadTemp { get; set; } = new();   // "yyyy-MM-dd" → [sum, count] of CPU °C while CPU ≥ 50 %
        public DateTime LastDustNote { get; set; }
    }

    static Data? _data;
    static string File_ => Path.Combine(AppPaths.Data, "care.json");
    public static Data Load()
    {
        if (_data != null) return _data;
        try { _data = JsonSerializer.Deserialize<Data>(File.ReadAllText(File_)); } catch { }
        return _data ??= new();
    }
    public static void Save()
    {
        string json;
        lock (Load().LoadTemp) json = JsonSerializer.Serialize(Load());   // samples keep arriving from the sampler thread
        try { File.WriteAllText(File_, json); } catch (Exception e) { Log.Error(e); }
    }

    /// <summary>Every sample: under load, its CPU temperature goes into today's sum.</summary>
    public static void Track(Sample s)
    {
        if (s.Cpu < 50 || s.CpuTemp <= 20) return;
        var d = Load().LoadTemp;
        var key = s.Time.ToString("yyyy-MM-dd");
        lock (d)
        {
            if (!d.TryGetValue(key, out var v)) d[key] = v = new double[2];
            v[0] += s.CpuTemp; v[1]++;
        }
    }

    /// <summary>
    /// (now, then): average CPU °C under load over the last 14 days, and over the first 30 days on record.
    /// Null until there's enough: two months of history and at least ~10 minutes of load in each window.
    /// </summary>
    internal static (double Now, double Then)? Dust(Dictionary<string, double[]> d, DateTime today)
    {
        var days = d.Select(kv => (Day: DateTime.Parse(kv.Key), Sum: kv.Value[0], N: kv.Value[1])).OrderBy(x => x.Day).ToList();
        if (days.Count == 0 || (today - days[0].Day).TotalDays < 60) return null;
        var first = days.Where(x => x.Day < days[0].Day.AddDays(30)).ToList();
        var recent = days.Where(x => x.Day > today.AddDays(-14)).ToList();
        double n1 = first.Sum(x => x.N), n2 = recent.Sum(x => x.N);
        if (n1 < 120 || n2 < 120) return null;
        return (recent.Sum(x => x.Sum) / n2, first.Sum(x => x.Sum) / n1);
    }

    /// <summary>Every half hour: save, forget days older than a year, and once a month say so if it runs 10° hotter than it used to.</summary>
    public static void Daily()
    {
        var data = Load();
        lock (data.LoadTemp)
            foreach (var k in data.LoadTemp.Keys.Where(k => DateTime.Parse(k) < DateTime.Today.AddDays(-400)).ToList()) data.LoadTemp.Remove(k);
        if (Dust(data.LoadTemp, DateTime.Today) is { } t && t.Now - t.Then >= 10 && (DateTime.Now - data.LastDustNote).TotalDays > 30)
        {
            data.LastDustNote = DateTime.Now;
            Alerts.Fire("dust", "", "Похоже, пора почистить от пыли",
                $"Под нагрузкой процессор теперь греется до {t.Now:0}°, а раньше было {t.Then:0}°. Чистка и новая термопаста обычно возвращают как было.", 60 * 24 * 30);
        }
        Save();
    }

    // ---------------- battery ----------------
    public record BatteryMonth(DateTime Month, int Percent);

    /// <summary>Battery capacity as % of new, by month, from Windows' own battery report (it keeps weeks of history).</summary>
    public static Task<(int Now, List<BatteryMonth> Months)?> Battery() => Task.Run<(int, List<BatteryMonth>)?>(() =>
    {
        var file = Path.Combine(AppPaths.Shared, "battery.xml");
        using (var p = Process.Start(new ProcessStartInfo(Shell.Sys("powercfg.exe"), $"/batteryreport /xml /output \"{file}\"") { CreateNoWindow = true, UseShellExecute = false }))
            p!.WaitForExit(30000);
        if (!File.Exists(file)) return null;
        return ParseBattery(File.ReadAllText(file));
    });

    internal static (int Now, List<BatteryMonth> Months)? ParseBattery(string xml)
    {
        var doc = XDocument.Parse(xml);
        string? V(XElement e, string name) => e.Attribute(name)?.Value ?? e.Elements().FirstOrDefault(x => x.Name.LocalName == name)?.Value;
        var bat = doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "Battery");
        if (bat == null || !double.TryParse(V(bat, "DesignCapacity"), out var design) || design <= 0 || !double.TryParse(V(bat, "FullChargeCapacity"), out var full)) return null;
        var months = doc.Descendants().Where(e => e.Name.LocalName == "HistoryEntry")
            .Select(e => (Start: DateTime.TryParse(V(e, "LocalStartDate"), out var d) ? d : default,
                          D: double.TryParse(V(e, "DesignCapacity"), out var dc) ? dc : 0, F: double.TryParse(V(e, "FullChargeCapacity"), out var fc) ? fc : 0))
            .Where(x => x.Start != default && x.D > 0 && x.F > 0)
            .GroupBy(x => new DateTime(x.Start.Year, x.Start.Month, 1))
            .Select(g => new BatteryMonth(g.Key, (int)Math.Round(g.Average(x => x.F / x.D) * 100)))
            .OrderBy(m => m.Month).ToList();
        return ((int)Math.Round(full / design * 100), months);
    }
}
