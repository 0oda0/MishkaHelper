using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Threading;

namespace MishkaHelper.Core;

/// <summary>
/// The secretary: watches which program is in front, keeps notes per program and site, turns dates in mail and copied text
/// into reminder offers, gives a morning and evening summary, remembers where things are. All of it local, no AI.
/// The whole mode and every feature switch off separately (SecretarySettings).
/// </summary>
public static class Secretary
{
    static SecretarySettings S => Settings.Current.Secretary;
    static DispatcherTimer? _t;
    static int _tick;
    static string _lastKey = "";
    static readonly HashSet<string> HintShown = new();
    static readonly HashSet<string> MeetingShown = new();
    static DateTime _activeSince = DateTime.Now, _lastSuggest;

    public static string CurrentApp { get; private set; } = "";
    public static string CurrentTitle { get; private set; } = "";
    public static bool CurrentFullScreen { get; private set; }
    public static event Action? Changed;

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO i);

    public static void Start()
    {
        _t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _t.Tick += (_, _) => Tick();
        _t.Start();
        Reminders.Due += text => { if (S.Enabled && S.ToPhone) _ = Phone.Say(text); };
        ClipHistory.Copied += OnCopied;
        Apply();
    }

    /// <summary>After any switch changes: start or stop what runs in the background.</summary>
    public static void Apply()
    {
        SecDownloads.Apply(S.Enabled && S.Downloads);
        Changed?.Invoke();
    }

    public static bool Active(bool feature)
    {
        if (S.Enabled && feature) return true;
        return false;
    }

    /// <summary>Hotkey pressed while off: say why nothing happens, instead of silence.</summary>
    public static bool Require(bool feature, string name)
    {
        if (Active(feature)) return true;
        Toast.Show("", "Секретарша", S.Enabled ? $"Функция «{name}» выключена." : "Секретарша выключена. Нажми, чтобы настроить.", () => App.Current.Navigate("Secretary"));
        return false;
    }

    /// <summary>A toast that respects "не мешать в игре": during game mode it waits in the suggestions list.</summary>
    public static void Notify(string glyph, string title, string text, Action? click = null)
    {
        if (S.QuietInGame && (Focus.On || CurrentFullScreen))
        {
            AddSuggestion($"{title}: {text}", "пока ты был в игре");
            return;
        }
        Toast.Show(glyph, title, text, click);
    }

    public static IntPtr ForegroundWindow() => GetForegroundWindow();

    public static (int Pid, string Name, string Title) Foreground()
    {
        var h = GetForegroundWindow();
        GetWindowThreadProcessId(h, out var pid);
        var sb = new System.Text.StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        try { using var p = Process.GetProcessById((int)pid); return ((int)pid, p.ProcessName, sb.ToString()); } catch { return (0, "", ""); }
    }

    static uint IdleMs()
    {
        var i = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref i) ? (uint)Environment.TickCount - i.dwTime : 0;
    }

    static void Tick()
    {
        if (!S.Enabled) return;
        _tick++;
        try
        {
            var (_, name, title) = Foreground();
            CurrentFullScreen = SHQueryUserNotificationState(out var st) == 0 && st is 2 or 3;
            // Our own windows don't count: the note hotkey must still refer to the program underneath.
            if (name.Length > 0 && !name.Equals("MishkaHelper", StringComparison.OrdinalIgnoreCase))
            {
                CurrentApp = name; CurrentTitle = title;
                if (S.AppHints) ShowHint(name);
                if (S.AppTriggers && SecStore.Cur.Triggers.Count > 0) CheckTriggers(name, title);
            }
            if (_tick % 15 == 0) Minute();
        }
        catch (Exception e) { Log.Error(e); }
    }

    // A note appears once per session, the first time its program comes to the front.
    static void ShowHint(string name)
    {
        var key = name.ToLowerInvariant();
        if (key == _lastKey) return;
        _lastKey = key;
        if (!S.AppNotes || !HintShown.Add(key) || !SecStore.Cur.AppNotes.TryGetValue(key, out var note) || note.Trim().Length == 0) return;
        Notify("", "Заметка: " + name, note.Length > 200 ? note[..200] + "…" : note, () => SecCapture.AppNote());
    }

    static string _trigSeen = "";

    // One shot: said once when that program or site comes up, then forgotten.
    static async void CheckTriggers(string name, string title)
    {
        var seen = name + "|" + title;
        if (seen == _trigSeen) return;
        _trigSeen = seen;
        var window = GetForegroundWindow();
        var url = await Task.Run(() => SecCapture.BrowserUrl(window, name));
        foreach (var t in SecStore.Cur.Triggers.Where(t => SecPlan.Matches(t.Key, name, title, url)).ToList())
        {
            SecStore.Cur.Triggers.Remove(t);
            SecStore.Save();
            Toast.Show("\uE7E7", "Ты просил напомнить", t.Text);
            Changed?.Invoke();
        }
    }

    public static List<SecTask> OpenTasks() =>
        SecStore.Cur.Tasks.Where(t => t.DoneAt == null).OrderBy(t => t.Due ?? DateTime.MaxValue).ThenBy(t => t.Created).ToList();

    /// <summary>What is due today or already late: morning summary, and the check before switching the PC off.</summary>
    public static List<string> TodoToday()
    {
        var res = OpenTasks().Where(t => t.Due?.Date <= DateTime.Today).Select(t => t.Text).ToList();
        res.AddRange(Reminders.Upcoming(1).Where(r => Reminders.RingAt(r).Date == DateTime.Today && Reminders.RingAt(r) > DateTime.Now && !res.Contains(r.Text)).Select(r => r.Text));
        return res;
    }

    public static void Finish(SecTask t)
    {
        t.DoneAt = DateTime.Now;
        // Finished ones stay a week for the day log, then go.
        SecStore.Cur.Tasks.RemoveAll(x => x.DoneAt < DateTime.Now.AddDays(-7));
        SecStore.Save();
        Changed?.Invoke();
    }

    public static List<string> DayLog()
    {
        var l = new List<string>();
        var top = ScreenTime.Top(1).Take(4).Select(x => $"{x.App} {ScreenTime.Format(x.Seconds)}").ToList();
        if (top.Count > 0) l.Add("Больше всего: " + string.Join(", ", top));
        var done = SecStore.Cur.Tasks.Count(t => t.DoneAt?.Date == DateTime.Today);
        var left = OpenTasks().Count(t => t.Due?.Date <= DateTime.Today);
        if (done > 0 || left > 0) l.Add($"Дел сделано: {done}" + (left > 0 ? $", осталось на сегодня: {left}" : ""));
        return l;
    }

    // Birthdays and anniversaries from the calendar, 3 days ahead.
    static readonly Regex DateEvent = new(@"день\s+рождени|\bдр\b|годовщин|birthday|anniversary", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static async Task<List<string>> CalendarDates()
    {
        var res = new List<string>();
        if (!S.Dates || !Calendar.Configured) return res;
        for (int d = 0; d <= 3; d++)
            try
            {
                foreach (var e in await Calendar.On(DateTime.Today.AddDays(d)))
                    if (DateEvent.IsMatch(e.Title)) res.Add((d == 0 ? "Сегодня: " : $"Через {d} дн.: ") + e.Title);
            }
            catch (Exception e) { Log.Error(e); }
        return res;
    }

    // Every 30 s.
    static void Minute()
    {
        var now = DateTime.Now;
        var idle = IdleMs();
        if (idle > 5 * 60_000) _activeSince = now;
        var store = SecStore.Cur;

        if (S.Morning && store.LastMorning.Date != now.Date && now.Hour >= 5 && idle < 60_000 && !CurrentFullScreen && !Focus.On)
        {
            store.LastMorning = now; SecStore.Save();
            _ = Morning();
        }
        if (S.Evening && store.LastEvening.Date != now.Date && now.Hour >= S.EveningHour && idle < 60_000 && !CurrentFullScreen && !Focus.On)
        {
            store.LastEvening = now; SecStore.Save();
            Evening();
        }
        if (S.Breaks && !Focus.On && !CurrentFullScreen && (now - _activeSince).TotalMinutes >= Math.Max(15, S.BreakMinutes))
        {
            _activeSince = now;
            Toast.Show("", "Перерыв", $"Ты за компьютером уже {Math.Max(15, S.BreakMinutes)} минут. Встань, разомнись.");
        }
        if (S.Meetings) _ = Meetings(now);
        if (S.Workspace && _tick % 450 == 0 && idle < 5 * 60_000) _ = SecWorkspace.AutoSave();
        if (S.Mail && _tick % (Math.Max(5, S.MailMinutes) * 30) == 15) _ = SecMail.Check();
    }

    // ---------- summaries ----------
    public record Line(string Glyph, string Text, Action? Click);

    /// <summary>The "today" list: page card and morning toast use the same data.</summary>
    public static async Task<List<Line>> Today()
    {
        var l = new List<Line>();
        if (S.Tasks)
            foreach (var t in OpenTasks().Where(t => t.Due?.Date <= DateTime.Today))
                l.Add(new("\uE73E", (t.Due < DateTime.Today ? "Просрочено: " : "") + t.Text, null));
        foreach (var r in Reminders.Upcoming(1).Where(r => Reminders.RingAt(r).Date == DateTime.Today && !(S.Tasks && OpenTasks().Any(t => t.Text == r.Text))))
            l.Add(new("\uE787", (r.HasTime ? r.At.ToString("HH:mm ") : "") + r.Text, null));
        foreach (var d in await CalendarDates()) l.Add(new("\uE787", d, null));
        foreach (var line in await SecSchedule.Lines(DateTime.Today)) l.Add(new("\uE8A1", line, null));
        if (Calendar.Configured)
            try { foreach (var e in await Calendar.On(DateTime.Today)) l.Add(new("", (e.AllDay ? "" : e.Start.ToString("HH:mm ")) + e.Title, null)); } catch (Exception e) { Log.Error(e); }
        if (S.Mail && SecMail.Unread > 0) l.Add(new("\uE715", $"Непрочитанных писем: {SecMail.Unread}" + (SecMail.Recent.Count(x => x.Bulk) is > 0 and var bulk ? $" (из последних {bulk} рассылки)" : ""), SecMail.OpenWeb));
        foreach (var d in SecStore.Cur.Remembered.Where(x => x.Daily)) l.Add(new("", d.Title, () => Open(d)));
        if (S.Workspace && SecStore.Cur.LastWorkspace.Count > 0 && SecStore.Cur.LastWorkspaceAt.Date < DateTime.Today)
            l.Add(new("", $"Вернуть рабочее место ({SecStore.Cur.LastWorkspaceAt:dd.MM HH:mm})", () => SecWorkspace.Restore(SecStore.Cur.LastWorkspace)));
        if (S.Downloads && SecDownloads.Pending().Count is > 0 and var n) l.Add(new("", $"Файлов разобрать в «Загрузках»: {n}", () => App.Current.Navigate("Secretary")));
        return l;
    }

    static async Task Morning()
    {
        var l = await Today();
        Toast.Show("", "Доброе утро", l.Count == 0 ? "На сегодня дел нет." : string.Join("\n", l.Take(5).Select(x => "• " + x.Text)), () => App.Current.Navigate("Secretary"));
    }

    static async void Evening()
    {
        var day = DateTime.Today.AddDays(1);
        var items = Reminders.Upcoming(2).Where(r => Reminders.RingAt(r).Date == day).Select(r => r.Text).ToList();
        items.AddRange(await SecSchedule.Lines(day));
        var daily = SecStore.Cur.Remembered.FirstOrDefault(x => x.Daily);
        var log = S.DayLog ? DayLog() : new();
        if (items.Count == 0 && daily == null && log.Count == 0) return;
        var parts = new List<string>();
        if (log.Count > 0) parts.Add(string.Join("\n", log));
        if (items.Count > 0) parts.Add("Завтра:\n" + string.Join("\n", items.Take(6).Select(x => "• " + x)));
        if (daily != null) parts.Add("Посмотреть: " + daily.Title);
        Toast.Show("\uE787", "Итог дня", string.Join("\n", parts), daily != null ? () => Open(daily) : () => App.Current.Navigate("Secretary"));
    }

    public static void Open(RememberedItem r)
    {
        if (Uri.TryCreate(r.Target, UriKind.Absolute, out var u) && u.Scheme is "http" or "https") Shell.Open(u.AbsoluteUri);
        else Toast.Show("", r.Title, $"Было открыто в {r.App}: {r.Target}");
    }

    static readonly Regex AnyUrl = new(@"https?://\S+", RegexOptions.Compiled);

    // A meeting link in the event's place opens with one click, 10 minutes ahead.
    static async Task Meetings(DateTime now)
    {
        if (!Calendar.Configured) return;
        try
        {
            foreach (var e in await Calendar.On(now.Date))
            {
                if (e.AllDay || e.Start <= now || e.Start > now.AddMinutes(10) || !MeetingShown.Add(e.Start.ToString("o") + e.Title)) continue;
                var link = AnyUrl.Match(e.Place).Value;
                Notify("", $"Через {(int)Math.Ceiling((e.Start - now).TotalMinutes)} мин: {e.Title}", e.Place, link.Length > 0 ? () => Shell.Open(link) : null);
            }
        }
        catch (Exception e) { Log.Error(e); }
    }

    // ---------- dates in text ----------
    static readonly Regex DeadlineWord = new(@"сдат|сдач|срок|дедлайн|deadline|встреч|экзамен|зач[её]т|оплат|запис|при[её]м|концерт|билет|рейс|поезд|вылет|собеседован|созвон|лекци|семинар|до\s+\d", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A future date next to a word that means business. Plain dates in copied text (a news item, a code snippet) are not offers.</summary>
    public static When.Result? FindDeadline(string text, DateTime now)
    {
        if (text.Length is < 8 or > 500 || !DeadlineWord.IsMatch(text)) return null;
        var r = When.Parse(text, now);
        return r != null && r.At > now && r.At < now.AddYears(1) ? r : null;
    }

    public static void Offer(string text, string source)
    {
        var d = FindDeadline(text, DateTime.Now);
        if (d == null) return;
        var label = text.Length > 120 ? text[..120] + "…" : text;
        Notify("", "Напомнить?", $"{When.Say(d.At, d.HasTime, DateTime.Now)}: {label}\nНажми, чтобы добавить ({source}).", () =>
        {
            Reminders.Add(label, d.At, d.HasTime);
            Osd.Show("", "Напоминание добавлено");
        });
    }

    static void OnCopied(string text)
    {
        if (!S.Enabled || !S.Deadlines || (DateTime.Now - _lastSuggest).TotalSeconds < 60) return;
        if (FindDeadline(text, DateTime.Now) == null) return;
        _lastSuggest = DateTime.Now;
        Offer(text, "из буфера");
    }

    public static void AddSuggestion(string text, string source)
    {
        var list = SecStore.Cur.Suggestions;
        if (list.Any(s => s.Text == text)) return;
        list.Insert(0, new Suggestion { Text = text, Source = source });
        if (list.Count > 50) list.RemoveAt(50);
        SecStore.Save();
        Changed?.Invoke();
    }

    /// <summary>Search over notes, app notes and remembered pages.</summary>
    public static List<Line> Search(string query)
    {
        var words = RuText.Clean(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool Hit(string s) { var c = RuText.Clean(s); return words.Length > 0 && words.All(w => c.Contains(w.Length > 4 ? w[..^1] : w)); }
        var l = new List<Line>();
        foreach (var r in SecStore.Cur.Remembered.Where(r => Hit(r.Title + " " + r.Target))) l.Add(new("", r.Title, () => Open(r)));
        foreach (var t in SecStore.Cur.Tasks.Where(t => Hit(t.Text))) l.Add(new("\uE73E", (t.DoneAt != null ? "Сделано: " : "Дело: ") + t.Text, null));
        foreach (var (k, v) in SecStore.Cur.AppNotes.Where(kv => Hit(kv.Key + " " + kv.Value))) l.Add(new("\uE70B", $"{k}: {v}", null));
        foreach (var n in Notes.All.Where(n => Hit(n.Text)).OrderByDescending(n => n.When).Take(20)) l.Add(new("", $"{n.When:dd.MM} {n.Text}", null));
        return l;
    }
}
