using System.Globalization;
using System.Net.Http;

namespace MishkaHelper.Core;

/// <summary>
/// Reads the user's calendars from ICS links (Google/Outlook "secret iCal address", university schedule exports)
/// so Jarvis can answer "что у меня завтра". Read-only, cached for 30 minutes.
/// </summary>
public static class Calendar
{
    public record Event(DateTime Start, DateTime End, bool AllDay, string Title, string Place);

    public record Raw(DateTime Start, TimeSpan Length, bool AllDay, string Title, string Place, string Freq, int Interval, DateTime? Until, int Count, DayOfWeek[] Days, HashSet<DateTime> Except);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static List<Raw> _cache = new();
    static DateTime _loaded;

    public static bool Configured => Settings.Current.Jarvis.Calendars.Count > 0;

    public static async Task<List<Event>> On(DateTime day)
    {
        if (DateTime.Now - _loaded > TimeSpan.FromMinutes(30) || _cache.Count == 0) await Load();
        return _cache.Where(r => Occurs(r, day.Date))
            .Select(r => { var s = day.Date + r.Start.TimeOfDay; return new Event(s, s + r.Length, r.AllDay, r.Title, r.Place); })
            .OrderBy(e => e.Start).ToList();
    }

    public static async Task Load()
    {
        var all = new List<Raw>();
        foreach (var url in Settings.Current.Jarvis.Calendars)
        {
            try { all.AddRange(Parse(await Http.GetStringAsync(url.Replace("webcal://", "https://")))); }
            catch (Exception e) { Log.Error(e); }
        }
        _cache = all; _loaded = DateTime.Now;
    }

    /// <summary>Spoken summary for a day: "Завтра 3 события: в 9:00 Матанализ (ауд. 401), …".</summary>
    public static async Task<string> Say(DateTime day)
    {
        if (!Configured) return "Календарь не подключён: добавь ссылку iCal на странице «Джарвис» → «Календарь».";
        var ev = await On(day);
        var name = day.Date == DateTime.Today ? "Сегодня" : day.Date == DateTime.Today.AddDays(1) ? "Завтра" : day.ToString("dddd, d MMMM", new CultureInfo("ru-RU"));
        if (ev.Count == 0) return $"{name} в календаре пусто.";
        return $"{name} {ev.Count} {Plural(ev.Count)}: " + string.Join("; ", ev.Select(e =>
            (e.AllDay ? "весь день" : $"в {e.Start:H:mm}") + " " + e.Title + (e.Place.Length > 0 ? $" ({e.Place})" : ""))) + ".";
    }

    static string Plural(int n) => n % 10 == 1 && n % 100 != 11 ? "событие" : n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14 ? "события" : "событий";

    // ---------- ICS ----------
    public static List<Raw> Parse(string ics)
    {
        // RFC 5545 line folding: a line starting with a space/tab continues the previous one.
        var lines = new List<string>();
        foreach (var l in ics.Replace("\r\n", "\n").Split('\n'))
            if (l.Length > 0 && (l[0] == ' ' || l[0] == '\t') && lines.Count > 0) lines[^1] += l[1..]; else lines.Add(l);

        var list = new List<Raw>();
        Dictionary<string, string>? ev = null;
        var except = new HashSet<DateTime>();
        foreach (var line in lines)
        {
            if (line == "BEGIN:VEVENT") { ev = new(); except = new(); continue; }
            if (line == "END:VEVENT" && ev != null)
            {
                if (ev.TryGetValue("DTSTART", out var ds) && !ev.ContainsKey("RECURRENCE-ID"))
                {
                    var (start, allDay) = Date(ds);
                    var end = ev.TryGetValue("DTEND", out var de) ? Date(de).Item1 : start + (allDay ? TimeSpan.FromDays(1) : TimeSpan.FromHours(1));
                    var rule = (ev.GetValueOrDefault("RRULE") ?? "").Split(';').Select(p => p.Split('=')).Where(p => p.Length == 2).ToDictionary(p => p[0], p => p[1]);
                    var days = rule.TryGetValue("BYDAY", out var bd) ? bd.Split(',').Select(d => Dow(d[^2..])).ToArray() : new[] { start.DayOfWeek };
                    list.Add(new Raw(start, end - start, allDay, Text(ev.GetValueOrDefault("SUMMARY") ?? ""), Text(ev.GetValueOrDefault("LOCATION") ?? ""),
                        rule.GetValueOrDefault("FREQ") ?? "", int.TryParse(rule.GetValueOrDefault("INTERVAL"), out var iv) ? iv : 1,
                        rule.TryGetValue("UNTIL", out var u) ? Date(u).Item1 : null, int.TryParse(rule.GetValueOrDefault("COUNT"), out var c) ? c : 0, days, except));
                }
                ev = null; continue;
            }
            if (ev == null) continue;
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            var key = line[..colon].Split(';')[0];
            var val = line[(colon + 1)..];
            // TZID parameters are ignored: times are read as the PC's local time (right for local schedules).
            if (key == "EXDATE") foreach (var x in val.Split(',')) except.Add(Date(x).Item1.Date);
            else ev.TryAdd(key, val);
        }
        return list;
    }

    static (DateTime, bool) Date(string v)
    {
        v = v.Trim();
        if (v.Length == 8) return (DateTime.ParseExact(v, "yyyyMMdd", CultureInfo.InvariantCulture), true);
        bool utc = v.EndsWith('Z');
        var d = DateTime.ParseExact(v.TrimEnd('Z')[..15], "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
        return (utc ? DateTime.SpecifyKind(d, DateTimeKind.Utc).ToLocalTime() : d, false);
    }

    static string Text(string s) => s.Replace("\\n", " ").Replace("\\N", " ").Replace("\\,", ",").Replace("\\;", ";").Replace("\\\\", "\\").Trim();

    static DayOfWeek Dow(string d) => d switch { "MO" => DayOfWeek.Monday, "TU" => DayOfWeek.Tuesday, "WE" => DayOfWeek.Wednesday, "TH" => DayOfWeek.Thursday, "FR" => DayOfWeek.Friday, "SA" => DayOfWeek.Saturday, _ => DayOfWeek.Sunday };

    // ponytail: DAILY/WEEKLY only (covers class schedules); MONTHLY/YEARLY rules show just the first date — add when someone needs them.
    internal static bool Occurs(Raw r, DateTime day)
    {
        var first = r.Start.Date;
        if (day < first || r.Except.Contains(day)) return false;
        if (r.Freq is not ("DAILY" or "WEEKLY")) return day == first;
        if (r.Until is { } until && day > until.Date) return false;
        bool Hit(DateTime d) => r.Freq == "DAILY"
            ? (d - first).Days % r.Interval == 0
            : r.Days.Contains(d.DayOfWeek) && ((d - Monday(first)).Days / 7) % r.Interval == 0;
        if (!Hit(day)) return false;
        if (r.Count == 0) return true;
        int n = 0;
        for (var d = first; d <= day; d = d.AddDays(1)) if (Hit(d) && ++n > r.Count) return false;
        return true;
    }

    static DateTime Monday(DateTime d) => d.AddDays(-(((int)d.DayOfWeek + 6) % 7));
}
