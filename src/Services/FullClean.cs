using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MishkaHelper.Core;

/// <summary>
/// "Полная очистка": one pass over junk files, autostart, background programs and settings, with a verdict per item
/// so a first-timer can just press one button. Only known junk is pre-selected; drivers, security, sound and
/// input software are never touched.
/// </summary>
public static class FullClean
{
    public enum Verdict { Junk, Ask, Keep }

    public record Finding(string Name, string Detail, Verdict Verdict, string Why);

    // Updaters, launchers and helpers that don't need to start with Windows.
    static readonly Regex JunkRx = new(
        @"update|updater|launcher|steam|epic ?games|origin|ea ?desktop|eadm|ubisoft|uplay|battle\.?net|riot|gog ?galaxy|" +
        @"spotify|skype|teams|zoom|viber|whatsapp|itunes|adobe ?(arm|gc|acrobat)|acrobat|ccleaner|utorrent|bittorrent|mediaget|" +
        @"yandex(?!.*disk)|browser[_ ]?assistant|opera.*(assistant|autoupdate)|cortana|msedge.*--no-startup-window|edge.*autolaunch|" +
        @"java ?update|jusched|ms ?teams|lghub_updater|wallpaper|razer ?cortex|avast ?browser|mail\.ru|amigo|guard\.mail",
        RegexOptions.IgnoreCase);

    // Never suggested: hardware, sound, input, security, cloud sync, ourselves.
    static readonly Regex KeepRx = new(
        @"realtek|nvidia|nv(cpl|display|tray)|amd|radeon|intel|igfx|synaptics|elan|touchpad|dolby|nahimic|waves|maxx|audio|" +
        @"securityhealth|defender|windows ?security|kaspersky|eset|avast(?! ?browser)|avg|norton|bitdefender|malwarebytes|drweb|" +
        @"onedrive|google ?drive|dropbox|yandex ?disk|icloud|\bmega(sync)?\b|nextcloud|" +
        @"acer|nitro|predator|asus|armoury|lenovo|vantage|msi|dragon ?center|hp ?(hotkey|support)|dell|huawei|pc ?manager|" +
        @"logi|logitech|razer(?! ?cortex)|steelseries|corsair|hyperx|bluetooth|keyboard|mouse|\bpen\b|tablet|wacom|" +
        @"mishkahelper|ctfmon|explorer|rundll32 .*shell32",
        RegexOptions.IgnoreCase);

    // Browsers keep running in the background with open tabs/downloads — the user decides.
    static readonly Regex BrowserRx = new(@"^(browser|chrome|msedge|firefox|opera|brave|vivaldi|yandex)$|\\(browser|chrome|msedge|firefox|opera|brave|vivaldi)\.exe", RegexOptions.IgnoreCase);

    public static Verdict Classify(string name, string command)
    {
        var s = name + " " + command;
        if (KeepRx.IsMatch(s)) return Verdict.Keep;
        if (BrowserRx.IsMatch(name) || BrowserRx.IsMatch(command) && !Regex.IsMatch(command, @"update|--no-startup-window|autolaunch", RegexOptions.IgnoreCase)) return Verdict.Ask;
        return JunkRx.IsMatch(s) ? Verdict.Junk : Verdict.Ask;
    }

    static string Why(Verdict v) => v switch
    {
        Verdict.Junk => "не нужна при старте — запустится сама, когда откроешь программу",
        Verdict.Keep => "драйвер, звук, защита, облако или устройство — лучше оставить",
        _ => "неизвестно — не трогай, если не уверен",
    };

    public static List<(StartupItem Item, Verdict Verdict, string Why)> Startup() =>
        StartupApps.List().Where(i => i.Enabled).Select(i => { var v = Classify(i.Name, i.Command); return (i, v, Why(v)); })
            .OrderBy(x => x.v).ThenBy(x => x.i.Name).ToList();

    public record Bg(string Name, int Count, long Bytes, Verdict Verdict, string Why);

    /// <summary>Programs of this user that run without a window, heaviest first.</summary>
    public static List<Bg> Background()
    {
        int session = Process.GetCurrentProcess().SessionId, self = Environment.ProcessId;
        var list = new List<(string Name, long Mem, string Path)>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.SessionId != session || p.Id == self || p.MainWindowHandle != IntPtr.Zero) continue;
                string path = "";
                try { path = p.MainModule?.FileName ?? ""; } catch { }
                // System programs live in Windows; skip them and anything we can't identify.
                if (path.Length == 0 || path.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)) continue;
                list.Add((p.ProcessName, p.WorkingSet64, path));
            }
            catch { }
            finally { p.Dispose(); }
        }
        // Programs with a visible window somewhere (e.g. browser helpers) are in use — not "background".
        var visible = Process.GetProcesses().Where(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } }).Select(p => p.ProcessName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return list.Where(x => !visible.Contains(x.Name)).GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => { var v = Classify(g.Key, g.First().Path); return new Bg(g.Key, g.Count(), g.Sum(x => x.Mem), v, Why(v)); })
            .Where(b => b.Bytes > 20L << 20 || b.Verdict == Verdict.Junk)
            .OrderBy(b => b.Verdict).ThenByDescending(b => b.Bytes).Take(25).ToList();
    }

    public static int Close(string processName)
    {
        int n = 0;
        foreach (var p in Process.GetProcessesByName(processName))
        {
            try { p.Kill(true); n++; } catch { }
            finally { p.Dispose(); }
        }
        return n;
    }

    public static double RamUsedPercent() { var m = new Native.MEMORYSTATUSEX(); Native.GlobalMemoryStatusEx(m); return m.dwMemoryLoad; }
}
