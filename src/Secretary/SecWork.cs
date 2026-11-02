using System.Diagnostics;
using System.IO;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;

namespace MishkaHelper.Core;

/// <summary>"Где я остановился": the open programs, folders and browser pages, saved and brought back with one click.</summary>
public static class SecWorkspace
{
    static readonly HashSet<string> Skip = new(StringComparer.OrdinalIgnoreCase)
        { "MishkaHelper", "explorer", "ApplicationFrameHost", "SystemSettings", "TextInputHost", "ShellExperienceHost", "SearchHost", "StartMenuExperienceHost", "LockApp" };

    public static Task<List<SavedWindow>> Snapshot() => Task.Run(() =>
    {
        var res = new List<SavedWindow>();
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero || p.MainWindowTitle.Length == 0 || Skip.Contains(p.ProcessName)) continue;
                    var url = SecCapture.BrowserUrl(p.MainWindowHandle, p.ProcessName);
                    if (url != null) { res.Add(new SavedWindow { Kind = "url", Target = url }); continue; }
                    var path = p.MainModule?.FileName;
                    if (path != null && res.All(x => x.Target != path)) res.Add(new SavedWindow { Kind = "app", Target = path });
                }
                catch { }   // protected system process
            }
        }
        res.AddRange(ExplorerFolders().Select(f => new SavedWindow { Kind = "folder", Target = f }));
        return res;
    });

    static List<string> ExplorerFolders()
    {
        var l = new List<string>();
        var t = new Thread(() =>
        {
            try
            {
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!)!;
                foreach (var w in shell.Windows())
                {
                    string url = w.LocationURL;
                    if (url.StartsWith("file:///")) l.Add(new Uri(url).LocalPath);
                }
            }
            catch (Exception e) { Log.Error(e); }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join(5000);
        return l;
    }

    public static async Task AutoSave()
    {
        var snap = await Snapshot();
        if (snap.Count == 0) return;
        SecStore.Cur.LastWorkspace = snap; SecStore.Cur.LastWorkspaceAt = DateTime.Now;
        SecStore.Save();
    }

    public static async Task SaveNamed(string name)
    {
        SecStore.Cur.Workspaces[name] = await Snapshot();
        SecStore.Save();
        Secretary.Apply();
    }

    /// <summary>A named set: its windows plus a profile and "do not disturb". One press before a lecture or a shift.</summary>
    public static void OpenSet(string name)
    {
        if (!SecStore.Cur.Workspaces.TryGetValue(name, out var items)) return;
        if (SecStore.Cur.SetOptions.TryGetValue(name, out var o))
        {
            if (o.Profile.Length > 0) Profiles.Apply(o.Profile);
            if (o.Dnd) Tweaks.DoNotDisturb = true;
        }
        Restore(items);
    }

    /// <summary>Everything opens unelevated through Explorer; programs already running are left alone.</summary>
    public static void Restore(List<SavedWindow> items)
    {
        var running = Process.GetProcesses().Select(p => { using (p) { try { return p.MainModule?.FileName; } catch { return null; } } }).Where(x => x != null).ToHashSet(StringComparer.OrdinalIgnoreCase);
        int opened = 0;
        foreach (var i in items)
        {
            try
            {
                if (i.Kind == "app" && (running.Contains(i.Target) || !File.Exists(i.Target))) continue;
                if (i.Kind == "folder" && !Directory.Exists(i.Target)) continue;
                if (i.Kind == "url" && !(Uri.TryCreate(i.Target, UriKind.Absolute, out var u) && u.Scheme is "http" or "https")) continue;
                Shell.Open(i.Target);
                opened++;
            }
            catch (Exception e) { Log.Error(e); }
        }
        Osd.Show("", $"Открыто: {opened}");
    }
}

/// <summary>Sorting the Downloads folder: only proposes. A file moves only when the user presses the button.</summary>
public static class SecDownloads
{
    public record Item(string Path, string Name, string Category, string Dest);

