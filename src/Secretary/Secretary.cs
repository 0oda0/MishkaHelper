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
    static DateTime _activeSince = DateTime.Now, _lastSuggest;

    public static string CurrentApp { get; private set; } = "";
    public static string CurrentTitle { get; private set; } = "";
    public static bool CurrentFullScreen { get; private set; }
    public static event Action? Changed;

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, System.Text.StringBuilder s, int n);
    delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc f, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct LASTINPUTINFO { public uint cbSize, dwTime; }
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO i);

    public static void Start()
    {
        _t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _t.Tick += (_, _) => Tick();
        _t.Start();
        Reminders.Due += ToPhoneIfAway;
        SecInbox.Changed += () => { SecBubble.Apply(CurrentFullScreen); ShowCount(); };
        ShowCount();
        ClipHistory.Copied += OnCopied;
        Apply();
    }

    static void ShowCount() => Tray.Current?.Status(S.Enabled && SecInbox.Count > 0 ? $"ждёт ответа: {SecInbox.Count}" : "");

    /// <summary>After any switch changes: start or stop what runs in the background.</summary>
    public static void Apply()
    {
        SecDownloads.Apply(S.Enabled && S.Downloads);
        SecBubble.Apply(CurrentFullScreen);
        ShowCount();
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
            SecInbox.Add(new InboxItem { Kind = "note", Title = title, Text = text });
            return;
        }
        Toast.Show(glyph, title, text, click);
    }

    /// <summary>Away from the PC for 10+ minutes: the phone gets it, the screen of an empty room doesn't help.</summary>
    public static bool Away => IdleMs() > 10 * 60_000;
    public static void ToPhoneIfAway(string text) { if (S.Enabled && S.ToPhone && Away) _ = Phone.Say(text); }

    public static IntPtr ForegroundWindow() => GetForegroundWindow();

    public static (int Pid, string Name, string Title) Foreground()
    {
        var h = GetForegroundWindow();
        GetWindowThreadProcessId(h, out var pid);
        var sb = new System.Text.StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            if (!p.ProcessName.Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)) return ((int)pid, p.ProcessName, sb.ToString());
            // Store apps live inside this host window: the real program is the child window of another process.
            uint real = 0;
            EnumChildWindows(h, (c, _) => { GetWindowThreadProcessId(c, out var cp); if (cp != pid) { real = cp; return false; } return true; }, IntPtr.Zero);
            if (real == 0) return ((int)pid, p.ProcessName, sb.ToString());
            using var rp = Process.GetProcessById((int)real);
            return ((int)real, rp.ProcessName, sb.ToString());
        }
        catch { return (0, "", ""); }
    }

    static uint IdleMs()
    {
        var i = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        return GetLastInputInfo(ref i) ? (uint)Environment.TickCount - i.dwTime : 0;
    }

    static void Tick()
    {
        if (!S.Enabled) { SecBubble.Apply(false); return; }
        _tick++;
        try
        {
            var (_, name, title) = Foreground();
            CurrentFullScreen = Focus.FullScreenNow();
            SecBubble.Apply(CurrentFullScreen);
            // Our own windows don't count: the note hotkey must still refer to the program underneath.
            if (name.Length > 0 && !name.Equals("MishkaHelper", StringComparison.OrdinalIgnoreCase))
            {
                CurrentApp = name; CurrentTitle = title;
                if (S.AppHints) ShowHint(name, title);
                if (name != _appNow) { _appNow = name; _appSince = DateTime.Now; }
                if (S.Habits && _tick % 30 == 0 && IdleMs() < 120_000 && !SecContext.ShouldStayQuiet(name, false)) SecHabits.Note(name, DateTime.Now, SecStore.Cur.HabitLog);
                if (S.AppTriggers && SecStore.Cur.Triggers.Count > 0) CheckTriggers(name, title);
            }
            if (_tick % 15 == 0) Minute();
        }
        catch (Exception e) { Log.Error(e); }
    }

    static string _appNow = "";
    static DateTime _appSince = DateTime.Now;
    static string _hintSeen = "";
    static readonly Dictionary<string, DateTime> HintedAt = new();

    /// <summary>What the secretary said about the program in front, for the page and the self-test.</summary>
    public static event Action<string, string?>? Hinted;

    // Each program or site is judged once per title change; a hint repeats no sooner than 4 hours later.
    static async void ShowHint(string name, string title)
    {
        var seen = name + "|" + title;
        if (seen == _hintSeen) return;
        _hintSeen = seen;
        if (!S.AppNotes && !S.ContextHints) return;
        var window = GetForegroundWindow();
        var url = await Task.Run(() => SecCapture.BrowserUrl(window, name));
        if (seen != _hintSeen) return;   // moved on meanwhile
        var hint = SecContext.For(name, title, url, CurrentFullScreen, S.AppNotes ? SecStore.Cur.AppNotes : new Dictionary<string, string>(), S.ContextHints ? SecStore.Cur.Remembered : new(), S.ContextHints && S.Tasks ? OpenTasks().Select(t => t.Text) : Array.Empty<string>());
        Hinted?.Invoke(name, hint?.Text);
        if (hint == null || (HintedAt.TryGetValue(hint.Key, out var at) && (DateTime.Now - at).TotalHours < 4)) return;
        HintedAt[hint.Key] = DateTime.Now;
        Action click = hint.Action switch
        {
            "note" => () => SecCapture.AppNote(),
            _ => () => SecDesk.Open(activate: true),
        };
        if (SecBubble.Shown) SecBubble.Say(hint.Text, click);
        else Notify("\uE70B", S.Name, hint.Text, click);
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

    /// <summary>Sunday evening: the week in two lines. Finished tasks are kept for a week, so the count is honest.</summary>
    internal static List<string> WeekLog(DateTime now, IEnumerable<SecTask> tasks)
    {
        var all = tasks.ToList();
        int done = all.Count(t => t.DoneAt > now.AddDays(-7));
        int late = all.Count(t => t.DoneAt == null && t.Due?.Date < now.Date);
        int next = all.Count(t => t.DoneAt == null && t.Due?.Date > now.Date && t.Due <= now.AddDays(7));
        return new() { $"За неделю сделано: {done}" + (late > 0 ? $", просрочено: {late}" : ""), $"На следующей неделе дел со сроком: {next}" };
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
        if (S.Meetings || S.Prep) _ = Prepare(now);
        if (S.Habits && _tick % 1800 == 0) CheckHabits(now);
        if (S.LongSession && S.Tasks) CheckLongSession(now);
        if (S.CheckIns && S.Tasks && idle < 60_000 && !CurrentFullScreen && !Focus.On && CheckInDue(S.CheckInHours, store.LastCheckIn, now))
        {
            store.LastCheckIn = now; SecStore.Save();
            CheckIn(now);
        }
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
        if (S.Downloads && SecDownloads.RecentCount() is > 0 and var n) l.Add(new("\uE896", $"Новых файлов в «Загрузках» за неделю: {n}", () => App.Current.Navigate("Secretary")));
        return l;
    }

    // The morning summary stays until closed: a 6-second toast was easy to miss.
    static Task Morning()
    {
        SecInbox.ReviewOverdue(DateTime.Now);
        SecDesk.Open(activate: false);
        if (S.SpeakMorning) Speak();
        return Task.CompletedTask;
    }

    static async void Speak()
    {
#if JARVIS
        var l = await Today();
        if (Settings.Current.Jarvis.Speak) _ = Voice.Say(SecVoiceTone.Greeting(DateTime.Now, S) + ". " + (l.Count == 0 ? "На сегодня дел нет." : string.Join(". ", l.Take(4).Select(x => x.Text))));
#else
        await Task.CompletedTask;
#endif
    }

    static async void Evening()
    {
        var day = DateTime.Today.AddDays(1);
        var items = Reminders.Upcoming(2).Where(r => Reminders.RingAt(r).Date == day).Select(r => r.Text).ToList();
        items.AddRange(await SecSchedule.Lines(day));
        var daily = SecStore.Cur.Remembered.FirstOrDefault(x => x.Daily);
        var log = S.DayLog ? DayLog() : new();
        if (S.DayLog && DateTime.Today.DayOfWeek == DayOfWeek.Sunday) log.AddRange(WeekLog(DateTime.Now, SecStore.Cur.Tasks));
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

    static readonly HashSet<string> PrepShown = new();

    // Ahead of a lesson or meeting: what, where, and one press to open the prepared set or the meeting link.
    static async Task Prepare(DateTime now)
    {
        try
        {
            var ahead = now.AddMinutes(Math.Max(5, S.PrepMinutes));
            var soon = new List<(DateTime At, string Text, string Place)>();
            if (S.Meetings && Calendar.Configured)
                foreach (var e in await Calendar.On(now.Date)) if (!e.AllDay) soon.Add((e.Start, e.Title, e.Place));
            if (S.Prep)
                foreach (var line in await SecSchedule.Lines(now.Date))
                    if (SecPlan.LineTime(line) is { } t) soon.Add((now.Date + t, line, ""));
            foreach (var (at, text, place) in soon)
            {
                if (at <= now || at > ahead || !PrepShown.Add(at.ToString("o") + text)) continue;
                var buttons = new List<(string, Action)>();
                var link = AnyUrl.Match(place).Value;
                if (link.Length > 0) buttons.Add(("Подключиться", () => Shell.Open(link)));
                var set = SecStore.Cur.Workspaces.Keys.FirstOrDefault();
                if (set != null) buttons.Add(($"Набор «{set}»", () => SecWorkspace.OpenSet(set)));
                var title = $"Через {(int)Math.Ceiling((at - now).TotalMinutes)} мин";
                var body = place.Length > 0 && link.Length == 0 ? $"{text}, {place}" : text;
                if (S.QuietInGame && (Focus.On || CurrentFullScreen)) continue;
                Toast.Show("\uE787", title, body, null, buttons.Count > 0 ? buttons.ToArray() : null);
                ToPhoneIfAway($"{title}: {body}");
            }
        }
        catch (Exception e) { Log.Error(e); }
    }

    /// <summary>A check-in hour has come and there was no check-in since it. Pure, for tests.</summary>
    internal static bool CheckInDue(List<int> hours, DateTime last, DateTime now) =>
        hours.Where(h => h is >= 0 and < 24).Select(h => now.Date.AddHours(h)).Any(at => now >= at && last < at && (now - at).TotalHours < 3);

    /// <summary>
    /// "Как дела с планами?": today's open tasks land in the window, each with Сделано / На завтра, so the day gets sorted in a few clicks.
    /// No plans at all → it asks what they are, with one press to write them down.
    /// </summary>
    static void CheckIn(DateTime now)
    {
        var today = OpenTasks().Where(t => t.Due == null || t.Due?.Date <= now.Date).Take(6).ToList();
        if (today.Count == 0)
        {
            Toast.Show("\uE70B", S.Name, now.Hour < 15 ? "Что на сегодня в планах? Запишу и напомню." : "Что ещё хочешь успеть сегодня или завтра?",
                null, new (string, Action)[] { ("Записать", () => SecCapture.Quick()), ("Не сейчас", () => { }) });
            return;
        }
        foreach (var t in today) SecInbox.Add(new InboxItem { Kind = "plan", Title = "Как с этим?", Text = t.Text, Ref = t.Id, At = t.Due });
        Toast.Show("\uE70B", "Как дела с планами?", string.Join("\n", today.Take(3).Select(t => "• " + t.Text)) + (today.Count > 3 ? $"\nи ещё {today.Count - 3}" : ""),
            () => SecDesk.Open(activate: true), new (string, Action)[] { ("Отметить", () => SecDesk.Open(activate: true)), ("Позже", () => { SecStore.Cur.LastCheckIn = DateTime.Now.AddHours(-3); SecStore.Save(); }) });
        ToPhoneIfAway($"Как дела с планами? {today[0].Text}" + (today.Count > 1 ? $" и ещё {today.Count - 1}" : ""));
    }

    static void CheckHabits(DateTime now)
    {
        var store = SecStore.Cur;
        foreach (var key in SecHabits.Found(store.HabitLog, store.HabitAsked, now).Take(1).ToList())
        {
            var p = key.Split('|', 3);
            int dow = int.Parse(p[0]), hour = int.Parse(p[1]);
            var ru = new System.Globalization.CultureInfo("ru-RU");
            var next = now.Date.AddDays(((dow - (int)now.DayOfWeek) + 7) % 7).AddHours(hour);
            if (next <= now) next = next.AddDays(7);
            store.HabitAsked.Add(key);
            SecInbox.Add(new InboxItem { Kind = "habit", Title = $"Ты часто открываешь {p[2]}: {next.ToString("dddd", ru)}, около {hour}:00. Напоминать?", Text = $"Обычно в это время: {p[2]}", At = next, HasTime = true });
        }
    }

    static bool _longToldToday;
    static DateTime _longDay;

    static void CheckLongSession(DateTime now)
    {
        if (_longDay != now.Date) { _longDay = now.Date; _longToldToday = false; }
        if (_longToldToday || (now - _appSince).TotalHours < 3 || SecContext.ShouldStayQuiet(_appNow, CurrentFullScreen)) return;
        var due = OpenTasks().FirstOrDefault(t => t.Due?.Date <= now.Date);
        if (due == null) return;
        _longToldToday = true;
        var text = $"Уже 3 часа в {_appNow}, а на сегодня ещё открыто: {due.Text}";
        if (SecBubble.Shown) SecBubble.Say(text, () => SecDesk.Open(activate: true)); else Notify("\uE823", S.Name, text);
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
        var item = new InboxItem { Kind = "offer", Title = $"Напомнить {When.Say(d.At, d.HasTime, DateTime.Now)}? ({source})", Text = label, At = d.At, HasTime = d.HasTime };
        SecInbox.Add(item);   // stays in the inbox if the toast is missed
        if (S.QuietInGame && (Focus.On || CurrentFullScreen)) return;
        Toast.Show("\uE787", item.Title, label, () => SecDesk.Open(activate: true), SecInbox.Buttons(item));
    }

    static void OnCopied(string text)
    {
        if (!S.Enabled || !S.Deadlines || (DateTime.Now - _lastSuggest).TotalSeconds < 60) return;
        if (FindDeadline(text, DateTime.Now) == null) return;
        _lastSuggest = DateTime.Now;
        Offer(text, "из буфера");
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
