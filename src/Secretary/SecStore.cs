using System.IO;
using System.Text.Json;

namespace MishkaHelper.Core;

public class RememberedItem
{
    public string Title { get; set; } = "";
    public string Target { get; set; } = "";       // URL, or a window title if there is no address
    public string App { get; set; } = "";
    public bool Daily { get; set; }                // shown in the morning and evening summaries (a schedule)
    public DateTime When { get; set; } = DateTime.Now;
}

public class SavedWindow { public string Kind { get; set; } = "app"; public string Target { get; set; } = ""; }   // app | folder | url

public class MailAccount
{
    public string Email { get; set; } = "";
    public string Host { get; set; } = "";
    public int Port { get; set; } = 993;
    public string Secret { get; set; } = "";       // app password, DPAPI-encrypted for this Windows user
    public uint LastUid { get; set; }
    public string Web { get; set; } = "";
}

public class Suggestion { public string Text { get; set; } = ""; public string Source { get; set; } = ""; public DateTime When { get; set; } = DateTime.Now; }

/// <summary>Everything the secretary remembers, in one JSON file next to the settings. Never leaves this PC.</summary>
public class SecStore
{
    public Dictionary<string, string> AppNotes { get; set; } = new();     // app or site key → note
    public List<RememberedItem> Remembered { get; set; } = new();
    public List<SavedWindow> LastWorkspace { get; set; } = new();
    public DateTime LastWorkspaceAt { get; set; }
    public Dictionary<string, List<SavedWindow>> Workspaces { get; set; } = new();
    public List<MailAccount> Mail { get; set; } = new();
    public List<Suggestion> Suggestions { get; set; } = new();
    public DateTime LastMorning { get; set; }
    public DateTime LastEvening { get; set; }

    static readonly string FilePath = Path.Combine(Settings.Dir, "secretary.json");
    static SecStore? _cur;
    public static SecStore Cur => _cur ??= Load();

    static SecStore Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<SecStore>(File.ReadAllText(FilePath)) ?? new(); }
        catch (Exception e) { Log.Error(e); }
        return new();
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            var tmp = FilePath + ".tmp";   // a crash mid-write must not lose the notes
            File.WriteAllText(tmp, JsonSerializer.Serialize(Cur, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, FilePath, true);
        }
        catch (Exception e) { Log.Error(e); }
    }
}