    static readonly string Dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    static FileSystemWatcher? _fw;
    static readonly System.Text.RegularExpressions.Regex Important = new(@"бил[её]т|ticket|boarding|посадочн|invoice|сч[её]т|квитанц|договор|contract|расписан|schedule", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    internal static (string Category, string Folder)? Sort(string name)
    {
        switch (System.IO.Path.GetExtension(name).ToLowerInvariant())
        {
            case ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".txt" or ".rtf" or ".odt" or ".csv": return ("Документы", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));
            case ".jpg" or ".jpeg" or ".png" or ".gif" or ".webp" or ".bmp" or ".heic" or ".svg": return ("Картинки", Environment.GetFolderPath(Environment.SpecialFolder.MyPictures));
            case ".mp3" or ".flac" or ".wav" or ".m4a" or ".ogg": return ("Музыка", Environment.GetFolderPath(Environment.SpecialFolder.MyMusic));
            case ".mp4" or ".mkv" or ".avi" or ".mov" or ".webm": return ("Видео", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos));
            case ".exe" or ".msi": return ("Установщики", System.IO.Path.Combine(Dir, "Установщики"));
            case ".zip" or ".rar" or ".7z" or ".tar" or ".gz": return ("Архивы", System.IO.Path.Combine(Dir, "Архивы"));
            default: return null;   // unfinished downloads and unknown types stay where they are
        }
    }

    public static void Apply(bool on)
    {
        _fw?.Dispose(); _fw = null;
        if (!on || !Directory.Exists(Dir)) return;
        _fw = new FileSystemWatcher(Dir) { NotifyFilter = NotifyFilters.FileName, EnableRaisingEvents = true };
        // Browsers create "x.pdf.crdownload" and rename it at the end, so Renamed is the moment a download finishes.
        _fw.Renamed += (_, e) => Fresh(e.Name);
        _fw.Created += (_, e) => Fresh(e.Name);
    }

    /// <summary>The user's own rule for this name, if any. Rules are the only thing that moves a file without a button.</summary>
    internal static DownloadRule? RuleFor(string name, IEnumerable<DownloadRule> rules) =>
        rules.FirstOrDefault(r => r.Word.Length > 0 && name.Contains(r.Word, StringComparison.OrdinalIgnoreCase));

    /// <summary>Only real folders inside the user's profile: this process is elevated, a rule must not be able to move files elsewhere.</summary>
    internal static bool FolderAllowed(string folder, string profile)
    {
        try
        {
            var full = System.IO.Path.GetFullPath(folder);
            if (!full.StartsWith(profile.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase)) return false;
            var d = new DirectoryInfo(full);
            return !(d.Exists && d.Attributes.HasFlag(FileAttributes.ReparsePoint));
        }
        catch { return false; }
    }

    static readonly string Profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    public static bool AddRule(string word, string folder)
    {
        if (word.Trim().Length < 2 || !FolderAllowed(folder, Profile)) return false;
        SecStore.Cur.DownloadRules.Add(new DownloadRule { Word = word.Trim(), Folder = System.IO.Path.GetFullPath(folder) });
        SecStore.Save();
        return true;
    }

    // A browser may still be writing: wait until the file can be opened alone (up to 10 minutes).
    static async void ApplyRule(string name, DownloadRule rule)
    {
        var path = System.IO.Path.Combine(Dir, name);
        for (int i = 0; i < 120; i++)
        {
            await Task.Delay(5000);
            if (!File.Exists(path)) return;
            try { using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None)) break; }
            catch (IOException) { if (i == 119) return; }
        }
        try
        {
            if (!FolderAllowed(rule.Folder, Profile)) return;
            var to = Move(new Item(path, name, "", rule.Folder));
            Secretary.Notify("\uE8DE", "Файл разложен", $"{name} → {rule.Folder}", () => Shell.Open(Shell.Sys("explorer"), $"/select,\"{to}\""));
        }
        catch (Exception e) { Log.Error(e); }
    }

    static readonly HashSet<string> Seen = new();

    static void Fresh(string? name)
    {
        if (name == null || Sort(name) == null) return;
        var app = System.Windows.Application.Current;
        if (Settings.Current.Secretary.DownloadRules && RuleFor(name, SecStore.Cur.DownloadRules) is { } rule)
        {
            lock (Seen) if (!Seen.Add(name)) return;
            app?.Dispatcher.BeginInvoke(() => ApplyRule(name, rule));
            return;
        }
        if (!Important.IsMatch(name)) return;
        System.Windows.Application.Current?.Dispatcher.BeginInvoke(() =>
            SecInbox.Add(new InboxItem { Kind = "file", Title = "Важный файл в «Загрузках»", Text = name, Ref = System.IO.Path.Combine(Dir, name) }));
    }

    public static List<Item> Pending()
    {
        if (!Directory.Exists(Dir)) return new();
        var res = new List<Item>();
        foreach (var f in new DirectoryInfo(Dir).EnumerateFiles().OrderByDescending(f => f.LastWriteTime).Take(300))
        {
            // Links are skipped: this process is elevated and must only ever move plain files of the user's.
            if (f.Attributes.HasFlag(FileAttributes.ReparsePoint) || f.Attributes.HasFlag(FileAttributes.Hidden)) continue;
            if (Sort(f.Name) is { } s) res.Add(new(f.FullName, f.Name, s.Category, s.Folder));
        }
        return res;
    }

    /// <summary>New in the last week: years of old downloads shouldn't nag every morning. The page still lists everything.</summary>
    public static int RecentCount(int days = 7) => Pending().Count(i => { try { return File.GetLastWriteTime(i.Path) >= DateTime.Now.AddDays(-days); } catch { return false; } });

    public static string Move(Item i)
    {
        var from = new FileInfo(i.Path);
        if (!from.Exists || from.Attributes.HasFlag(FileAttributes.ReparsePoint)) throw new IOException("Файла уже нет");
        Directory.CreateDirectory(i.Dest);
        var to = System.IO.Path.Combine(i.Dest, i.Name);
        for (int n = 2; File.Exists(to); n++) to = System.IO.Path.Combine(i.Dest, $"{System.IO.Path.GetFileNameWithoutExtension(i.Name)} ({n}){System.IO.Path.GetExtension(i.Name)}");
        var source = Source(i.Path);
        from.MoveTo(to);
        var log = SecStore.Cur.FileLog;
        log.Insert(0, new FileRecord { Name = i.Name, Path = to, Source = source });
        if (log.Count > 500) log.RemoveRange(500, log.Count - 500);
        SecStore.Save();
        return to;
    }

    /// <summary>Windows notes where a download came from in the file's Zone.Identifier stream.</summary>
    internal static string Source(string path)
    {
        try
        {
            foreach (var l in File.ReadLines(path + ":Zone.Identifier"))
                if (l.StartsWith("HostUrl=", StringComparison.OrdinalIgnoreCase)) return l[8..].Trim();
        }
        catch { }
        return "";
    }

    /// <summary>"Куда я сохранил договор?": names in the usual folders, plus everything the secretary has moved.</summary>
    public static Task<List<FileRecord>> Find(string query) => Task.Run(() =>
    {
        var words = RuText.Clean(query).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Length > 4 ? w[..^1] : w).ToArray();
        var res = new List<FileRecord>();
        if (words.Length == 0) return res;
        bool Hit(string s) { var c = RuText.Clean(s); return words.All(c.Contains); }
        foreach (var r in SecStore.Cur.FileLog.ToList().Where(r => (Hit(r.Name) || Hit(r.Source)) && File.Exists(r.Path))) res.Add(r);
        int looked = 0;
        var stack = new Stack<(string Path, int Depth)>();
        foreach (var root in new[] { Dir, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), Environment.GetFolderPath(Environment.SpecialFolder.Desktop) }.Distinct())
            if (Directory.Exists(root)) stack.Push((root, 0));
        // ponytail: walks the folders on each search (capped); a real index if this ever feels slow.
        while (stack.Count > 0 && looked < 30000 && res.Count < 30)
        {
            var (dir, depth) = stack.Pop();
            try
            {
                foreach (var f in new DirectoryInfo(dir).EnumerateFileSystemInfos())
                {
                    if (f.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
                    if (f is DirectoryInfo) { if (depth < 4 && !f.Name.StartsWith('.') && f.Name != "node_modules") stack.Push((f.FullName, depth + 1)); continue; }
                    looked++;
                    if (Hit(f.Name) && res.All(r => r.Path != f.FullName)) res.Add(new FileRecord { Name = f.Name, Path = f.FullName, Source = Source(f.FullName), When = f.LastWriteTime });
                }
            }
            catch { }   // a folder we may not read
        }
        return res.OrderByDescending(r => r.When).Take(30).ToList();
    });
}

