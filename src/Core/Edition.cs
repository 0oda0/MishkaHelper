namespace MishkaHelper.Core;

/// <summary>Which build this is: Lite (tools only), Secretary, Jarvis or Full (everything).</summary>
public static class Edition
{
#if JARVIS
    public const bool HasJarvis = true;
#else
    public const bool HasJarvis = false;
#endif
#if SECRETARY
    public const bool HasSecretary = true;
#else
    public const bool HasSecretary = false;
#endif
    public const string Name = HasJarvis ? (HasSecretary ? "Full" : "Jarvis") : (HasSecretary ? "Secretary" : "Lite");
    public const string Display = Name == "Full" ? "Mishka Helper" : "Mishka Helper " + Name;

    /// <summary>Which edition has exactly these parts. The tools are in every one.</summary>
    public static string NameFor(bool jarvis, bool secretary) => jarvis ? (secretary ? "Full" : "Jarvis") : (secretary ? "Secretary" : "Lite");
    public static string PrefixFor(string name) => name == "Full" ? "MishkaHelper-" : "MishkaHelper-" + name + "-";
    public static string Describe(string name) => name switch
    {
        "Full" => "Полная: инструменты, Джарвис и секретарша", "Jarvis" => "Инструменты и Джарвис", "Secretary" => "Инструменты и секретарша", _ => "Lite: только инструменты",
    };

    /// <summary>Start of this edition's release files. Full and Lite keep their old names: installed copies look for them when updating.</summary>
    public const string AssetPrefix = Name == "Full" ? "MishkaHelper-" : "MishkaHelper-" + Name + "-";

    public static Version Version
    {
        get { var v = typeof(App).Assembly.GetName().Version!; return new Version(v.Major, v.Minor, v.Build); }
    }
}
