using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;

namespace MishkaHelper.Core;

public class CleanTarget
{
    public string Key = "", Name = "", Hint = "";
    public Func<IEnumerable<string>> Paths = () => Array.Empty<string>();   // folders whose *contents* go
    public string[] Patterns = { "*" };
    public int MinAgeDays;
    public bool Special;        // handled by custom code (recycle bin, cleanmgr, DISM)
    public bool DefaultOn = true;
    public long Size; public int Files;
}

public static class Cleaner
{
    static string Env(string v) => Environment.ExpandEnvironmentVariables(v);
    static readonly string Local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    static readonly string Roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

    static IEnumerable<string> ChromiumCaches()
    {
        string[] roots =
        {
            @"Google\Chrome\User Data", @"Microsoft\Edge\User Data", @"Yandex\YandexBrowser\User Data",
            @"BraveSoftware\Brave-Browser\User Data", @"Vivaldi\User Data",
        };
        foreach (var r in roots.Select(r => Path.Combine(Local, r)).Where(Directory.Exists))
            foreach (var profile in SafeDirs(r))
                foreach (var c in new[] { @"Cache", @"Code Cache", @"GPUCache", @"Service Worker\CacheStorage" })
                    yield return Path.Combine(profile, c);
        foreach (var opera in new[] { @"Opera Software\Opera Stable", @"Opera Software\Opera GX Stable" })
        {
            yield return Path.Combine(Local, opera, "Cache");
            yield return Path.Combine(Roaming, opera, "Code Cache");
        }
        var ff = Path.Combine(Local, @"Mozilla\Firefox\Profiles");
        foreach (var p in SafeDirs(ff)) yield return Path.Combine(p, "cache2");
    }

    static IEnumerable<string> SafeDirs(string p)
    {
        try { return Directory.Exists(p) ? Directory.GetDirectories(p) : Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    public static List<CleanTarget> Targets() => new()
    {
        new() { Key = "usertemp", Name = "Временные файлы (%TEMP%)", Hint = "Мусор установщиков и программ", Paths = () => new[] { Path.GetTempPath() } },
        new() { Key = "wintemp", Name = "Временные файлы Windows", Hint = @"C:\Windows\Temp", Paths = () => new[] { Env(@"%WINDIR%\Temp") } },
        new() { Key = "recycle", Name = "Корзина", Hint = "Очистить корзину на всех дисках", Special = true },
        new() { Key = "browsers", Name = "Кэш браузеров", Hint = "Chrome, Edge, Яндекс, Opera, Brave, Vivaldi, Firefox. Пароли и вкладки не трогаются", Paths = ChromiumCaches },
        new() { Key = "thumbs", Name = "Кэш миниатюр", Hint = "Пересоздаётся сам при просмотре папок", Paths = () => new[] { Path.Combine(Local, @"Microsoft\Windows\Explorer") }, Patterns = new[] { "thumbcache_*.db", "iconcache_*.db" } },
        new() { Key = "dumps", Name = "Дампы и отчёты об ошибках", Hint = "Крэш-дампы, отчёты WER", Paths = () => new[]
            {
                Path.Combine(Local, "CrashDumps"), Env(@"%WINDIR%\Minidump"), Env(@"%ProgramData%\Microsoft\Windows\WER\ReportArchive"),
                Env(@"%ProgramData%\Microsoft\Windows\WER\ReportQueue"), Path.Combine(Local, @"Microsoft\Windows\WER"),
            } },
        new() { Key = "logs", Name = "Старые логи Windows", Hint = "Файлы журналов старше 7 дней", Paths = () => new[] { Env(@"%WINDIR%\Logs") }, Patterns = new[] { "*.log", "*.etl", "*.cab" }, MinAgeDays = 7 },
        new() { Key = "wu", Name = "Кэш обновлений Windows", Hint = "Уже установленные загрузки и кэш оптимизации доставки", Paths = () => new[]
            {
                Env(@"%WINDIR%\SoftwareDistribution\Download"),
                Env(@"%WINDIR%\ServiceProfiles\NetworkService\AppData\Local\Microsoft\Windows\DeliveryOptimization\Cache"),
            } },
        new() { Key = "shaders", Name = "Кэш шейдеров (DirectX / NVIDIA)", Hint = "Освобождает место, но игры при первом запуске могут подтормаживать", DefaultOn = false, Paths = () => new[]
            {
                Path.Combine(Local, "D3DSCache"), Path.Combine(Local, @"NVIDIA\DXCache"), Path.Combine(Local, @"NVIDIA\GLCache"),
            } },
        new() { Key = "drivers", Name = "Старые пакеты драйверов", Hint = "Через встроенную «Очистку диска». Размер заранее неизвестен", Special = true, DefaultOn = false },
        new() { Key = "dism", Name = "Хранилище компонентов (DISM)", Hint = "Удаляет заменённые версии обновлений. Долго (5–15 мин)", Special = true, DefaultOn = false },
    };

    public static void Measure(CleanTarget t, CancellationToken ct)
    {
        t.Size = 0; t.Files = 0;
        if (t.Key == "recycle")
        {
            var i = new Native.SHQUERYRBINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<Native.SHQUERYRBINFO>() };
            if (Native.SHQueryRecycleBin(null, ref i) == 0) { t.Size = i.i64Size; t.Files = (int)i.i64NumItems; }
            return;
        }
        foreach (var f in Files(t, ct))
        {
            try { t.Size += new FileInfo(f).Length; t.Files++; } catch { }
        }
    }

    static IEnumerable<string> Files(CleanTarget t, CancellationToken ct)
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        var cutoff = DateTime.Now.AddDays(-t.MinAgeDays);
        foreach (var dir in t.Paths().Where(Directory.Exists))
            foreach (var pat in t.Patterns)
            {
                IEnumerable<string> e;
                try { e = Directory.EnumerateFiles(dir, pat, opts); } catch { continue; }
                foreach (var f in e)
                {
                    ct.ThrowIfCancellationRequested();
                    if (t.MinAgeDays > 0) { try { if (File.GetLastWriteTime(f) > cutoff) continue; } catch { continue; } }
                    yield return f;
                }
            }
    }