/// <summary>
/// The schedule itself, read from the remembered page with Mishka's own browser: waits for the site to draw, presses the tab of the day
/// or "next week" if the day isn't on screen. A failure is remembered as a reason, never as "no lessons", and tried again in an hour.
/// </summary>
public static class SecSchedule
{
    static readonly Dictionary<string, List<string>> Days = new();
    static readonly SemaphoreSlim Busy = new(1, 1);

    public static RememberedItem? Page => SecStore.Cur.Remembered.FirstOrDefault(r => r.Daily && r.Target.StartsWith("http", StringComparison.OrdinalIgnoreCase));
    public static bool NeedsLogin => SecStore.Cur.ScheduleProblem == "login";
    public static string LoginUrl => Page?.Target ?? "";
    public static string Problem(DateTime day) => SecStore.Cur.ScheduleProblem;

    public static async Task<List<string>> Lines(DateTime day, bool fresh = false)
    {
        var s = Settings.Current.Secretary;
        var store = SecStore.Cur;
        var page = Page;
        if (!s.ScheduleRead || page == null) return new();
        var key = day.ToString("yyyy-MM-dd") + "|" + page.Target;
        if (!fresh && store.ScheduleCacheDay == key && store.ScheduleCache.Count > 0) return store.ScheduleCache;
        if (!fresh && Days.TryGetValue(key, out var known)) return known;
        // Not hammering the site: a failed read waits an hour unless the user asks directly.
        if (!fresh && (DateTime.Now - store.ScheduleTriedAt).TotalMinutes < (store.ScheduleProblem == "offline" ? 15 : 60)) return new();
        if (!await Busy.WaitAsync(0)) return new();
        try
        {
            store.ScheduleTriedAt = DateTime.Now;
            await Vpn.EnsureFor(page.Target);
            var lines = await ReadDay(page.Target, day);
            if (lines != null)
            {
                Days[key] = lines;
                store.ScheduleProblem = lines.Count == 0 ? "empty" : "";
                if (day == DateTime.Today && lines.Count > 0) { store.ScheduleCacheDay = key; store.ScheduleCache = lines; }
            }
            SecStore.Save();
            return lines ?? new();
        }
        catch (Exception e) { Log.Error(e); store.ScheduleProblem = "error"; return new(); }
        finally { Busy.Release(); }
    }

