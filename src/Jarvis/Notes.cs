using System.IO;
using System.Text.Json;

namespace MishkaHelper.Core;

public record Note(DateTime When, string Text);
public record ChatLine(DateTime When, bool FromUser, string Text);

/// <summary>Jarvis' own notes: "запиши в заметку…", "что я записывал про…". JSON file next to settings.</summary>
public static class Notes
{
    static readonly string FilePath = Path.Combine(Settings.Dir, "jarvis-notes.json");
    static List<Note>? _all;

    public static List<Note> All
    {
        get
        {
            if (_all != null) return _all;
            try { _all = File.Exists(FilePath) ? JsonSerializer.Deserialize<List<Note>>(File.ReadAllText(FilePath)) ?? new() : new(); }
            catch { _all = new(); }
            return _all;
        }
    }

    public static void Add(string text)
    {
        All.Add(new Note(DateTime.Now, text.Trim()));
        Save();
    }

    public static void Remove(Note n) { All.Remove(n); Save(); }

    static void Save()
    {
        Directory.CreateDirectory(Settings.Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(All, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>Keyword search with loose word stems (Russian endings vary: "молоко"/"молока").</summary>
    public static List<Note> Search(string query)
    {
        var words = Ears.Clean(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2).Select(Stem).ToList();
        if (words.Count == 0) return All.OrderByDescending(n => n.When).Take(5).ToList();
        return All.Select(n => (n, score: words.Count(w => Ears.Clean(n.Text).Contains(w))))
            .Where(x => x.score > 0).OrderByDescending(x => x.score).ThenByDescending(x => x.n.When).Select(x => x.n).Take(5).ToList();
    }

    static string Stem(string w) => w.Length > 5 ? w[..^2] : w.Length > 3 ? w[..^1] : w;
}

/// <summary>Conversation log; shown in the chat only on request ("История").</summary>
public static class ChatHistory
{
    static readonly string FilePath = Path.Combine(Settings.Dir, "jarvis-history.jsonl");
    public static readonly List<ChatLine> Session = new();

    public static void Add(bool fromUser, string text)
    {
        var l = new ChatLine(DateTime.Now, fromUser, text);
        Session.Add(l);
        try { Directory.CreateDirectory(Settings.Dir); File.AppendAllText(FilePath, JsonSerializer.Serialize(l) + "\n"); } catch { }
    }

    public static List<ChatLine> Load(int max = 300)
    {
        try
        {
            if (!File.Exists(FilePath)) return new();
            return File.ReadLines(FilePath).TakeLast(max).Select(l => JsonSerializer.Deserialize<ChatLine>(l)).Where(l => l != null).Cast<ChatLine>().ToList();
        }
        catch { return new(); }
    }

    public static void Clear() { Session.Clear(); try { File.Delete(FilePath); } catch { } }
}
