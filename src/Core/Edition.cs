using System.IO;

namespace MishkaHelper.Core;

/// <summary>
/// One build with everything; which parts are on is chosen with checkboxes in the installer or in the settings (modules.ini next to the exe).
/// The old compile-time flavours still build (dev, tests): a part that isn't compiled in is never on.
/// </summary>
public static class Edition
{
#if JARVIS
    const bool BuiltJarvis = true;
#else
    const bool BuiltJarvis = false;
#endif
#if SECRETARY
    const bool BuiltSecretary = true;
#else
    const bool BuiltSecretary = false;
#endif
    public static bool HasJarvis => BuiltJarvis && Modules.Jarvis;
    public static bool HasSecretary => BuiltSecretary && Modules.Secretary;
    /// <summary>The game helper builds its base with the online brain, which lives with Jarvis's code.</summary>
    public static bool HasGames => BuiltJarvis && Modules.Games;

    /// <summary>What is on, as the old edition names (support tickets, logs).</summary>
    public static string Name => NameFor(HasJarvis, HasSecretary) + (HasGames ? "+Games" : "");
    public const string Display = "Mishka Helper";

    public static string NameFor(bool jarvis, bool secretary) => jarvis ? (secretary ? "Full" : "Jarvis") : (secretary ? "Secretary" : "Lite");
    public static string PrefixFor(string name) => name == "Full" ? "MishkaHelper-" : "MishkaHelper-" + name + "-";

    /// <summary>Release files of the one build. The old edition names are published too, as copies, so older copies still find their updates.</summary>
    public const string AssetPrefix = "MishkaHelper-";

    public static Version Version
    {
        get { var v = typeof(App).Assembly.GetName().Version!; return new Version(v.Major, v.Minor, v.Build); }
    }
}

/// <summary>
/// modules.ini beside the exe: "jarvis=1" lines, written by the installer's checkboxes and by the settings page.
/// It sits in Program Files for installed copies (admin-only), so nothing else can switch parts on behind the user's back.
/// No file = everything on (portable zips, older installs).
/// </summary>
public static class Modules
{
    public static string File => Path.Combine(AppContext.BaseDirectory, "modules.ini");
    static Dictionary<string, bool>? _on;

    static Dictionary<string, bool> On => _on ??= Parse(System.IO.File.Exists(File) ? Read() : "");
    static string Read() { try { return System.IO.File.ReadAllText(File); } catch { return ""; } }

    internal static Dictionary<string, bool> Parse(string text)
    {
        var d = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n'))
            if (line.Split('=', 2) is [var k, var v]) d[k.Trim()] = v.Trim() is "1" or "true" or "yes";
        return d;
    }

    static bool Get(string key) => !On.TryGetValue(key, out var v) || v;
    public static bool Jarvis => Get("jarvis");
    public static bool Secretary => Get("secretary");
    public static bool Games => Get("games");

    /// <summary>Takes effect after a restart: services and pages are set up once at start.</summary>
    public static void Save(bool jarvis, bool secretary, bool games)
    {
        System.IO.File.WriteAllText(File, $"jarvis={(jarvis ? 1 : 0)}\r\nsecretary={(secretary ? 1 : 0)}\r\ngames={(games ? 1 : 0)}\r\n");
        Log.Info($"modules: jarvis={jarvis} secretary={secretary} games={games}");
    }
}