    /// <summary>The day's words a site might put on a tab or heading, most exact first.</summary>
    internal static string[] ClickWords(DateTime day, DateTime now)
    {
        var ru = new System.Globalization.CultureInfo("ru-RU");
        var l = new List<string> { day.ToString("dd.MM"), day.ToString("d MMMM", ru), day.ToString("dddd", ru), day.ToString("ddd", ru) };
        if (day == now.Date) l.Add("сегодня");
        if (day == now.Date.AddDays(1)) l.Add("завтра");
        return l.ToArray();
    }

    static async Task<List<string>?> ReadDay(string url, DateTime day)
    {
        var store = SecStore.Cur;
        var read = await WebReader.ReadWith(url, ClickWords(day, DateTime.Now), SameWeek(day, DateTime.Now));
        if (read.Login) { store.ScheduleProblem = "login"; return null; }
        if (read.Failed) { store.ScheduleProblem = "offline"; return null; }
        foreach (var text in read.Texts)
            if (SecPlan.ScheduleFor(text, day) is { } found) return found;
        store.ScheduleProblem = "notfound";
        // Kept for looking into a site the reader doesn't understand; it stays in the protected data folder.
        try { File.WriteAllText(System.IO.Path.Combine(Settings.Dir, "schedule-last.txt"), string.Join("\n=====\n", read.Texts)); } catch { }
        return null;
    }

    internal static bool SameWeek(DateTime day, DateTime now)
    {
        DateTime Monday(DateTime d) => d.Date.AddDays(-(((int)d.DayOfWeek + 6) % 7));
        return Monday(day) == Monday(now);
    }
}

