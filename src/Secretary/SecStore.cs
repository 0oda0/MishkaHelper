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

public class WorkSetOptions { public string Profile { get; set; } = ""; public bool Dnd { get; set; } }
public class DownloadRule { public string Word { get; set; } = ""; public string Folder { get; set; } = ""; }
public class FileRecord { public string Name { get; set; } = ""; public string Path { get; set; } = ""; public string Source { get; set; } = ""; public DateTime When { get; set; } = DateTime.Now; }

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
    public List<InboxItem> Inbox { get; set; } = new();
    public Dictionary<string, List<string>> HabitLog { get; set; } = new();
    public HashSet<string> HabitAsked { get; set; } = new();
    public DateTime ScheduleTriedAt { get; set; }
    public string ScheduleProblem { get; set; } = "";
    public List<SecTask> Tasks { get; set; } = new();
    public List<AppTrigger> Triggers { get; set; } = new();
    public Dictionary<string, WorkSetOptions> SetOptions { get; set; } = new();
    public List<DownloadRule> DownloadRules { get; set; } = new();
    public List<FileRecord> FileLog { get; set; } = new();
    public string ScheduleCacheDay { get; set; } = "";
    public List<string> ScheduleCache { get; set; } = new();
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

    /// <summary>After a backup was restored: the cached copy must not overwrite it later.</summary>
    public static void Reload() => _cur = Load();

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
