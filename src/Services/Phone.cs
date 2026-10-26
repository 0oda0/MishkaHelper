using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;

namespace MishkaHelper.Core;

/// <summary>
/// The Android app ("Mishka" for phone): finds the PC by UDP broadcast, then keeps one WebSocket open (same PIN as the remote).
/// Phone → PC: status (battery, storage), notifications, clipboard. PC → phone: ring, clipboard, files.
/// </summary>
public static class Phone
{
    public const int DiscoveryPort = 8771;

    public record Status(string Name, int Battery, bool Charging, long StorageFree, long StorageTotal, DateTime At);
    public record Notification(string App, string Title, string Text, DateTime At);

    static WebSocket? _ws;
    static readonly SemaphoreSlim SendLock = new(1, 1);
    public static Status? Last { get; private set; }
    public static readonly List<Notification> Notifications = new();
    public static bool Connected => _ws?.State == WebSocketState.Open;
    public static event Action? Changed;

    /// <summary>Files offered to the phone: id → path. Only what the user picked on the PC, for an hour.</summary>
    static readonly ConcurrentDictionary<string, (string Path, DateTime Until)> Offers = new();

    // ---------- discovery ----------
    static UdpClient? _udp;

    /// <summary>"MISHKA?" from the phone → "MISHKA|pc name|port". Only while the remote is on (and only on private networks).</summary>
    public static void StartDiscovery()
    {
        StopDiscovery();
        try
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, DiscoveryPort));
            _ = Task.Run(async () =>
            {
                var udp = _udp;
                while (udp != null)
                {
                    UdpReceiveResult r;
                    try { r = await udp.ReceiveAsync(); } catch { return; }
                    if (!Remote.IsPrivate(r.RemoteEndPoint.Address) || Encoding.UTF8.GetString(r.Buffer) != "MISHKA?") continue;
                    var reply = Encoding.UTF8.GetBytes($"MISHKA|{Environment.MachineName}|{Settings.Current.RemotePort}|{Edition.Version}");
                    try { await udp.SendAsync(reply, r.RemoteEndPoint); } catch { }
                }
            });
        }
        catch (Exception e) { Log.Error(e); }
    }

    public static void StopDiscovery() { try { _udp?.Close(); } catch { } _udp = null; }

    // ---------- websocket ----------
    /// <summary>Called by Remote after the PIN check. Lives until the phone disconnects; a new phone replaces the old one.</summary>
    public static async Task Accept(HttpListenerContext ctx)
    {
        var wsCtx = await ctx.AcceptWebSocketAsync(null, TimeSpan.FromSeconds(20));
        var ws = wsCtx.WebSocket;
        var old = _ws;
        _ws = ws;
        // Output-only close: the old socket's own loop is still inside ReceiveAsync, and CloseAsync would race it.
        try { if (old != null) await old.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "replaced", CancellationToken.None); } catch { }
        Fire();
        var buf = new byte[64 * 1024];
        using var msg = new MemoryStream();
        try
        {
            while (ws.State == WebSocketState.Open)
            {
                msg.SetLength(0);
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(buf, CancellationToken.None);
                    if (r.MessageType == WebSocketMessageType.Close) break;
                    msg.Write(buf, 0, r.Count);
                    if (msg.Length > 1 << 20) throw new InvalidDataException("message too big");
                } while (!r.EndOfMessage);
                if (r.MessageType == WebSocketMessageType.Close) break;
                try { Handle(JsonNode.Parse(msg.ToArray())!.AsObject()); } catch (Exception e) { Log.Error(e); }
            }
        }
        catch { }
        finally
        {
            // Answer the phone's "bye" properly, otherwise its side reports a broken connection.
            if (ws.State == WebSocketState.CloseReceived)
                try { await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
            if (_ws == ws) _ws = null;
            ws.Dispose();
            Fire();
        }
    }

    static void Fire() => Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke());

    static string S(JsonObject o, string k) => o[k]?.ToString() ?? "";

    static void Handle(JsonObject m)
    {
        switch (S(m, "t"))
        {
            case "status":
                Last = new(S(m, "name"), int.TryParse(S(m, "battery"), out var b) ? b : -1, S(m, "charging") == "true",
                    long.TryParse(S(m, "free"), out var f) ? f : 0, long.TryParse(S(m, "total"), out var tt) ? tt : 0, DateTime.Now);
                Fire();
                break;
            case "notif":
                var n = new Notification(S(m, "app"), S(m, "title"), S(m, "text"), DateTime.Now);
                lock (Notifications) { Notifications.Insert(0, n); if (Notifications.Count > 50) Notifications.RemoveAt(50); }
                if (Focus.On) Interlocked.Increment(ref Focus.HeldNotifications);   // shown as one summary when the game ends
                else if (Settings.Current.PhoneNotifications)
                    Application.Current?.Dispatcher.BeginInvoke(() => Toast.Show("", $"{n.App}: {n.Title}", n.Text.Length > 160 ? n.Text[..160] + "…" : n.Text));
                Fire();
                break;
            case "clip":
                var text = S(m, "text");
                if (text.Length is > 0 and < 100_000)
                    Application.Current?.Dispatcher.BeginInvoke(() => { try { Clipboard.SetText(text); Osd.Show("", "Текст с телефона в буфере"); } catch { } });
                break;
        }
    }

    static async Task<bool> Send(object o)
    {
        var ws = _ws;
        if (ws?.State != WebSocketState.Open) return false;
        await SendLock.WaitAsync();
        try { await ws.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o)), WebSocketMessageType.Text, true, CancellationToken.None); return true; }
        catch { return false; }
        finally { SendLock.Release(); }
    }

    public static Task<bool> Ring(bool on = true) => Send(new { t = on ? "ring" : "ringStop" });
    public static Task<bool> SendClipboard(string text) => Send(new { t = "clip", text });
    public static Task<bool> Say(string text) => Send(new { t = "toast", text });

    public static Task<bool> SendFile(string path)
    {
        var id = Guid.NewGuid().ToString("N");
        foreach (var x in Offers.Where(x => x.Value.Until < DateTime.Now).ToList()) Offers.TryRemove(x.Key, out _);
        Offers[id] = (path, DateTime.Now.AddHours(1));
        return Send(new { t = "file", id, name = Path.GetFileName(path), size = new FileInfo(path).Length });
    }

    /// <summary>GET /api/phone/file/{id}: the phone downloads a file the user sent from the PC.</summary>
    public static async Task ServeFile(string id, HttpListenerResponse resp)
    {
        if (!Offers.TryGetValue(id, out var o) || o.Until < DateTime.Now || !File.Exists(o.Path)) { resp.StatusCode = 404; return; }
        resp.ContentType = "application/octet-stream";
        resp.ContentLength64 = new FileInfo(o.Path).Length;
        resp.AddHeader("Content-Disposition", "attachment; filename*=UTF-8''" + Uri.EscapeDataString(Path.GetFileName(o.Path)));
        await using var fs = File.OpenRead(o.Path);
        await fs.CopyToAsync(resp.OutputStream);
    }
}
