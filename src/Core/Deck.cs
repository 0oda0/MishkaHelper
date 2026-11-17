using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows.Media.Imaging;

namespace MishkaHelper.Core;

/// <summary>One button of the phone deck.</summary>
public class DeckKey
{
    public int Slot { get; set; }               // row * Cols + column
    public string Label { get; set; } = "";
    public string Emoji { get; set; } = "";     // the phone has no Segoe icons: an emoji (or a program's own icon for "open")
    public string Color { get; set; } = "";     // "#RRGGBB" or "" = the theme's
    public string Kind { get; set; } = "act";   // act (any Mishka action key) | keys ("ctrl+shift+m", "alt w f f") | text | open (program, file, folder, site) | page (go to a deck page)
    public string Value { get; set; } = "";
    public string Live { get; set; } = "";      // shown on the key: cpu, gpu, cpuTemp, gpuTemp, ram, fps, vol, time, net
    public string Style { get; set; } = "";     // how the live number looks: "" number, bar, graph (last minute), ring
    public string Swipe { get; set; } = "";     // a sideways swipe on the key: vol (±5), bright (±10); a volume key swipes by itself
    public string Img { get; set; } = "";       // a picture instead of the emoji: base64 PNG/JPEG, made small (128 px) where it was picked
    public int W { get; set; } = 1;             // cells wide and high: a graph reads better on 2 × 1 or 4 × 2
    public int H { get; set; } = 1;
}

public class DeckPage
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "";
    /// <summary>Process names (without .exe) that bring this page up by themselves; "game" = any game in game mode.</summary>
    public List<string> Processes { get; set; } = new();
    public List<DeckKey> Keys { get; set; } = new();
}

public class DeckSettings
{
    public int Cols { get; set; } = 4;
    public int Rows { get; set; } = 3;
    public bool AutoPage { get; set; } = true;
    public List<DeckPage> Pages { get; set; } = new();
}

/// <summary>
/// The phone as a stream deck: pages of big buttons in the Mishka app, any Mishka action, keys, text, programs, live numbers.
/// The page follows the program in front (Photoshop → its keys, a game → the game page). Edited here (page «Стримдек») or on the phone.
/// Talks over the phone app's WebSocket: deckOn/deckOff, press, save; → deck, live, page.
/// </summary>
public static class Deck
{
    static DeckSettings D => Settings.Current.Deck;

    // ---------------- presets ----------------
    static DeckKey A(int slot, string label, string emoji, string action, string live = "") => new() { Slot = slot, Label = label, Emoji = emoji, Kind = "act", Value = action, Live = live };

    /// <summary>Main page, game page, and one page per work program from the wheel sets (same keys).</summary>
    public static DeckSettings Default()
    {
        var d = new DeckSettings();
        d.Pages.Add(new DeckPage
        {
            Id = "main", Name = "Главная", Keys =
            {
                A(0, "Звук", "🔇", "mute", "vol"), A(1, "Тише", "🔉", "volDown"), A(2, "Громче", "🔊", "volUp"), A(3, "Микрофон", "🎙️", "micMute"),
                new() { Slot = 4, Label = "Назад", Emoji = "⏮️", Kind = "keys", Value = "prev" }, new() { Slot = 5, Label = "Пауза", Emoji = "⏯️", Kind = "keys", Value = "play" },
                new() { Slot = 6, Label = "Вперёд", Emoji = "⏭️", Kind = "keys", Value = "next" }, A(7, "Снимок", "📸", "screenshot"),
                new() { Slot = 8, Label = "CPU", Emoji = "🧠", Kind = "act", Value = "taskmgr", Live = "cpu", Style = "graph" }, new() { Slot = 9, Label = "GPU", Emoji = "🎮", Kind = "act", Value = "focus", Live = "gpu", Style = "bar" }, A(10, "Блокировка", "🔒", "lock"), A(11, "Ночной свет", "🌙", "night"),
            },
        });
        d.Pages.Add(new DeckPage
        {
            Id = "game", Name = "Игра", Processes = { "game" }, Keys =
            {
                A(0, "FPS", "⏱️", "overlay", "fps"), A(1, "CPU", "🌡️", "taskmgr", "cpuTemp"), A(2, "GPU", "🔥", "fanMax", "gpuTemp"), A(3, "Микрофон", "🎙️", "micMute"),
                A(4, "Запись 30 с", "🎬", "replay"), A(5, "Снимок", "📸", "screenshot"), A(6, "Вентиляторы", "🌀", "fanMax"), A(7, "Звук", "🔇", "mute", "vol"),
                A(8, "Тише", "🔉", "volDown"), A(9, "Громче", "🔊", "volUp"), A(10, "База игры", "📖", "gameKb"), A(11, "Игровой режим", "🎯", "focus"),
            },
        });
        foreach (var w in AppWheels.Presets())
            d.Pages.Add(new DeckPage
            {
                Id = w.Name.ToLowerInvariant(), Name = w.Name, Processes = w.Processes.ToList(),
                Keys = w.Items.Take(d.Cols * d.Rows).Select((k, i) => new DeckKey { Slot = i, Label = k.Label, Kind = "keys", Value = k.Keys }).ToList(),
            });
        return d;
    }

