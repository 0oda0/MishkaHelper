using System.Net.Http;
using System.Text.Json;

namespace MishkaHelper.Core;

/// <summary>
/// Yandex Music without logins or keys: the public catalog search finds the track/artist, and the desktop app's own
/// deeplinks start playback ("yandexmusic://…?deeplink=play-vibe&amp;seeds=…" = a wave starting from that seed,
/// found in the app's protocol handler). Without the app, the same path opens on music.yandex.ru.
/// </summary>
public static class YandexMusic
{
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static bool Installed
    {
        get { try { using var k = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey(@"yandexmusic\shell\open\command"); return k != null; } catch { return false; } }
    }

    /// <summary>Path like "/album/1/track/2" → the app if installed, else the website.</summary>
    public static string Link(string path, bool app) =>
        app ? "yandexmusic://" + path.TrimStart('/') : "https://music.yandex.ru/" + path.TrimStart('/');

    public static string WavePath(string? seed = null) =>
        "/?deeplink=play-vibe&openPlayer=true&" + (seed == null ? "stationId=user:onyourwave" : "seeds=" + Uri.EscapeDataString(seed));

    public static void Open(string path) => Shell.Open(Link(path, Installed));

    public static void MyWave() => Open(WavePath());

    public record Found(string Kind, string Seed, string Title);

    /// <summary>Best catalog match for a spoken request: a track, artist, album or playlist.</summary>
    public static async Task<Found?> Search(string query)
    {
        var json = await Http.GetStringAsync($"https://api.music.yandex.net/search?text={Uri.EscapeDataString(query)}&type=all&page=0&nocorrect=false");
        using var doc = JsonDocument.Parse(json);
        return Parse(doc.RootElement.GetProperty("result"));
    }

    internal static Found? Parse(JsonElement result)
    {
        if (!result.TryGetProperty("best", out var best) || best.ValueKind != JsonValueKind.Object) return null;
        var type = best.GetProperty("type").GetString();
        var r = best.GetProperty("result");
        string Id(JsonElement e) => e.GetProperty("id").ToString();
        string Artists(JsonElement e) => e.TryGetProperty("artists", out var a) ? string.Join(", ", a.EnumerateArray().Select(x => x.GetProperty("name").GetString())) : "";
        return type switch
        {
            "track" => new("track", "track:" + Id(r), $"{Artists(r)} — {r.GetProperty("title").GetString()}".Trim(' ', '—')),
            "artist" => new("artist", "artist:" + Id(r), r.GetProperty("name").GetString() ?? ""),
            "album" => new("album", "album:" + Id(r), $"{Artists(r)} — {r.GetProperty("title").GetString()}".Trim(' ', '—')),
            "playlist" => new("playlist", $"playlist:{r.GetProperty("uid")}_{r.GetProperty("kind")}", r.GetProperty("title").GetString() ?? ""),
            _ => null,
        };
    }

    /// <summary>"включи Yanix" → wave seeded by the best match. Returns what Jarvis should say.</summary>
    public static async Task<string> Play(string query)
    {
        Found? f;
        try { f = await Search(query); }
        catch (Exception e) { Log.Error(e); return "Яндекс Музыка не ответила — проверь интернет."; }
        if (f == null) return $"Не нашёл «{query}» в Яндекс Музыке.";
        Open(WavePath(f.Seed));
        return f.Kind switch
        {
            "artist" => $"Включаю {f.Title} в Яндекс Музыке.",
            "track" => $"Включаю волну по треку {f.Title}.",
            _ => $"Включаю {f.Title}.",
        };
    }
}
