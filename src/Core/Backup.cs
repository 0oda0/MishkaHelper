using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace MishkaHelper.Core;

/// <summary>
/// Backup of everything the user has written, as one zip: settings, secretary, notes, pinned clipboard items.
/// Restoring takes only these known names, so a crafted archive can't write anywhere else. Encrypted passwords
/// (mail, support chat) open only under the same Windows account; on another PC they have to be entered again.
/// </summary>
public static class Backup
{
    internal static readonly string[] Files = { "settings.json", "secretary.json", "jarvis-notes.json", "clipboard-pinned.json" };
    const long MaxEntry = 64L << 20;

    public static void Make(string dir, string zipPath)
    {
        var tmp = zipPath + ".tmp";
        File.Delete(tmp);
        using (var z = ZipFile.Open(tmp, ZipArchiveMode.Create))
            foreach (var f in Files)
                if (File.Exists(Path.Combine(dir, f))) z.CreateEntryFromFile(Path.Combine(dir, f), f);
        File.Move(tmp, zipPath, true);
    }

    /// <summary>Checks everything first and changes nothing if the file is bad. Older backups were a bare settings.json. Returns how many files came back.</summary>
    public static int Restore(string path, string dir)
    {
        var found = new Dictionary<string, byte[]>();
        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) found["settings.json"] = File.ReadAllBytes(path);
        else
        {
            using var z = ZipFile.OpenRead(path);
            foreach (var e in z.Entries)
            {
                if (!Files.Contains(e.FullName)) continue;   // exact names only: "..\x" never matches
                if (e.Length > MaxEntry) throw new InvalidDataException("Файл в архиве слишком большой");
                using var s = e.Open();
                using var m = new MemoryStream();
                s.CopyTo(m);
                found[e.FullName] = m.ToArray();
            }
        }
        if (!found.ContainsKey("settings.json")) throw new InvalidDataException("Это не резервная копия Mishka Helper");
        JsonSerializer.Deserialize<AppSettings>(found["settings.json"]);
        foreach (var (name, data) in found.Where(f => f.Key != "settings.json"))
            using (JsonDocument.Parse(data)) { }
        Directory.CreateDirectory(dir);
        foreach (var (name, data) in found) File.WriteAllBytes(Path.Combine(dir, name), data);
        return found.Count;
    }
}