    // ---------------- presses ----------------
    /// <summary>A key pressed on the phone. Keys and text go to whatever is in front on the PC: the phone never takes the focus.</summary>
    public static void Press(DeckKey k)
    {
        Log.Info($"deck press: {k.Kind} {k.Value}");
        switch (k.Kind)
        {
            case "act": System.Windows.Application.Current.Dispatcher.BeginInvoke(() => CommandPalette.RunAction(k.Value)); break;
            case "keys":
                _ = Task.Run(async () => { foreach (var step in AppWheels.Steps(k.Value)) { InputSim.Key(step); await Task.Delay(60); } });
                break;
            case "text": InputSim.Type(k.Value.Length > 2000 ? k.Value[..2000] : k.Value); break;
            case "open": if (k.Value.Trim().Length > 0) Shell.Open(Environment.ExpandEnvironmentVariables(k.Value.Trim())); break;   // through Explorer: never elevated
        }
    }

    /// <summary>What a sideways swipe on this key changes: its own setting, or volume for a key that already is about sound.</summary>
    internal static string SwipeOf(DeckKey k) => k.Swipe.Length > 0 ? k.Swipe : k.Live == "vol" || k.Value is "mute" or "volUp" or "volDown" ? "vol" : "";

    static void Swipe(DeckKey k, int steps)
    {
        switch (SwipeOf(k))
        {
            case "vol": Audio.Volume = Math.Clamp(Audio.Volume + 5 * steps, 0, 100); Osd.Show("", "Громкость", Audio.Volume); break;
            case "bright": Display.Brightness = Math.Clamp(Display.Brightness + 10 * steps, 0, 100); Osd.Show("", "Яркость", Display.Brightness); break;
        }
    }

    public static readonly (string Key, string Name)[] Swipes = { ("", "Нет"), ("vol", "Громкость ±5"), ("bright", "Яркость ±10") };

    /// <summary>A picture from the phone or the PC editor, kept only if it is a small PNG or JPEG.</summary>
    internal static string SafeImg(string? b64)
    {
        if (string.IsNullOrEmpty(b64) || b64.Length > 140_000) return "";
        try
        {
            var b = Convert.FromBase64String(b64);
            bool png = b.Length > 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47;
            bool jpg = b.Length > 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF;
            return png || jpg ? b64 : "";
        }
        catch { return ""; }
    }

    // ---------------- live numbers ----------------
    public static readonly (string Key, string Name)[] Lives =
    {
        ("", "Нет"), ("cpu", "Загрузка CPU"), ("gpu", "Загрузка GPU"), ("cpuTemp", "Температура CPU"), ("gpuTemp", "Температура GPU"), ("ram", "Память"),
        ("fps", "FPS в игре"), ("vol", "Громкость"), ("net", "Сеть ↓"), ("time", "Время"),
    };

    internal static string LiveText(string key)
    {
        var s = SysMon.Last;
        return key switch
        {
            "cpu" => $"{s.Cpu:0}%", "gpu" => $"{s.Gpu:0}%", "cpuTemp" => s.CpuTemp > 0 ? $"{s.CpuTemp}°" : "—", "gpuTemp" => s.GpuTemp > 0 ? $"{s.GpuTemp}°" : "—",
            "ram" => $"{s.Ram:0}%", "fps" => GameOverlay.LastFps > 0 ? $"{GameOverlay.LastFps}" : "—", "vol" => Audio.Muted ? "выкл" : $"{Audio.Volume}%",
            "net" => s.NetDown >= 1024 * 1024 ? $"{s.NetDown / 1048576:0.0} МБ/с" : $"{s.NetDown / 1024:0} КБ/с", "time" => DateTime.Now.ToString("HH:mm"),
            _ => "",
        };
    }

