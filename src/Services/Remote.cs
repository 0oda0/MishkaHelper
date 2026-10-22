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
    static int _fails;
    static DateTime _failWindow;
    const string Rule = "Mishka Helper Remote";

    public static bool Running => _http?.IsListening == true;

    public static string Url => $"http://{LocalIp()}:{Settings.Current.RemotePort}";

    public static void Apply()
    {
        Stop();
        var s = Settings.Current;
        if (!s.RemoteEnabled) return;
        if (s.RemotePin.Length < 4) { s.RemotePin = Random.Shared.Next(1000, 10000).ToString(); Settings.Save(); }
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
        try { Process.Start(new ProcessStartInfo("netsh", del) { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(5000); } catch { }
        if (on) try { Process.Start(new ProcessStartInfo("netsh", add) { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(5000); } catch (Exception e) { Log.Error(e); }
    }

    public static string LocalIp()
    {
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
            var path = req.Url!.AbsolutePath.TrimEnd('/');
            if (path == "") { await Send(resp, Page, "text/html; charset=utf-8"); return; }
            if (!path.StartsWith("/api/")) { resp.StatusCode = 404; return; }

            // Brute-force guard: 10 wrong PINs per minute, then everything is refused until the minute passes.
            if (DateTime.Now - _failWindow > TimeSpan.FromMinutes(1)) { _fails = 0; _failWindow = DateTime.Now; }
            if (_fails >= 10) { resp.StatusCode = 429; return; }
            var pin = req.Headers["X-Pin"] ?? "";
            if (!CryptographicEquals(pin, Settings.Current.RemotePin)) { _fails++; resp.StatusCode = 401; return; }

            var cmd = path[5..];
            string body = "";
            if (req.HasEntityBody) { using var r = new StreamReader(req.InputStream, Encoding.UTF8); body = await r.ReadToEndAsync(); }
            var result = await Application.Current.Dispatcher.InvokeAsync(() => Do(cmd, body)).Task.Unwrap();
            await Send(resp, JsonSerializer.Serialize(result), "application/json; charset=utf-8");
        }
        catch (Exception e) { Log.Error(e); try { resp.StatusCode = 500; } catch { } }
        finally { try { resp.Close(); } catch { } }
    }

    static bool CryptographicEquals(string a, string b) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    static async Task Send(HttpListenerResponse r, string text, string type)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        r.ContentType = type; r.ContentLength64 = bytes.Length;
        r.Headers["Cache-Control"] = "no-store";
        await r.OutputStream.WriteAsync(bytes);
    }

    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    static void Key(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 2, UIntPtr.Zero); }

    static async Task<object> Do(string cmd, string arg)
    {
        switch (cmd)
        {
            case "status": break;
            case "volume": if (int.TryParse(arg, out var v)) Audio.Volume = v; break;
            case "mute": Audio.Muted = !Audio.Muted; break;
            case "brightness": if (int.TryParse(arg, out var br)) Display.Brightness = br; break;
            case "play": Key(0xB3); break;
            case "next": Key(0xB0); break;
            case "prev": Key(0xB1); break;
            case "profile": Profiles.Apply(arg); break;
            case "boost": await Cleaner.BoostAll(); break;
            case "lock": Toolbox.Lock(); break;
            case "screenOff": Toolbox.ScreenOff(); break;
            case "sleep": _ = Task.Delay(500).ContinueWith(_ => System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false)); break;
            case "shutdown": Process.Start(new ProcessStartInfo("shutdown", "/s /t 30") { CreateNoWindow = true, UseShellExecute = false }); return new { say = "Выключение через 30 с" };
            case "cancelShutdown": Process.Start(new ProcessStartInfo("shutdown", "/a") { CreateNoWindow = true, UseShellExecute = false }); return new { say = "Выключение отменено" };
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
        };
    }

    const string Page = """
<!doctype html><html lang="ru"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Mishka Remote</title><style>
:root{--bg:#0b0b14;--card:#151527;--a:#ff3da8;--b:#3dc8ff;--t:#eef;--m:#99a}
*{box-sizing:border-box}body{margin:0;background:var(--bg);color:var(--t);font:15px system-ui,sans-serif;padding:16px}
h1{font-size:20px;margin:4px 0 12px;background:linear-gradient(90deg,var(--a),var(--b));-webkit-background-clip:text;color:transparent}
.c{background:var(--card);border-radius:16px;padding:14px;margin-bottom:12px;box-shadow:0 0 18px #ff3da822}
.r{display:flex;gap:8px;flex-wrap:wrap}button{flex:1;min-width:30%;padding:14px 8px;border:0;border-radius:12px;background:#22223a;color:var(--t);font-size:15px}
button:active{background:var(--a)}button.d{background:#4a1530}input[type=range]{width:100%;accent-color:var(--a)}
input[type=text],input[type=password]{width:100%;padding:12px;border-radius:10px;border:1px solid #334;background:#0e0e1c;color:var(--t);font-size:16px}
.m{color:var(--m);font-size:13px}#st{display:flex;justify-content:space-between}
</style></head><body>
<h1>Mishka Remote <span id="pc" class="m"></span></h1>
<div id="login" class="c" hidden><div class="m">PIN из Mishka Helper → Инструменты → «Пульт с телефона»</div><br>
<input id="pin" type="password" inputmode="numeric"><br><br><button onclick="savePin()">Войти</button></div>
<div id="app" hidden>
<div class="c" id="st"><span id="cpu"></span><span id="ram"></span><span id="bat"></span></div>
<div class="c"><div class="m">Громкость <span id="vv"></span></div><input id="vol" type="range" min="0" max="100" onchange="api('volume',this.value)">
<div class="r"><button onclick="api('prev')">⏮</button><button onclick="api('play')">⏯</button><button onclick="api('next')">⏭</button><button onclick="api('mute')">🔇</button></div></div>
<div class="c"><div class="m">Яркость <span id="bv"></span></div><input id="bri" type="range" min="0" max="100" onchange="api('brightness',this.value)"></div>
<div class="c"><div class="m">Профили</div><div class="r" id="prof"></div><br><div class="r"><button onclick="api('boost')">🚀 Ускорить</button></div></div>
<div class="c" id="jv" hidden><div class="m">Сказать Джарвису</div><input id="q" type="text" placeholder="что у меня завтра?">
<br><br><button onclick="say()">Отправить</button><p id="ans" class="m"></p></div>
<div class="c"><div class="r"><button onclick="api('lock')">🔒 Блок</button><button onclick="api('screenOff')">🌙 Экран</button><button onclick="if(confirm('Сон?'))api('sleep')">💤 Сон</button></div><br>
<div class="r"><button class="d" onclick="if(confirm('Выключить ПК через 30 секунд?'))api('shutdown')">⏻ Выключить</button><button onclick="api('cancelShutdown')">Отмена</button></div></div>
</div><script>
let pin=localStorage.pin||'';
function savePin(){pin=document.getElementById('pin').value;localStorage.pin=pin;load()}
async function api(c,v){const r=await fetch('/api/'+c,{method:'POST',headers:{'X-Pin':pin},body:v??''});
if(r.status==401){show(false);return null}if(!r.ok)return null;const j=await r.json();if(j.say)alert(j.say);if(j.volume!==undefined)render(j);return j}
function show(ok){document.getElementById('login').hidden=ok;document.getElementById('app').hidden=!ok}
function render(s){show(true);pc.textContent=s.pc;vol.value=s.volume;vv.textContent=s.volume+'%';bri.value=s.brightness;bv.textContent=s.brightness+'%';
cpu.textContent='CPU '+s.cpu+'°';ram.textContent='RAM '+s.ram+'%';bat.textContent=s.battery>=0?(s.ac?'⚡':'🔋')+s.battery+'%':'';
jv.hidden=!s.jarvis;if(!prof.childElementCount)for(const p of s.profiles){const b=document.createElement('button');b.textContent=p;b.onclick=()=>api('profile',p);prof.append(b)}}
async function say(){ans.textContent='…';const r=await fetch('/api/say',{method:'POST',headers:{'X-Pin':pin},body:q.value});const j=await r.json();ans.textContent=j.say||'';q.value=''}
function load(){api('status')}
pin?load():show(false);setInterval(()=>{if(pin&&!document.hidden)api('status')},5000);
</script></body></html>
""";
}