/// <summary>Mail by IMAP, read only: the folder is opened read-only, so letters stay unread and nothing can be sent or deleted.</summary>
public static class SecMail
{
    public record Letter(string From, string Subject, string Preview, DateTime When, string Web, bool Bulk, string Address = "");

    public static string ShortFrom(string from) { var i = from.IndexOf('<'); return (i > 0 ? from[..i] : from).Trim(' ', '"'); }

    /// <summary>A prepared reply in the user's own mail: it opens as a draft, and only they can press "send".</summary>
    internal static string DraftUrl(string web, string to, string subject, string name)
    {
        var su = subject.StartsWith("Re:", StringComparison.OrdinalIgnoreCase) ? subject : "Re: " + subject;
        var body = "Здравствуйте!\n\n\n\nС уважением,\n" + name;
        string E(string x) => Uri.EscapeDataString(x);
        if (web.Contains("mail.google")) return $"https://mail.google.com/mail/?view=cm&to={E(to)}&su={E(su)}&body={E(body)}";
        if (web.Contains("mail.yandex")) return $"https://mail.yandex.ru/compose?to={E(to)}&subject={E(su)}&body={E(body)}";
        if (web.Contains("e.mail.ru")) return $"https://e.mail.ru/compose/?to={E(to)}&subject={E(su)}&body={E(body)}";
        return $"mailto:{E(to)}?subject={E(su)}&body={E(body)}";
    }

    public static int Unread { get; private set; }
    public static List<Letter> Recent { get; private set; } = new();
    public static string LastError { get; private set; } = "";
    public static event Action? Changed;
    static readonly SemaphoreSlim Busy = new(1, 1);

    /// <summary>IMAP server and web mail by address. Outlook.com is left out: Microsoft no longer accepts app passwords there.</summary>
    internal static (string Host, string Web)? Provider(string email)
    {
        var d = email.Split('@').LastOrDefault()?.ToLowerInvariant() ?? "";
        return d switch
        {
            "yandex.ru" or "ya.ru" or "yandex.com" or "narod.ru" => ("imap.yandex.ru", "https://mail.yandex.ru"),
            "mail.ru" or "bk.ru" or "inbox.ru" or "list.ru" or "internet.ru" => ("imap.mail.ru", "https://e.mail.ru"),
            "gmail.com" or "googlemail.com" => ("imap.gmail.com", "https://mail.google.com"),
            "icloud.com" or "me.com" => ("imap.mail.me.com", "https://www.icloud.com/mail"),
            "rambler.ru" => ("imap.rambler.ru", "https://mail.rambler.ru"),
            _ => null,
        };
    }

    static readonly System.Text.RegularExpressions.Regex BulkSender = new(@"no-?reply|newsletter|news@|mailer|рассылк|promo|marketing|notifications?@", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    /// <summary>A newsletter says so in List-Unsubscribe; automatic senders give themselves away by the address.</summary>
    internal static bool IsBulk(string from, bool hasUnsubscribe) => hasUnsubscribe || BulkSender.IsMatch(from);

    public static void OpenWeb()
    {
        var web = SecStore.Cur.Mail.FirstOrDefault()?.Web;
        if (!string.IsNullOrEmpty(web)) Shell.Open(web);
    }

    static async Task<ImapClient> Connect(MailAccount a, CancellationToken ct)
    {
        var c = new ImapClient { Timeout = 30_000 };
        await c.ConnectAsync(a.Host, a.Port, MailKit.Security.SecureSocketOptions.SslOnConnect, ct);
        await c.AuthenticateAsync(a.Email, Secrets.Unprotect(a.Secret), ct);
        return c;
    }

    /// <summary>Checks the login and remembers where the inbox is now, so old letters don't arrive as a flood of "new" ones.</summary>
    public static async Task Add(string email, string password, string? host = null)
    {
        var p = Provider(email);
        var h = host ?? p?.Host ?? throw new InvalidOperationException("Не знаю сервер этой почты — укажи его (например imap.example.com)");
        var a = new MailAccount { Email = email.Trim(), Host = h, Web = p?.Web ?? "", Secret = Secrets.Protect(password) };
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(40));
        using var c = await Connect(a, cts.Token);
        await c.Inbox.OpenAsync(FolderAccess.ReadOnly, cts.Token);
        var all = await c.Inbox.SearchAsync(SearchQuery.All, cts.Token);
        a.LastUid = all.Count > 0 ? all.Max(u => u.Id) : 0;
        await c.DisconnectAsync(true, cts.Token);
        SecStore.Cur.Mail.RemoveAll(x => x.Email.Equals(a.Email, StringComparison.OrdinalIgnoreCase));
        SecStore.Cur.Mail.Add(a);
        SecStore.Save();
        _ = Check();
    }