    /// <summary>The same live value as a number, for bars, rings and graphs; Max is the full scale (0 = the graph scales itself).</summary>
    internal static (double Value, double Max)? LiveNumber(string key)
    {
        var s = SysMon.Last;
        return key switch
        {
            "cpu" => (s.Cpu, 100), "gpu" => (s.Gpu, 100), "ram" => (s.Ram, 100), "vol" => (Audio.Muted ? 0 : Audio.Volume, 100),
            "cpuTemp" => (s.CpuTemp, 100), "gpuTemp" => (s.GpuTemp, 100), "fps" => (GameOverlay.LastFps, 0), "net" => (s.NetDown / 1024, 0),
            _ => null,
        };
    }

    public static readonly (string Key, string Name)[] Styles = { ("", "Цифра"), ("bar", "Столбец"), ("graph", "График за минуту"), ("ring", "Кольцо") };

    /// <summary>On/off switches show their state: the mic key is red while the mic is off.</summary>
    internal static bool? State(string action) => action switch
    {
        "micMute" => Audio.MicMuted, "mute" => Audio.Muted, "focus" => Focus.On, "dnd" => Tweaks.DoNotDisturb, "night" => Display.NightLight,
        "keepAwake" => Toolbox.KeepAwake, "overlay" => GameOverlay.Shown, _ => null,
    };

    // ---------------- which page ----------------
    /// <summary>The page for what's in front: a program's own page, the game page in game mode, else none (the phone keeps its own).</summary>
    internal static string? PageFor(DeckSettings d, string process, bool game) =>
        d.Pages.FirstOrDefault(p => p.Processes.Any(x => x.Equals(process, StringComparison.OrdinalIgnoreCase)))?.Id
        ?? (game ? d.Pages.FirstOrDefault(p => p.Processes.Contains("game"))?.Id : null);

    // ---------------- the phone link ----------------
    static System.Threading.Timer? _timer;
    static IDisposable? _live;
    static string _lastFg = "", _lastPage = "", _lastLive = "";

    /// <summary>The deck is on the phone's screen: live numbers every second, page follows the program in front.</summary>
    public static void On()
    {
        if (_timer != null) return;
        _live = SysMon.Live();
        _lastFg = _lastPage = _lastLive = "";
        _timer = new System.Threading.Timer(_ => Tick(), null, 0, 1000);
        _ = SendDeck();
    }

    public static void Off()
    {
        _timer?.Dispose(); _timer = null;
        _live?.Dispose(); _live = null;
    }

    static void Tick()
    {
        try
        {
            if (!Phone.Connected) { Off(); return; }
            var d = D;
            if (d.AutoPage)
            {
                var (_, fg) = Focus.Foreground();
                var key = fg + "|" + Focus.On;
                // Only on a change: the user may have picked another page by hand, and that holds until the program changes.
                if (key != _lastFg && fg != "MishkaHelper")
                {
                    _lastFg = key;
                    if (PageFor(d, fg, Focus.On) is { } page && page != _lastPage) { _lastPage = page; _ = Phone.Send(new { t = "deckPage", id = page }); }
                }
            }
            var live = new JsonObject();
            foreach (var k in d.Pages.SelectMany(p => p.Keys))
            {
                if (k.Live.Length > 0)
                {
                    live["l:" + k.Live] = LiveText(k.Live);
                    if (LiveNumber(k.Live) is { } n) { live["n:" + k.Live] = Math.Round(n.Value, 1); live["m:" + k.Live] = n.Max; }
                }
                if (k.Kind == "act" && State(k.Value) is { } on) live["s:" + k.Value] = on;
            }
            var json = live.ToJsonString();
            if (json != _lastLive) { _lastLive = json; _ = Phone.Send(new { t = "deckLive", v = live }); }
        }
        catch (Exception e) { Log.Error(e); }
    }

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>The whole deck, plus what the phone's editor offers: every action with its name, and program icons for "open" keys.</summary>
    public static Task<bool> SendDeck()
    {
        var d = D;
        var icons = new Dictionary<string, string>();
        foreach (var k in d.Pages.SelectMany(p => p.Keys).Where(k => k.Kind == "open" && k.Emoji.Length == 0))
            if (!icons.ContainsKey(k.Value) && IconPng(k.Value) is { } png) icons[k.Value] = png;
        var actions = CommandPalette.Commands().Select(c => new { a = c.Action, n = c.Name }).ToList();
        return Phone.Send(new { t = "deck", deck = JsonSerializer.SerializeToNode(d, Json), icons, actions, lives = Lives.Select(l => new { k = l.Key, n = l.Name }),
            styles = Styles.Select(l => new { k = l.Key, n = l.Name }), swipes = Swipes.Select(l => new { k = l.Key, n = l.Name }) });
    }

