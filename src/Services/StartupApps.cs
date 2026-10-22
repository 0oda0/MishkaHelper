using System.IO;
using Microsoft.Win32;

namespace MishkaHelper.Core;

public record StartupItem(string Name, string Command, string Source, bool Enabled, RegistryKey Hive, string ApprovedKey, string? FilePath);
public record InstalledApp(string Name, string Publisher, string Version, long SizeKb, string Uninstall, string? InstallDate);

/// <summary>Autostart entries (same enable/disable mechanism as Task Manager) and installed programs.</summary>
public static class StartupApps
{
    const string Approved = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static List<StartupItem> List()
    {
        var res = new List<StartupItem>();
        void FromRun(RegistryKey hive, string path, string approved, string src)
        {
            using var k = hive.OpenSubKey(path);
            if (k == null) return;
            foreach (var n in k.GetValueNames().Where(n => n != ""))
            {
                var cmd = k.GetValue(n)?.ToString() ?? "";
                res.Add(new(n, cmd, src, IsEnabled(hive, approved, n), hive, approved, ExeFromCommand(cmd)));
            }
        }
        FromRun(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run", "Реестр (пользователь)");
        FromRun(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Run", "Run", "Реестр (все)");
        FromRun(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run", "Run32", "Реестр (все, 32-бит)");

        foreach (var (folder, hive) in new[]
                 {
                     (Environment.GetFolderPath(Environment.SpecialFolder.Startup), Registry.CurrentUser),
                     (Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), Registry.LocalMachine),
                 })
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var f in Directory.GetFiles(folder).Where(f => !f.EndsWith("desktop.ini", StringComparison.OrdinalIgnoreCase)))
                res.Add(new(Path.GetFileName(f), f, "Папка автозагрузки", IsEnabled(hive, "StartupFolder", Path.GetFileName(f)), hive, "StartupFolder", f));
        }
        return res.OrderBy(r => r.Name).ToList();
    }

    static bool IsEnabled(RegistryKey hive, string approved, string name)
    {
        using var k = hive.OpenSubKey(Approved + approved);
        return k?.GetValue(name) is not byte[] b || b.Length == 0 || (b[0] & 1) == 0;   // 02 = on, 03 = off
    }

    public static void SetEnabled(StartupItem it, bool on)
    {
        using var k = it.Hive.CreateSubKey(Approved + it.ApprovedKey);
        var data = new byte[12];
        data[0] = (byte)(on ? 2 : 3);
        if (!on) BitConverter.GetBytes(DateTime.Now.ToFileTimeUtc()).CopyTo(data, 4);
        k.SetValue(it.Name, data, RegistryValueKind.Binary);
    }

    public static string? ExeFromCommand(string cmd)
    {
        cmd = Environment.ExpandEnvironmentVariables(cmd.Trim());
        if (cmd.StartsWith('"')) { int e = cmd.IndexOf('"', 1); return e > 1 ? cmd[1..e] : null; }
        int exe = cmd.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        return exe > 0 ? cmd[..(exe + 4)] : cmd.Split(' ')[0];
    }

    public static List<InstalledApp> Installed()
    {
        var res = new Dictionary<string, InstalledApp>();
        foreach (var (hive, path) in new[]
                 {
                     (Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                     (Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
                     (Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall"),
                 })
        {
            using var root = hive.OpenSubKey(path);
            if (root == null) continue;
            foreach (var sub in root.GetSubKeyNames())
            {
                using var k = root.OpenSubKey(sub);
                var name = k?.GetValue("DisplayName") as string;
                var un = (k?.GetValue("QuietUninstallString") ?? k?.GetValue("UninstallString")) as string;
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(un) || k!.GetValue("SystemComponent") is 1) continue;
                res[name] = new(name, k.GetValue("Publisher") as string ?? "", k.GetValue("DisplayVersion") as string ?? "",
                    k.GetValue("EstimatedSize") is int sz ? sz : 0, k.GetValue("UninstallString") as string ?? un, k.GetValue("InstallDate") as string);
            }
        }
        return res.Values.OrderBy(a => a.Name).ToList();
    }

    public static void Uninstall(InstalledApp a)
    {
        var cmd = a.Uninstall.Trim();
        string exe, args;
        if (cmd.StartsWith('"')) { int e = cmd.IndexOf('"', 1); exe = cmd[1..e]; args = cmd[(e + 1)..]; }
        else if (cmd.StartsWith("MsiExec", StringComparison.OrdinalIgnoreCase)) { exe = "msiexec.exe"; args = cmd[(cmd.IndexOf(' ') + 1)..].Replace("/I", "/X"); }
        else { int sp = cmd.IndexOf(".exe ", StringComparison.OrdinalIgnoreCase); exe = sp > 0 ? cmd[..(sp + 4)] : cmd; args = sp > 0 ? cmd[(sp + 5)..] : ""; }
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(exe, args) { UseShellExecute = true });
    }
}
