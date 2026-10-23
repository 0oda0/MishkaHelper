using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;

namespace MishkaHelper.Core;

/// <summary>
/// Microsoft Edge "Read aloud" neural voices over WebSocket — the same service the browser uses (free, no key).
/// Unofficial: needs internet and may change; Voice falls back to Piper offline.
/// </summary>
public static class EdgeTts
{
    const string Token = "6A5AA1D4EAFF4E9FB37E23D68491D6F4";
    const string ChromiumVersion = "143.0.3650.75";
    const string Wss = "wss://speech.platform.bing.com/consumer/speech/synthesize/readaloud/edge/v1";

    /// <summary>Voices that speak Russian well; multilingual ones keep a light foreign accent (very "AI butler").</summary>
    public static readonly (string Id, string Name)[] Russian =
    {
        ("ru-RU-DmitryNeural", "Дмитрий — русский, чистый"),
        ("en-US-AndrewMultilingualNeural", "Эндрю — мягкий, лёгкий акцент"),
        ("en-US-BrianMultilingualNeural", "Брайан — уверенный, лёгкий акцент"),
        ("en-AU-WilliamMultilingualNeural", "Уильям — низкий, акцент"),
        ("de-DE-FlorianMultilingualNeural", "Флориан — строгий, немецкий акцент"),
        ("fr-FR-RemyMultilingualNeural", "Реми — бархатный, французский акцент"),
        ("it-IT-GiuseppeMultilingualNeural", "Джузеппе — тёплый, итальянский акцент"),
        ("ru-RU-SvetlanaNeural", "Светлана — женский"),
    };

    /// <summary>British voices for the film-style mode (English answers).</summary>
    public static readonly (string Id, string Name)[] Movie =
    {
        ("en-GB-RyanNeural", "Ryan — британский, ближе всего к фильму"),
        ("en-GB-ThomasNeural", "Thomas — британский, мягче"),
    };

    /// <summary>Russian text can't be read by an English-only voice → a multilingual one reads it instead.</summary>
    public const string RussianFallback = "en-US-BrianMultilingualNeural";

    /// <summary>Server time minus ours: a PC clock that is off by minutes gets 403 until corrected from the server's Date.</summary>
    static double _skew;

    static string Gec() => EdgeGec.Generate(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() / 1000.0 + _skew, Token);

    static string Stamp() => DateTime.UtcNow.ToString("ddd MMM dd yyyy HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture) + " GMT+0000 (Coordinated Universal Time)";

    /// <summary>MP3 (24 kHz mono) for the text. rate/pitch in percent, e.g. +5 / -4.</summary>
    static async Task<ClientWebSocket> Connect(string conn, CancellationToken ct, bool retried = false)
    {
        var ws = new ClientWebSocket();
        ws.Options.CollectHttpResponseDetails = true;
        ws.Options.SetRequestHeader("Origin", "chrome-extension://jdiccldimpdaibmpdkjnbmckianbfold");
        ws.Options.SetRequestHeader("User-Agent", $"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/{ChromiumVersion.Split('.')[0]}.0.0.0 Safari/537.36 Edg/{ChromiumVersion.Split('.')[0]}.0.0.0");
        ws.Options.SetRequestHeader("Pragma", "no-cache");
        ws.Options.SetRequestHeader("Cache-Control", "no-cache");
        ws.Options.SetRequestHeader("Cookie", "muid=" + Convert.ToHexString(RandomNumberGenerator.GetBytes(16)) + ";");
        try
        {
            await ws.ConnectAsync(new Uri($"{Wss}?TrustedClientToken={Token}&ConnectionId={conn}&Sec-MS-GEC={Gec()}&Sec-MS-GEC-Version=2-{ChromiumVersion}"), ct);
            return ws;
        }
        catch (WebSocketException) when (!retried && ws.HttpStatusCode == System.Net.HttpStatusCode.Forbidden
                                         && ws.HttpResponseHeaders?.TryGetValue("Date", out var d) == true && DateTimeOffset.TryParse(d.FirstOrDefault(), out var server))
        {
            // Token is bound to a 5-minute window: sync with the server clock once and retry.
            _skew += (server - DateTimeOffset.UtcNow).TotalSeconds;
            ws.Dispose();
            return await Connect(conn, ct, true);
        }
        catch { ws.Dispose(); throw; }
    }

    public static async Task<byte[]> Synthesize(string text, string voice, int ratePct = 0, int pitchPct = 0, CancellationToken ct = default)
    {
        var conn = Guid.NewGuid().ToString("N");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));
        using var ws = await Connect(conn, timeout.Token);

        async Task Send(string s) => await ws.SendAsync(Encoding.UTF8.GetBytes(s), WebSocketMessageType.Text, true, timeout.Token);
        await Send($"X-Timestamp:{Stamp()}\r\nContent-Type:application/json; charset=utf-8\r\nPath:speech.config\r\n\r\n" +
                   "{\"context\":{\"synthesis\":{\"audio\":{\"metadataoptions\":{\"sentenceBoundaryEnabled\":\"false\",\"wordBoundaryEnabled\":\"false\"},\"outputFormat\":\"audio-24khz-48kbitrate-mono-mp3\"}}}}");

        var (lang, name) = SplitVoice(voice);
        var ssml = $"<speak version='1.0' xmlns='http://www.w3.org/2001/10/synthesis' xml:lang='{lang}'>" +
                   $"<voice name='Microsoft Server Speech Text to Speech Voice ({lang}, {name})'>" +
                   $"<prosody pitch='{pitchPct:+0;-0}%' rate='{ratePct:+0;-0}%' volume='+0%'>{System.Security.SecurityElement.Escape(text)}</prosody></voice></speak>";
        await Send($"X-RequestId:{conn}\r\nContent-Type:application/ssml+xml\r\nX-Timestamp:{Stamp()}Z\r\nPath:ssml\r\n\r\n{ssml}");

        using var audio = new MemoryStream();
        var buf = new byte[1 << 16];
        using var frame = new MemoryStream();
        while (true)
        {
            frame.SetLength(0);
            WebSocketReceiveResult r;
            do
            {
                r = await ws.ReceiveAsync(buf, timeout.Token);
                frame.Write(buf, 0, r.Count);
            } while (!r.EndOfMessage);
            if (r.MessageType == WebSocketMessageType.Close) break;
            var data = frame.ToArray();
            if (r.MessageType == WebSocketMessageType.Text)
            {
                if (Encoding.UTF8.GetString(data).Contains("Path:turn.end")) break;
                continue;
            }
            // Binary: 2-byte big-endian header length, headers, then audio.
            if (data.Length < 2) continue;
            int hl = data[0] << 8 | data[1];
            if (data.Length <= 2 + hl) continue;
            if (Encoding.ASCII.GetString(data, 2, hl).Contains("Path:audio")) audio.Write(data, 2 + hl, data.Length - 2 - hl);
        }
        try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        if (audio.Length == 0) throw new InvalidOperationException("Голос Edge не ответил");
        return audio.ToArray();
    }

    /// <summary>"en-US-AndrewMultilingualNeural" → ("en-US", "AndrewMultilingualNeural").</summary>
    static (string Lang, string Name) SplitVoice(string id)
    {
        var p = id.Split('-');
        return ($"{p[0]}-{p[1]}", string.Join('-', p.Skip(2)));
    }
}