    public static void Remove(MailAccount a) { SecStore.Cur.Mail.Remove(a); SecStore.Save(); Unread = 0; Recent = new(); Changed?.Invoke(); }

    public static async Task Check()
    {
        if (!await Busy.WaitAsync(0)) return;
        try
        {
            int unread = 0; var letters = new List<Letter>(); LastError = "";
            bool changed = false;
            foreach (var a in SecStore.Cur.Mail)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                    using var c = await Connect(a, cts.Token);
                    await c.Inbox.OpenAsync(FolderAccess.ReadOnly, cts.Token);
                    var ids = await c.Inbox.SearchAsync(SearchQuery.NotSeen, cts.Token);
                    unread += ids.Count;
                    var newest = ids.OrderByDescending(u => u.Id).Take(20).ToList();
                    var infos = newest.Count == 0 ? new List<IMessageSummary>() : (await c.Inbox.FetchAsync(newest, new FetchRequest(MessageSummaryItems.Envelope | MessageSummaryItems.UniqueId | MessageSummaryItems.PreviewText) { Headers = new HeaderSet(new[] { "List-Unsubscribe" }) }, cts.Token)).ToList();
                    foreach (var m in infos.OrderByDescending(m => m.UniqueId.Id))
                    {
                        var l = new Letter(m.Envelope.From.ToString(), m.Envelope.Subject ?? "(без темы)", m.PreviewText ?? "", m.Envelope.Date?.LocalDateTime ?? DateTime.Now, a.Web, IsBulk(m.Envelope.From.ToString(), m.Headers?.Contains("List-Unsubscribe") == true), m.Envelope.From.Mailboxes.FirstOrDefault()?.Address ?? "");
                        letters.Add(l);
                        if (m.UniqueId.Id > a.LastUid) Arrived(l);
                    }
                    if (newest.Count > 0 && newest[0].Id > a.LastUid) { a.LastUid = newest[0].Id; changed = true; }
                    await c.DisconnectAsync(true, cts.Token);
                }
                catch (Exception e) { LastError = $"{a.Email}: {e.Message}"; Log.Info("mail: " + LastError); }   // never log the password; MailKit's message has none
            }
            Unread = unread; Recent = letters.OrderByDescending(l => l.When).ToList();
            if (changed) SecStore.Save();
            Changed?.Invoke();
        }
        finally { Busy.Release(); }
    }

    static void Arrived(Letter l)
    {
        var s = Settings.Current.Secretary;
        if (l.Bulk) return;   // counted on the page as "ещё N рассылок", no toast
        var vip = s.MailImportant;
        bool important = vip.Count == 0 || vip.Any(v => (l.From + " " + l.Subject).Contains(v, StringComparison.OrdinalIgnoreCase));
        if (important)
        {
            Secretary.Notify("\uE715", ShortFrom(l.From), l.Subject, () => { if (l.Web.Length > 0) Shell.Open(l.Web); });
            Secretary.ToPhoneIfAway($"Письмо от {ShortFrom(l.From)}: {l.Subject}");
            if (l.Address.Length > 0) SecInbox.Add(new InboxItem { Kind = "mail", Title = "Письмо от " + ShortFrom(l.From), Text = l.Subject, Ref = DraftUrl(l.Web, l.Address, l.Subject, Environment.UserName) });
        }
        if (s.Deadlines) Secretary.Offer((l.Subject + ". " + l.Preview).Trim(), "из письма");
    }
}
