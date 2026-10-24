using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace MishkaHelper.Core;

/// <summary>
/// Phone remote: a small web page on the home network (http://PC-IP:port), protected by a PIN.
/// Off by default; only private-network addresses are answered, and the firewall rule is limited to the Private profile.
/// </summary>
public static class Remote
{
    static HttpListener? _http;
    const string Rule = "Mishka Helper Remote";

    public static bool Running => _http?.IsListening == true;

    public static string Url => $"http://{LocalIp()}:{Settings.Current.RemotePort}";

    /// <summary>Self-test hook: serve on localhost only (works without admin, no firewall rule).</summary>
    internal static void StartLocal()
    {
        Stop();
        _http = new HttpListener();
        _http.Prefixes.Add($"http://localhost:{Settings.Current.RemotePort}/");
        _http.Start();
        _ = Task.Run(Loop);
    }

    public static void Apply()
    {
        Stop();
        var s = Settings.Current;
        if (!s.RemoteEnabled) return;
        EnsurePin();
        Firewall(true);
        _http = new HttpListener();
        _http.Prefixes.Add($"http://+:{s.RemotePort}/");   // "+" needs admin — the app is elevated
        _http.Start();
        _ = Task.Run(Loop);
    }

    public static void Stop()
    {
        try { _http?.Stop(); _http?.Close(); } catch { }
        _http = null;
    }

    public static void Disable() { Stop(); Firewall(false); }

