using System.Diagnostics;
using System.IO;
using System.Text;

namespace MishkaHelper.Core;

public static class Shell
{
    static Shell() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>
    /// Full System32 path for a bare tool name ("netsh" → C:\Windows\System32\netsh.exe). A bare name is also looked up
    /// in the app's and the current folder first, where a planted netsh.exe would then run as admin.
    /// </summary>
    public static string Sys(string exe)
    {
        if (exe.Contains('\\') || exe.Contains('/')) return exe;
        var name = exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? exe : exe + ".exe";
        if (name.Equals("powershell.exe", StringComparison.OrdinalIgnoreCase))
            return Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
        var p = Path.Combine(Environment.SystemDirectory, name);
        return File.Exists(p) ? p : exe;
    }

    /// <summary>Run a console tool hidden and return stdout (OEM code page, so Russian output is readable).</summary>
    public static string Run(string exe, string args, int timeoutMs = 30000, Encoding? enc = null)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(Sys(exe), args)
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                StandardOutputEncoding = enc ?? Encoding.GetEncoding(866),
            })!;
            var o = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(timeoutMs)) { try { p.Kill(true); } catch { } }
            return o.Result;
        }
        catch (Exception e) { Log.Error(e); return ""; }
    }

    public static Task<string> RunAsync(string exe, string args, int timeoutMs = 30000, Encoding? enc = null) =>
        Task.Run(() => Run(exe, args, timeoutMs, enc));

    /// <summary>
    /// Open a file, folder, URL or ms-settings: link. Without arguments it goes through Explorer, so programs and
    /// browsers start with normal rights instead of inheriting our admin token.
    /// </summary>
    public static void Open(string target, string args = "")
    {
        try
        {
            bool viaExplorer = AppPaths.Elevated && args.Length == 0 && !target.Contains('"')
                               && (target.Contains('\\') || target.Contains(':'));   // a bare "taskmgr.exe" isn't something Explorer can open
            if (viaExplorer && Process.GetProcessesByName("explorer").Length > 0)
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"\"{target}\"") { UseShellExecute = false });
            else
                Process.Start(new ProcessStartInfo(target, args) { UseShellExecute = true });
        }
        catch (Exception e) { Log.Error(e); }
    }
}