    /// <summary>A program's own icon as base64 PNG (32 px), for the phone.</summary>
    static string? IconPng(string path)
    {
        try
        {
            string? b64 = null;
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (AppIcon.For(new Launcher { Path = path }) is not BitmapSource img) return;
                var enc = new PngBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create(img));
                using var ms = new MemoryStream();
                enc.Save(ms);
                b64 = Convert.ToBase64String(ms.ToArray());
            });
            return b64;
        }
        catch { return null; }
    }

    /// <summary>A message from the phone.</summary>
    public static void Handle(JsonObject m)
    {
        switch (m["t"]?.ToString())
        {
            case "deckOn": On(); break;
            case "deckOff": Off(); break;
            case "deckPress":
                var page = D.Pages.FirstOrDefault(p => p.Id == m["page"]?.ToString());
                if (page?.Keys.FirstOrDefault(k => k.Slot == (int?)m["slot"]) is { } key) Press(key);
                break;
            case "deckSwipe":   // n steps, left negative
                var sp = D.Pages.FirstOrDefault(p => p.Id == m["page"]?.ToString());
                if (sp?.Keys.FirstOrDefault(k => k.Slot == (int?)m["slot"]) is { } sk && int.TryParse(m["n"]?.ToString(), out var steps))
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Swipe(sk, Math.Clamp(steps, -20, 20)));
                break;
            case "deckSave":   // the phone's editor: the whole deck back, checked before it replaces ours
                if (Parse(m["deck"]) is { } d) { Settings.Current.Deck = d; Settings.Save(); Changed?.Invoke(); _ = SendDeck(); }
                break;
        }
    }

    public static event Action? Changed;

    /// <summary>Called after the PC's own editor saved: the phone gets it at once.</summary>
    public static void Saved() { Settings.Save(); if (_timer != null) _ = SendDeck(); }

    /// <summary>A deck from the phone, made safe: sizes in range, known kinds only, no oversized texts.</summary>
    internal static DeckSettings? Parse(JsonNode? n)
    {
        DeckSettings? d;
        try { d = n?.Deserialize<DeckSettings>(Json); } catch { return null; }
        if (d == null || d.Pages.Count is 0 or > 30) return null;
        d.Cols = Math.Clamp(d.Cols, 2, 8); d.Rows = Math.Clamp(d.Rows, 2, 8);
        foreach (var p in d.Pages)
        {
            p.Name = Cut(p.Name, 40); p.Processes = p.Processes.Select(x => Cut(x.Trim(), 60)).Where(x => x.Length > 0).Take(10).ToList();
            p.Keys = p.Keys.Where(k => k.Slot >= 0 && k.Slot < MaxSlots && k.Kind is "act" or "keys" or "text" or "open" or "page")
                .GroupBy(k => k.Slot).Select(g => g.Last()).ToList();
            foreach (var k in p.Keys) { k.Label = Cut(k.Label, 40); k.Emoji = Cut(k.Emoji, 16); k.Value = Cut(k.Value, 2000); k.Color = System.Text.RegularExpressions.Regex.IsMatch(k.Color, "^#[0-9a-fA-F]{6}$") ? k.Color : ""; k.Live = Lives.Any(l => l.Key == k.Live) ? k.Live : ""; k.Style = Styles.Any(x => x.Key == k.Style) ? k.Style : ""; k.W = Math.Clamp(k.W, 1, 8); k.H = Math.Clamp(k.H, 1, 8);
                k.Swipe = Swipes.Any(x => x.Key == k.Swipe) ? k.Swipe : ""; k.Img = SafeImg(k.Img); }
        }
        return d;
    }

    /// <summary>Each device picks its own grid (a tablet shows more keys than the phone): keys are kept up to the biggest one, 8 × 8.</summary>
    public const int MaxSlots = 64;

    static string Cut(string? s, int max) => s == null ? "" : s.Length > max ? s[..max] : s;
}
