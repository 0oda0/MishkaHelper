namespace MishkaHelper.Core;

/// <summary>Which build this is. Lite = everything except Jarvis (smaller download, no voice/AI).</summary>
public static class Edition
{
#if LITE
    public const bool HasJarvis = false;
    public const string Name = "Lite";
    public const string Display = "Mishka Helper Lite";
#else
    public const bool HasJarvis = true;
    public const string Name = "Full";
    public const string Display = "Mishka Helper";
#endif

    public static Version Version
    {
        get { var v = typeof(App).Assembly.GetName().Version!; return new Version(v.Major, v.Minor, v.Build); }
    }
}
