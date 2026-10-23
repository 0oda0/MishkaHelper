using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;

namespace MishkaHelper.Core;

public record UpdateInfo(Version Version, string Notes, string? SetupUrl, string? PortableUrl, string Page);

/// <summary>
/// In-app updates from GitHub Releases, per edition (full / Lite). Installed copies run the silent installer;
/// portable copies swap their folder via a tiny script after the app exits. Settings and models live elsewhere and stay.
/// </summary>
public static class Updater
{
    const string Repo = "0oda0/MishkaHelper";
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(30) };
    static Updater() => Http.DefaultRequestHeaders.UserAgent.ParseAdd("MishkaHelper-Updater");


    /// <summary>Installed = lives in Program Files (Inno setup or our local installer); otherwise portable.</summary>
    public static bool IsInstalled =>
        AppContext.BaseDirectory.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), StringComparison.OrdinalIgnoreCase);

    public static UpdateInfo? Available { get; private set; }
    static readonly Dictionary<string, string> Digests = new();   // download url → sha256 hex from the release

    /// <summary>Early dev installs wrote their own "MishkaHelper" uninstall entry; the Inno setup has its own → drop the duplicate.</summary>
    public static void CleanLegacyUninstallEntry()
    {
        try
        {
            if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"))) return;
            Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\MishkaHelper", false);
        }
        catch { }
    }

    /// <summary>Latest release newer than us, or null.</summary>
    /// <summary>"MishkaHelper-Setup-0.4.0.exe" must not match the Lite edition and vice versa.</summary>
    internal static bool AssetMatches(string n, string kind, string ext, bool full)
    {
        bool lite = n.StartsWith("MishkaHelper-Lite-", StringComparison.OrdinalIgnoreCase);
        return lite == !full && n.StartsWith((full ? "MishkaHelper-" : "MishkaHelper-Lite-") + kind, StringComparison.OrdinalIgnoreCase) && n.EndsWith(ext, StringComparison.OrdinalIgnoreCase);
    }

    public static async Task<UpdateInfo?> Check()
    {
        var json = await Http.GetStringAsync($"https://api.github.com/repos/{Repo}/releases/latest");
        using var doc = JsonDocument.Parse(json);
        var r = doc.RootElement;
        if (!Version.TryParse(r.GetProperty("tag_name").GetString()!.TrimStart('v'), out var v) || v <= Edition.Version) return Available = null;

        string? Asset(string kind, string ext)
        {
            foreach (var a in r.GetProperty("assets").EnumerateArray())
            {
                if (AssetMatches(a.GetProperty("name").GetString()!, kind, ext, Edition.HasJarvis))
                {
                    var u = a.GetProperty("browser_download_url").GetString()!;
                    if (a.TryGetProperty("digest", out var d) && d.GetString() is { } dg && dg.StartsWith("sha256:")) Digests[u] = dg[7..];
                    return u;
                }
            }
            return null;
        }
        return Available = new UpdateInfo(v, r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
            Asset("Setup-", ".exe"), Asset("Portable-", ".zip"), r.GetProperty("html_url").GetString()!);
    }

    /// <summary>Download and apply; the app quits at the end and the new version starts by itself.</summary>
    public static async Task Install(UpdateInfo u, Action<double>? progress = null)
    {
        // Not %TEMP%: another program could swap the installer there between download and launch (we run it as admin).
        var dir = Path.Combine(AppPaths.Shared, "update");
        if (Directory.Exists(dir)) Directory.Delete(dir, true);
        Directory.CreateDirectory(dir);
        string? url = IsInstalled ? u.SetupUrl ?? u.PortableUrl : u.PortableUrl ?? u.SetupUrl;
        if (url == null) { Shell.Open(u.Page); throw new InvalidOperationException("В релизе нет файла для этой редакции — открыл страницу загрузки"); }

        var file = Path.Combine(dir, Path.GetFileName(new Uri(url).LocalPath));
        await Download(url, file, progress);
        // GitHub publishes a SHA-256 for every release file: a damaged or tampered download is never run.
        if (Digests.TryGetValue(url, out var want))
        {
            await using (var fs = File.OpenRead(file))
                if (!Convert.ToHexString(await System.Security.Cryptography.SHA256.HashDataAsync(fs)).Equals(want, StringComparison.OrdinalIgnoreCase))
                {
                    fs.Close(); File.Delete(file);
                    throw new InvalidOperationException("Файл обновления повреждён (контрольная сумма не совпала) — попробуй ещё раз");
                }
        }
        Log.Info($"update {Edition.Version} → {u.Version} via {Path.GetFileName(file)}");

        var appDir = AppContext.BaseDirectory.TrimEnd('\\');
        if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            // Inno Setup: silent, same folder; its [Run] entry starts the app again afterwards.
            Process.Start(new ProcessStartInfo(file, $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /DIR=\"{appDir}\"") { UseShellExecute = true });
        }
        else
        {
            var unpack = Path.Combine(dir, "unpacked");
            if (Directory.Exists(unpack)) Directory.Delete(unpack, true);
            ZipFile.ExtractToDirectory(file, unpack);
            var exe = Path.Combine(appDir, "MishkaHelper.exe");
            var script = Path.Combine(dir, "apply.cmd");
            // Wait for us to exit, copy the new files over, start again.
            File.WriteAllText(script,
                "@echo off\r\n" +
                $":wait\r\ntasklist /fi \"PID eq {Environment.ProcessId}\" | find \"{Environment.ProcessId}\" >nul && (timeout /t 1 /nobreak >nul & goto wait)\r\n" +
                "taskkill /f /im llama-server.exe >nul 2>&1\r\n" +
                $"robocopy \"{unpack}\" \"{appDir}\" /E /IS /IT /R:3 /W:1 >nul\r\n" +
                $"start \"\" \"{exe}\"\r\n",
                new System.Text.UTF8Encoding(false));
            Process.Start(new ProcessStartInfo(Shell.Sys("cmd"), $"/c \"{script}\"") { UseShellExecute = false, CreateNoWindow = true });
        }
        System.Windows.Application.Current.Dispatcher.Invoke(() => App.Current.Quit());
    }

    static async Task Download(string url, string path, Action<double>? progress)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? 0, done = 0;
        await using var src = await resp.Content.ReadAsStreamAsync();
        await using var dst = File.Create(path);
        var buf = new byte[1 << 16];
        int n;
        while ((n = await src.ReadAsync(buf)) > 0)
        {
            await dst.WriteAsync(buf.AsMemory(0, n));
            done += n;
            if (total > 0) progress?.Invoke((double)done / total);
        }
    }

    /// <summary>Background daily check → toast; clicking it updates.</summary>
    public static async Task CheckInBackground(bool silent)
    {
        try
        {
            var u = await Check();
            Settings.Current.LastUpdateCheck = DateTime.Now;
            if (u == null) { if (!silent) Osd.Show("", $"Последняя версия ({Edition.Version})"); return; }
            Toast.Show("", $"Доступна версия {u.Version}", "Нажми, чтобы обновиться — займёт минуту, настройки сохранятся", () => _ = InstallWithOsd(u));
        }
        catch (Exception e) { if (!silent) Osd.Show("", "Не удалось проверить: " + e.Message); }
    }

    public static async Task InstallWithOsd(UpdateInfo u)
    {
        try { await Install(u, f => System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Osd.Show("", $"Обновление {u.Version}", (int)(f * 100)))); }
        catch (Exception e) { Osd.Show("", e.Message); }
    }
}
