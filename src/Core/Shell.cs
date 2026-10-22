using System.Diagnostics;
using System.Text;

namespace MishkaHelper.Core;

public static class Shell
{
    static Shell() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Run a console tool hidden and return stdout (OEM code page, so Russian output is readable).</summary>
    public static string Run(string exe, string args, int timeoutMs = 30000, Encoding? enc = null)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo(exe, args)
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

    /// <summary>Open a file, folder, URL or ms-settings: link.</summary>
    public static void Open(string target, string args = "")
    {
        try { Process.Start(new ProcessStartInfo(target, args) { UseShellExecute = true }); }
        catch (Exception e) { Log.Error(e); }
    }
}
