using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MishkaHelper.Core;

public class KbEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..10];
    public string Kind { get; set; } = "";       // GameKb.Kinds
    public string Title { get; set; } = "";      // Russian
    public string TitleEn { get; set; } = "";    // as on the source page: found by search too
    public string Text { get; set; } = "";       // Russian; empty until opened for big lists (items)
    public int Stage { get; set; }               // 0 = any time
    public bool Spoiler { get; set; }
    public string Source { get; set; } = "";     // page address
}

public class KbStage
{
    public int N { get; set; }
    public string Name { get; set; } = "";
    public string Summary { get; set; } = "";
}

/// <summary>Everything Mishka knows about one game, in Russian. One JSON file per game in the data folder.</summary>
public class GameBase
{
    public string Key { get; set; } = "";        // "steam-1086940" or the process name
    public string Name { get; set; } = "";
    public List<string> Exe { get; set; } = new();   // process names (without .exe) that mean "this game is in front"
    public int AppId { get; set; }
    public string Wiki { get; set; } = "";       // MediaWiki api.php the base was built from
    public DateTime Built { get; set; }
    public string Status { get; set; } = "";     // "" ready; otherwise what is happening or what went wrong
    public double Done { get; set; }             // 0..1 while building
    public int Progress { get; set; } = 1;       // the stage the player has reached: later stages stay hidden
    public List<KbStage> Stages { get; set; } = new();
    public List<KbEntry> Entries { get; set; } = new();
    public List<string> Sources { get; set; } = new();
}

/// <summary>
/// The game helper: notices which game is in front, builds its base once (GameBuilder), and opens it by hotkey only while that game is in front.
/// </summary>
public static class GameKb
{
    /// <summary>Kinds of entries, in the order the filters show them.</summary>
    public static readonly (string Key, string Name, string Icon)[] Kinds =
    {
        ("about", "Об игре", "\uE946"), ("stage", "Этапы и сюжет", "\uE8A1"), ("location", "Локации", "\uE707"), ("boss", "Боссы", "\uE7EF"),
        ("quest", "Задания", "\uE73E"), ("character", "Персонажи", "\uE77B"), ("item", "Предметы", "\uE7B8"), ("mechanic", "Механики", "\uE90F"),
        ("problem", "Проблемы и решения", "\uE90A"), ("found", "Найдено по запросу", "\uE721"),
    };
    public static string KindName(string k) => Kinds.FirstOrDefault(x => x.Key == k).Name ?? k;