    static void Firewall(bool on)
    {
        var del = $"advfirewall firewall delete rule name=\"{Rule}\"";
        var add = $"advfirewall firewall add rule name=\"{Rule}\" dir=in action=allow protocol=TCP localport={Settings.Current.RemotePort} profile=private";
        try { Process.Start(new ProcessStartInfo(Shell.Sys("netsh"), del) { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(5000); } catch { }
        if (on) try { Process.Start(new ProcessStartInfo(Shell.Sys("netsh"), add) { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(5000); } catch (Exception e) { Log.Error(e); }
    }

    static readonly System.Text.RegularExpressions.Regex VirtualNic = new(@"virtual|hyper-v|vethernet|vmware|virtualbox|docker|wsl|tap|tun|wireguard|vpn|loopback|xray|v2ray|outline|zerotier|tailscale|radmin|hamachi", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>The address the phone can reach: a real Wi-Fi/Ethernet adapter with a gateway (VPN, Docker and WSL adapters skipped).</summary>
    public static string LocalIp()
    {
        var real = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up
                && n.NetworkInterfaceType is System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 or System.Net.NetworkInformation.NetworkInterfaceType.Ethernet
                && !VirtualNic.IsMatch(n.Description + " " + n.Name))
            .Select(n => (n, p: n.GetIPProperties()))
            .Where(x => x.p.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
            .OrderBy(x => x.n.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Wireless80211 ? 0 : 1)
            .SelectMany(x => x.p.UnicastAddresses.Select(u => u.Address))
            .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && IsPrivate(a));
        if (real != null) return real.ToString();
        try
        {
            // The interface Windows would use to reach the internet = the LAN address the phone can see.
            using var s = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            s.Connect("8.8.8.8", 53);
            return ((IPEndPoint)s.LocalEndPoint!).Address.ToString();
        }
        catch { return "localhost"; }
    }

    public static bool IsPrivate(IPAddress a)
    {
        if (IPAddress.IsLoopback(a)) return true;
        if (a.IsIPv4MappedToIPv6) a = a.MapToIPv4();
        if (a.AddressFamily == AddressFamily.InterNetworkV6) return a.IsIPv6LinkLocal || a.IsIPv6UniqueLocal;
        var b = a.GetAddressBytes();
        return b[0] == 10 || b[0] == 172 && b[1] is >= 16 and <= 31 || b[0] == 192 && b[1] == 168 || b[0] == 169 && b[1] == 254;
    }

    static async Task Loop()
    {
        var http = _http;
        while (http?.IsListening == true)
        {
            HttpListenerContext ctx;
            try { ctx = await http.GetContextAsync(); } catch { return; }
            _ = Task.Run(() => Serve(ctx));
        }
    }

    static async Task Serve(HttpListenerContext ctx)
    {
        var req = ctx.Request; var resp = ctx.Response;
        try
        {
            if (!IsPrivate(req.RemoteEndPoint.Address)) { resp.StatusCode = 403; return; }
            // DNS rebinding: a web page could point its own domain name at this PC; real visitors use the IP.
            if (Uri.CheckHostName(req.Url!.Host) == UriHostNameType.Dns && req.Url.Host != "localhost") { resp.StatusCode = 403; return; }
            var path = req.Url!.AbsolutePath.TrimEnd('/');
            if (path == "") { await Send(resp, Page, "text/html; charset=utf-8"); return; }
            if (!path.StartsWith("/api/")) { resp.StatusCode = 404; return; }

            var who = req.RemoteEndPoint.Address.ToString();
            if (!CheckPin(req.Headers["X-Pin"] ?? "", "Wi-Fi " + who)) { resp.StatusCode = Locked ? 429 : 401; return; }

            var cmd = path[5..];
            if (cmd == "upload") { await Upload(req, resp); return; }
            if (cmd == "screen")
            {
                var jpg = await Task.Run(() => InputSim.Screenshot(int.TryParse(req.QueryString["w"], out var w) ? Math.Clamp(w, 320, 1920) : 1280));
                resp.ContentType = "image/jpeg"; resp.ContentLength64 = jpg.Length; resp.Headers["Cache-Control"] = "no-store";
                await resp.OutputStream.WriteAsync(jpg);
                return;
            }
            string body = "";
            if (req.ContentLength64 > 64 * 1024) { resp.StatusCode = 413; return; }
            if (req.HasEntityBody) { using var r = new StreamReader(req.InputStream, Encoding.UTF8); body = await r.ReadToEndAsync(); }
            if (body.Length > 64 * 1024) { resp.StatusCode = 413; return; }
            var result = await Application.Current.Dispatcher.InvokeAsync(() => Do(cmd, body)).Task.Unwrap();
            await Send(resp, JsonSerializer.Serialize(result), "application/json; charset=utf-8");
        }
        catch (Exception e) { Log.Error(e); try { resp.StatusCode = 500; } catch { } }
        finally { try { resp.Close(); } catch { } }
    }

    public static readonly string Inbox = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads", "С телефона");

    /// <summary>Phone → PC file: raw body, name in ?name=. Saved under Downloads, never opened or run by us.</summary>
    static async Task Upload(HttpListenerRequest req, HttpListenerResponse resp)
    {
        const long Max = 2L << 30;   // 2 GB: phone videos
        if (req.ContentLength64 > Max) { resp.StatusCode = 413; return; }
        var raw = Path.GetFileName(req.QueryString["name"] ?? "") ?? "";
        var name = string.Concat(raw.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c)).Trim(' ', '.');
        if (name.Length == 0) name = $"файл-{DateTime.Now:yyyyMMdd-HHmmss}";
        if (name.Length > 120) name = name[..80] + Path.GetExtension(name);
        Directory.CreateDirectory(Inbox);
        var target = Path.Combine(Inbox, name);
        for (int i = 2; File.Exists(target); i++) target = Path.Combine(Inbox, $"{Path.GetFileNameWithoutExtension(name)} ({i}){Path.GetExtension(name)}");
        long got = 0;
        try
        {
            await using var dst = new FileStream(target, FileMode.CreateNew);
            var buf = new byte[1 << 16]; int n;
            while ((n = await req.InputStream.ReadAsync(buf)) > 0)
            {
                if ((got += n) > Max) throw new InvalidDataException("too big");
                await dst.WriteAsync(buf.AsMemory(0, n));
            }
        }
        catch { try { File.Delete(target); } catch { } throw; }
        var file = target;
        Application.Current.Dispatcher.BeginInvoke(() => Toast.Show("", "С телефона", $"{Path.GetFileName(file)} ({Cleaner.Size(got)})", () => Shell.Open(Shell.Sys("explorer"), $"/select,\"{file}\"")));
        await Send(resp, JsonSerializer.Serialize(new { say = $"Сохранено: {Path.GetFileName(file)}" }), "application/json; charset=utf-8");
    }

    static Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager? _media;

    /// <summary>"Artist — Title" of what Windows media controls show now, or "".</summary>
    public static async Task<string> NowPlaying()
    {
        try
        {
            _media ??= await Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if (_media.GetCurrentSession() is not { } s) return "";
            var p = await s.TryGetMediaPropertiesAsync();
            return string.Join(" — ", new[] { p.Artist, p.Title }.Where(x => !string.IsNullOrWhiteSpace(x)));
        }
        catch { return ""; }
    }

    static bool CryptographicEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    /// <summary>6 random digits (crypto RNG). Older 4-digit PINs are replaced: 10 000 variants is too few for a remote with full keyboard control.</summary>
    public static void EnsurePin()
    {
        var s = Settings.Current;
        if (s.RemotePin.Length >= 6) return;
        s.RemotePin = System.Security.Cryptography.RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        Settings.Save();
    }

    // ---- PIN guard shared by Wi-Fi and Bluetooth: 5 misses → locked 1 min, each further lock twice as long (max 1 h). ----
    static readonly object Guard = new();
    static int _misses, _locks;
    static DateTime _lockedUntil;
    static readonly HashSet<string> Known = new();

    public static bool Locked { get { lock (Guard) return DateTime.Now < _lockedUntil; } }

    /// <summary>Checks a PIN attempt; tells the user about brute force and about every new phone that gets in.</summary>
    public static bool CheckPin(string pin, string who)
    {
        bool ok, lockedNow = false, newDevice = false;
        lock (Guard)
        {
            if (DateTime.Now < _lockedUntil) return false;
            ok = Settings.Current.RemotePin.Length >= 6 && CryptographicEquals(pin, Settings.Current.RemotePin);
            if (ok) { _misses = 0; _locks = 0; newDevice = Known.Add(who); }
            else if (++_misses >= 5)
            {
                _misses = 0;
                _lockedUntil = DateTime.Now.AddMinutes(Math.Min(60, 1 << Math.Min(6, _locks++)));
                lockedNow = true;
            }
        }
        if (lockedNow) Notify($"Кто-то подбирает PIN пульта ({who}). Пульт заблокирован до {_lockedUntil:HH:mm}. Если это не ты — смени PIN.");
        else if (newDevice) Notify($"К пульту подключились: {who}");
        return ok;
    }

    static void Notify(string text)
    {
        Log.Info("remote: " + text);
        Application.Current?.Dispatcher.BeginInvoke(() => Toast.Show("", "Пульт", text, () => App.Current.Navigate("Tools")));
    }

    static async Task Send(HttpListenerResponse r, string text, string type)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        r.ContentType = type; r.ContentLength64 = bytes.Length;
        r.Headers["Cache-Control"] = "no-store";
        r.Headers["X-Frame-Options"] = "DENY";   // no clickjacking via a frame on another page
        r.Headers["X-Content-Type-Options"] = "nosniff";
        await r.OutputStream.WriteAsync(bytes);
    }

    static (double, double) Pair(string s)
    {
        var p = s.Split(',');
        double Num(int i) => p.Length > i && double.TryParse(p[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        return (Num(0), Num(1));
    }

    /// <summary>One command from any remote (Wi-Fi page or Bluetooth). Runs on the UI thread.</summary>
    public static async Task<object> Do(string cmd, string arg)
    {
        switch (cmd)
        {
            case "status": break;
            case "mouse": { var (dx, dy) = Pair(arg); InputSim.Move((int)dx, (int)dy); return new { }; }
            case "click": InputSim.Click(arg == "double" ? "left" : arg is "right" or "middle" ? arg : "left", arg == "double"); return new { };
            case "scroll": InputSim.Scroll(int.TryParse(arg, out var n) ? Math.Clamp(n, -20, 20) : 0); return new { };
            case "tap": { var (x, y) = Pair(arg); InputSim.TapAt(x, y, arg.EndsWith(",right") ? "right" : "left"); return new { }; }
            case "type": InputSim.Type(arg.Length > 2000 ? arg[..2000] : arg); return new { };
            case "key": return new { ok = InputSim.Key(arg) };
            case "clipGet":
                var clip = System.Windows.Clipboard.ContainsText() ? System.Windows.Clipboard.GetText() : "";
                return new { clip = clip.Length > 20000 ? clip[..20000] : clip };
            case "clipSet": System.Windows.Clipboard.SetText(arg); return new { say = "Текст в буфере ПК" };
            case "launch": Actions.Run("launch:" + arg); return new { };
            case "volume": if (int.TryParse(arg, out var v)) Audio.Volume = v; break;
            case "mute": Audio.Muted = !Audio.Muted; break;
            case "brightness": if (int.TryParse(arg, out var br)) Display.Brightness = br; break;
            case "play": InputSim.Key("play"); break;
            case "next": InputSim.Key("next"); break;
            case "prev": InputSim.Key("prev"); break;
            case "profile": Profiles.Apply(arg); break;
            case "boost": await Cleaner.BoostAll(); break;
            case "lock": Toolbox.Lock(); break;
            case "screenOff": Toolbox.ScreenOff(); break;
            case "sleep": _ = Task.Delay(500).ContinueWith(_ => System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false)); break;
            case "shutdown": Process.Start(new ProcessStartInfo(Shell.Sys("shutdown"), "/s /t 30") { CreateNoWindow = true, UseShellExecute = false }); return new { say = "Выключение через 30 с" };
            case "cancelShutdown": Process.Start(new ProcessStartInfo(Shell.Sys("shutdown"), "/a") { CreateNoWindow = true, UseShellExecute = false }); return new { say = "Выключение отменено" };
#if !LITE
            case "say":
                var tcs = new TaskCompletionSource<string>();
                void OnMsg(string t, bool user) { if (!user) tcs.TrySetResult(t); }
                Jarvis.Message += OnMsg;
                try { await Jarvis.Handle(arg); return new { say = await Task.WhenAny(tcs.Task, Task.Delay(30000)) == tcs.Task ? tcs.Task.Result : "…" }; }
                finally { Jarvis.Message -= OnMsg; }
#endif
            default: return new { error = "unknown" };
        }
        var st = SysMon.Last; var bat = Power.Battery();
        return new
        {
            pc = Environment.MachineName, volume = Audio.Volume, muted = Audio.Muted, brightness = Display.Brightness,
            cpu = st.CpuTemp, ram = Math.Round(st.Ram), battery = bat.Present ? bat.Percent : -1, ac = bat.OnAc,
            profiles = Settings.Current.Profiles.Select(p => p.Name), jarvis = Edition.HasJarvis,
            apps = Settings.Current.Launchers.Select(l => l.Name), playing = await NowPlaying(),
        };
    }

    // Same page as docs/remote (GitHub Pages serves it for Bluetooth); embedded so Wi-Fi works offline.
    static readonly string Page = new StreamReader(typeof(Remote).Assembly.GetManifestResourceStream("remote.html")!).ReadToEnd();
}
