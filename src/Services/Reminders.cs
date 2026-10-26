using System.Windows.Threading;

namespace MishkaHelper.Core;

/// <summary>
/// Persistent reminders (Settings.Reminders): a toast (and Jarvis' voice in the full edition) when due.
/// Date-only reminders ring at 10:00 that day, plus "Завтра: …" at 19:00 the evening before.
/// Missed ones (PC was off) ring right after start.
/// </summary>
public static class Reminders
{
    static DispatcherTimer? _t;
    public static event Action<string>? Due;   // spoken text — Jarvis reads it out

    public static void Start()
    {
        _t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _t.Tick += (_, _) => Check(DateTime.Now);
        _t.Start();
        Check(DateTime.Now, quiet: true);   // missed while the PC was off: show them, but no sound at startup
    }

    /// <summary>When the reminder actually rings: its time, or 10:00 for a date-only one.</summary>
    public static DateTime RingAt(Reminder r) => r.HasTime ? r.At : r.At.Date.AddHours(10);

    public static Reminder Add(string text, DateTime at, bool hasTime)
    {
        var r = new Reminder { Text = text, At = at, HasTime = hasTime };
        Settings.Current.Reminders.Add(r);
        Settings.Save();
        return r;
    }

    public static void Remove(Reminder r) { Settings.Current.Reminders.Remove(r); Settings.Save(); }

    /// <summary>Not done yet, from now to `days` ahead, soonest first.</summary>
    public static List<Reminder> Upcoming(int days = 7) =>
        Settings.Current.Reminders.Where(r => !r.Done && RingAt(r) <= DateTime.Now.AddDays(days)).OrderBy(RingAt).ToList();

    internal static void Check(DateTime now, bool quiet = false)
    {
        bool changed = false;
        foreach (var r in Settings.Current.Reminders.Where(r => !r.Done).ToList())
        {
            if (!r.HasTime && !r.EveWarned && now >= r.At.Date.AddDays(-1).AddHours(19) && now < r.At.Date)
            {
                r.EveWarned = true; changed = true;
                Ring("Завтра", r.Text, quiet);
            }
            if (now >= RingAt(r))
            {
                r.Done = r.EveWarned = true; changed = true;
                Ring(r.HasTime ? "Напоминание" : "Сегодня", r.Text, quiet);
            }
        }
        // Keep a week of finished ones (visible in the list), then forget them.
        changed |= Settings.Current.Reminders.RemoveAll(r => r.Done && RingAt(r) < now.AddDays(-7)) > 0;
        if (changed) Settings.Save();
    }

    static void Ring(string title, string text, bool quiet)
    {
        if (quiet) { Toast.Show("", title, text, () => App.Current.Navigate("Home")); return; }
        Toast.Show("", title, text, () => App.Current.Navigate("Home"));
        System.Media.SystemSounds.Exclamation.Play();
        Due?.Invoke($"{title}: {text}");
    }

    /// <summary>Google Calendar "new event" page, pre-filled — one click on «Сохранить» and it's in the user's calendar.</summary>
    public static string GoogleLink(Reminder r)
    {
        string dates = r.HasTime
            ? $"{r.At:yyyyMMdd'T'HHmmss}/{r.At.AddHours(1):yyyyMMdd'T'HHmmss}"
            : $"{r.At:yyyyMMdd}/{r.At.AddDays(1):yyyyMMdd}";   // all-day: end date is exclusive
        var tz = TimeZoneInfo.TryConvertWindowsIdToIanaId(TimeZoneInfo.Local.Id, out var iana) ? "&ctz=" + Uri.EscapeDataString(iana) : "";
        return "https://calendar.google.com/calendar/render?action=TEMPLATE&text=" + Uri.EscapeDataString(Cap(r.Text)) + "&dates=" + dates + tz +
               "&details=" + Uri.EscapeDataString("Добавлено Mishka Helper");
    }

    public static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];
}