    public static event Action? Changed;
    public static void Fire() => System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Changed?.Invoke());

    static string Dir => Path.Combine(AppPaths.Data, "games");
    static readonly Dictionary<string, GameBase> Cache = new();
    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public static List<GameBase> All()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            foreach (var f in Directory.GetFiles(Dir, "*.json"))
            {
                var key = Path.GetFileNameWithoutExtension(f);
                if (Cache.ContainsKey(key)) continue;
                try { if (JsonSerializer.Deserialize<GameBase>(File.ReadAllText(f)) is { } b) Cache[key] = b; } catch (Exception e) { Log.Error(e); }
            }
        }
        catch (Exception e) { Log.Error(e); }
        return Cache.Values.OrderBy(b => b.Name).ToList();
    }

    public static void Save(GameBase b)
    {
        Directory.CreateDirectory(Dir);
        Cache[b.Key] = b;
        var tmp = Path.Combine(Dir, b.Key + ".tmp");
        lock (b) File.WriteAllText(tmp, JsonSerializer.Serialize(b, Json));
        File.Move(tmp, Path.Combine(Dir, b.Key + ".json"), true);
        Fire();
    }

    public static void Delete(GameBase b)
    {
        Cache.Remove(b.Key);
        try { File.Delete(Path.Combine(Dir, b.Key + ".json")); } catch (Exception e) { Log.Error(e); }
        Fire();
    }

    // ---------------- which game is in front ----------------
    public record Game(string Key, string Name, string Exe, int AppId, string Folder);

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);

    /// <summary>The game in front: one that has a base, or a Steam / Epic / GOG game recognised by where it is installed. Null for anything else.</summary>
    public static Game? Current()
    {
        var hwnd = GetForegroundWindow();
        GetWindowThreadProcessId(hwnd, out var pid);
        string name, path;
        try { using var p = Process.GetProcessById((int)pid); name = p.ProcessName; path = ImagePath(pid); }
        catch { return null; }
        if (pid == Environment.ProcessId) return null;
        if (All().FirstOrDefault(b => b.Exe.Any(e => e.Equals(name, StringComparison.OrdinalIgnoreCase))) is { } known)
            return new(known.Key, known.Name, name, known.AppId, "");
        if (Steam(path) is { } s) return new("steam-" + s.AppId, s.Name, name, s.AppId, s.Folder);
        if (Store(path) is { } folder)
        {
            var sb = new System.Text.StringBuilder(256);
            GetWindowText(hwnd, sb, 256);
            var title = sb.ToString().Trim();
            return new(Key(name), title.Length is > 1 and < 60 ? title : Path.GetFileName(folder), name, 0, folder);
        }
        return null;
    }

    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern bool QueryFullProcessImageName(IntPtr h, int flags, System.Text.StringBuilder s, ref int n);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);

    /// <summary>The exe path with the least rights there are: games behind anti-cheat refuse reading their modules, not this.</summary>
    static string ImagePath(uint pid)
    {
        var h = OpenProcess(0x1000 /* QUERY_LIMITED_INFORMATION */, false, pid);
        if (h == IntPtr.Zero) return "";
        try { var sb = new System.Text.StringBuilder(1024); int n = sb.Capacity; return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : ""; }
        finally { CloseHandle(h); }
    }

    internal static string Key(string exe) => "exe-" + Regex.Replace(exe.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

    /// <summary>A Steam game: its folder under steamapps\common has an appmanifest next to it with the app id and the real name.</summary>
    internal static (int AppId, string Name, string Folder)? Steam(string exePath)
    {
        var m = Regex.Match(exePath, @"^(.*\\steamapps)\\common\\([^\\]+)", RegexOptions.IgnoreCase);
        if (!m.Success) return null;
        try
        {
            foreach (var acf in Directory.GetFiles(m.Groups[1].Value, "appmanifest_*.acf"))
            {
                var t = File.ReadAllText(acf);
                if (AcfValue(t, "installdir")?.Equals(m.Groups[2].Value, StringComparison.OrdinalIgnoreCase) == true && int.TryParse(AcfValue(t, "appid"), out var id))
                    return (id, AcfValue(t, "name") ?? m.Groups[2].Value, Path.Combine(m.Groups[1].Value, "common", m.Groups[2].Value));
            }
        }
        catch { }
        return null;
    }

    internal static string? AcfValue(string acf, string key) =>
        Regex.Match(acf, "\"" + key + "\"\\s+\"([^\"]*)\"", RegexOptions.IgnoreCase) is { Success: true } m ? m.Groups[1].Value : null;

    /// <summary>Epic and GOG keep games in their own folders; anything there is taken for a game.</summary>
    static string? Store(string exePath)
    {
        var m = Regex.Match(exePath, @"^(.*\\(Epic Games|GOG Galaxy\\Games|GOG Games)\\[^\\]+)", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups[1].Value : null;
    }

    // ---------------- hotkey and offers ----------------
    static readonly HashSet<string> Offered = new();

    public static void Start() { }

    /// <summary>Every 5 s: a game without a base comes to the front → offer to build one, once per game per run.</summary>
    public static void Check()
    {
        if (!Settings.Current.GameOffers) return;
        // Steam also ships tools (Wallpaper Engine…): a game is offered once it's actually played full-screen.
        if (!Focus.On && !Focus.FullScreenNow()) return;
        var g = Current();
        if (g == null || All().Any(b => b.Key == g.Key) || Settings.Current.GamesDeclined.Contains(g.Key) || !Offered.Add(g.Key)) return;
        Toast.Show("\uE7FC", "Игровой помощник", $"Собрать базу знаний по «{g.Name}» на русском? Сюжет по этапам, локации, боссы, предметы, проблемы. Займёт 5–15 минут, играть можно.",
            null, new (string, Action)[]
            {
                ("Собрать", () => _ = GameBuilder.Build(g)),
                ("Не для этой игры", () => { Settings.Current.GamesDeclined.Add(g.Key); Settings.Save(); }),
            });
    }

    /// <summary>The hotkey: this game's base, if it has one. Nothing opens over other programs.</summary>
    public static void Toggle()
    {
        if (GameKbWindow.Shown) { GameKbWindow.Close(); return; }
        var fg = GetForegroundWindow();
        var g = Current();
        if (g == null) { Osd.Show("\uE7FC", "Это не игра. Добавить игру можно на странице «Игры»"); return; }
        var b = All().FirstOrDefault(x => x.Key == g.Key);
        if (b == null)
        {
            Toast.Show("\uE7FC", g.Name, "Базы по этой игре ещё нет. Собрать?", null, new (string, Action)[] { ("Собрать", () => _ = GameBuilder.Build(g)) });
            return;
        }
        GameKbWindow.Open(b, fg);
    }

    /// <summary>Adds the program in front (or any process) as a game, for ones not found by their folder.</summary>
    public static Game Manual(string exe, string name) => new(Key(exe), name, exe, 0, "");
}