    /// <summary>Delete what Measure found. Locked files are skipped. Returns bytes freed.</summary>
    public static long Clean(CleanTarget t, CancellationToken ct, Action<string>? progress = null)
    {
        long freed = 0;
        switch (t.Key)
        {
            case "recycle":
                Measure(t, ct);
                if (Native.SHEmptyRecycleBin(IntPtr.Zero, null, 7) == 0) freed = t.Size;
                return freed;
            case "drivers":
                progress?.Invoke("Очистка диска: старые драйверы…");
                using (var k = Microsoft.Win32.Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches\Device Driver Packages"))
                    k.SetValue("StateFlags0777", 2, Microsoft.Win32.RegistryValueKind.DWord);
                Shell.Run("cleanmgr", "/sagerun:777", 30 * 60_000);
                return 0;
            case "dism":
                progress?.Invoke("DISM: очистка хранилища компонентов… (это долго)");
                Shell.Run("dism", "/online /cleanup-image /startcomponentcleanup", 60 * 60_000);
                return 0;
        }

        foreach (var f in Files(t, ct).ToList())
        {
            try
            {
                var len = new FileInfo(f).Length;
                File.SetAttributes(f, FileAttributes.Normal);
                File.Delete(f);
                freed += len;
            }
            catch { /* in use — skip */ }
        }
        if (t.Patterns.Length == 1 && t.Patterns[0] == "*")
            foreach (var dir in t.Paths().Where(Directory.Exists))
                DeleteEmptyDirs(dir, ct);
        return freed;
    }

    static void DeleteEmptyDirs(string root, CancellationToken ct)
    {
        string[] subs;
        try { subs = Directory.GetDirectories(root); } catch { return; }
        foreach (var d in subs)
        {
            ct.ThrowIfCancellationRequested();
            if ((File.GetAttributes(d) & FileAttributes.ReparsePoint) != 0) continue;
            DeleteEmptyDirs(d, ct);
            try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d); } catch { }
        }
    }

    /// <summary>Trim working sets of background processes. Helps when RAM is nearly full; pointless otherwise.</summary>
    public static int TrimRam()
    {
        int n = 0;
        var self = Environment.ProcessId;
        var fgWnd = GetForegroundWindow();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.Id == self || p.Id <= 4 || p.MainWindowHandle == fgWnd) continue;   // never the app you're using
                if (Native.EmptyWorkingSet(p.Handle)) n++;
            }
            catch { }
            finally { p.Dispose(); }
        }
        return n;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();

    public static void FlushDns() => Shell.Run("ipconfig", "/flushdns");

    /// <summary>The one-button "make it faster": selected cleanup + DNS + RAM trim.</summary>
    public static async Task<long> BoostAll(Action<string>? progress = null)
    {
        var keys = Settings.Current.CleanTargets.Where(k => k is not ("dism" or "drivers")).ToHashSet();
        long freed = 0;
        await Task.Run(() =>
        {
            foreach (var t in Targets().Where(t => keys.Contains(t.Key)))
            {
                progress?.Invoke(t.Name);
                try { freed += Clean(t, CancellationToken.None); } catch (Exception e) { Log.Error(e); }
            }
            progress?.Invoke("DNS и память");
            FlushDns();
            TrimRam();
        });
        Settings.Current.LastClean = DateTime.Now;
        Settings.Save();
        return freed;
    }

    public static string Size(long b) => b switch
    {
        >= 1L << 30 => $"{b / (double)(1L << 30):0.00} ГБ",
        >= 1L << 20 => $"{b / (double)(1L << 20):0.0} МБ",
        >= 1L << 10 => $"{b / 1024.0:0} КБ",
        _ => $"{b} Б",
    };

    // Large files & duplicates
    public record FoundFile(string Path, long Size, DateTime Modified);

    public static List<FoundFile> LargeFiles(string root, long minBytes, CancellationToken ct, int max = 300)
    {
        var opts = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System };
        var res = new List<FoundFile>();
        foreach (var fi in new DirectoryInfo(root).EnumerateFiles("*", opts))
        {
            ct.ThrowIfCancellationRequested();
            try { if (fi.Length >= minBytes) res.Add(new(fi.FullName, fi.Length, fi.LastWriteTime)); } catch { }
        }
        return res.OrderByDescending(f => f.Size).Take(max).ToList();
    }

    /// <summary>Groups of identical files (size match → SHA-256 match). Files under 1 MB are ignored.</summary>
    public static List<List<FoundFile>> Duplicates(string root, CancellationToken ct)
    {
        var all = LargeFiles(root, 1 << 20, ct, int.MaxValue);
        var res = new List<List<FoundFile>>();
        foreach (var bySize in all.GroupBy(f => f.Size).Where(g => g.Count() > 1))
            foreach (var byHash in bySize.GroupBy(f => Hash(f.Path, ct)).Where(g => g.Key != "" && g.Count() > 1))
                res.Add(byHash.ToList());
        return res.OrderByDescending(g => g[0].Size * (g.Count - 1)).ToList();
    }

    public static string Hash(string path, CancellationToken ct = default)
    {
        try
        {
            using var s = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(s));
        }
        catch { return ""; }
    }

    public static void ToRecycleBin(string path) =>
        Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile(path, Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs, Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
}
