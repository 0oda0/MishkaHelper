using System.IO;

namespace MishkaHelper.Core;

/// <summary>"Что можно сделать": the bundled HTML landing page with every feature and screenshots.</summary>
public static class Features
{
    public static void Open()
    {
        var page = Path.Combine(AppContext.BaseDirectory, "docs", "index.html");
        if (File.Exists(page)) Shell.Open(page);
        else Shell.Open("https://github.com/0oda0/MishkaHelper#readme");
    }
}
