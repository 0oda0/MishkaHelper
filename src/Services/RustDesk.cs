using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;

namespace MishkaHelper.Core;

/// <summary>
/// Access from anywhere = RustDesk (free, open source, encrypted, its own relay servers). We don't run our own
/// internet-facing server for the PC — too much risk. This only finds or downloads the official build and opens it.
/// </summary>
public static class RustDesk
{
    static readonly string Mine = Path.Combine(AppPaths.Shared, "rustdesk", "rustdesk.exe");

    static string? Installed()
    {
        foreach (var p in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            var f = Path.Combine(p, "RustDesk", "rustdesk.exe");
            if (File.Exists(f)) return f;
        }
        return File.Exists(Mine) ? Mine : null;
    }

    public static bool Present => Installed() != null;

    /// <summary>Open RustDesk, downloading the latest official release first if needed. progress: 0..1.</summary>
    public static async Task Open(Action<double>? progress = null)
    {
        var exe = Installed();
        if (exe == null)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("MishkaHelper");
            using var doc = JsonDocument.Parse(await http.GetStringAsync("https://api.github.com/repos/rustdesk/rustdesk/releases/latest"));
            var asset = doc.RootElement.GetProperty("assets").EnumerateArray()
                .FirstOrDefault(a => System.Text.RegularExpressions.Regex.IsMatch(a.GetProperty("name").GetString()!, @"^rustdesk-[\d.]+-x86_64\.exe$"));
            if (asset.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException("Не нашёл RustDesk для Windows в последнем релизе");
            Directory.CreateDirectory(Path.GetDirectoryName(Mine)!);
            var tmp = Mine + ".part";
            using (var resp = await http.GetAsync(asset.GetProperty("browser_download_url").GetString(), HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? 25 << 20, done = 0;
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(tmp);
                var buf = new byte[1 << 16]; int n;
                while ((n = await src.ReadAsync(buf)) > 0) { await dst.WriteAsync(buf.AsMemory(0, n)); done += n; progress?.Invoke((double)done / total); }
            }
            // Only a validly signed build gets run.
            if (Security.Signature(tmp) is not { Length: > 0 } signer) { File.Delete(tmp); throw new InvalidOperationException("Скачанный RustDesk без цифровой подписи — не запускаю"); }
            Log.Info("RustDesk signed by " + signer);
            File.Move(tmp, Mine, true);
            exe = Mine;
        }
        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true });
    }

    /// <summary>
    /// "Помоги мне": RustDesk opens and the message for the friend (with this PC's ID) goes to the clipboard.
    /// The password stays in RustDesk's own window: it changes every session, and only the person at the PC should hand it out.
    /// </summary>
    public static async Task<string?> AskHelp(Action<double>? progress = null)
    {
        await Open(progress);
        for (int i = 0; i < 30; i++)
        {
            if (Id() is { } id) return id;
            await Task.Delay(500);
        }
        return null;
    }

    /// <summary>This PC's RustDesk ID from its config (written on its first start).</summary>
    public static string? Id()
    {
        foreach (var dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), @"ServiceProfiles\LocalService\AppData\Roaming") })
        {
            try
            {
                var f = Path.Combine(dir, "RustDesk", "config", "RustDesk.toml");
                if (File.Exists(f) && ParseId(File.ReadAllText(f)) is { } id) return id;
            }
            catch { }
        }
        return null;
    }

    internal static string? ParseId(string toml) =>
        System.Text.RegularExpressions.Regex.Match(toml, @"(?m)^\s*id\s*=\s*['""](\d{6,12})['""]") is { Success: true } m ? m.Groups[1].Value : null;

    public static string Message(string id) =>
        $"Помоги с компьютером, пожалуйста. Поставь RustDesk (rustdesk.com) и подключись: ID {Spaced(id)}. Пароль скажу, когда будешь готов.";

    static string Spaced(string id) => string.Join(" ", Enumerable.Range(0, (id.Length + 2) / 3).Select(i => id.Substring(i * 3, Math.Min(3, id.Length - i * 3))));
}
