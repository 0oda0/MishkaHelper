using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;

namespace MishkaHelper.Core;

/// <summary>
/// "VPN first, then YouTube": before a site from Settings.VpnSites opens, make sure the VPN is up, starting the VPN app if needed.
/// </summary>
public static class Vpn
{
    // Proxy cores the popular clients run only while connected.
    static readonly string[] Cores = { "xray", "sing-box", "v2ray", "hysteria", "tun2socks", "openvpn", "wireguard", "amneziawg", "AmneziaVPN-service", "outline" };
    static readonly System.Text.RegularExpressions.Regex Tun = new("wintun|wireguard|tap-windows|amnezia|happ|hiddify|sing-tun|xray|outline", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    public static bool IsUp() =>
        Cores.Any(c => Process.GetProcessesByName(c).Length > 0)
        || NetworkInterface.GetAllNetworkInterfaces().Any(n => n.OperationalStatus == OperationalStatus.Up && Tun.IsMatch(n.Description + " " + n.Name));

    public static bool Needed(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && Settings.Current.VpnSites.Any(s => u.Host == s || u.Host.EndsWith("." + s));

    /// <summary>Only from Program Files (admin-writable): the app runs elevated and must not start what a user-level program dropped.</summary>
    public static string? App()
    {
        var set = Settings.Current.VpnApp;
        if (set.Length > 0) return File.Exists(set) ? set : null;
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var rel in new[] { @"FlyFrogLLC\Happ\Happ.exe", @"Hiddify\Hiddify.exe", @"AmneziaVPN\AmneziaVPN.exe", @"v2rayN\v2rayN.exe" })
            if (File.Exists(Path.Combine(pf, rel))) return Path.Combine(pf, rel);
        return null;
    }

    /// <summary>null = VPN is up (or wasn't needed); otherwise what to tell the user.</summary>
    public static async Task<string?> EnsureFor(string url)
    {
        if (!Needed(url) || IsUp()) return null;
        var app = App();
        if (app == null) return "Для этого сайта нужен VPN — включи его на ПК";
        Osd.Show("", "Включаю VPN…");
        Shell.Open(app);
        for (int i = 0; i < 20; i++)
        {
            await Task.Delay(1000);
            if (IsUp()) { await Task.Delay(1500); return null; }   // a moment for the route to settle
        }
        return $"{Path.GetFileNameWithoutExtension(app)} открыт, но не подключился — нажми «Подключить» тачпадом, потом выбери видео ещё раз";
    }
}
