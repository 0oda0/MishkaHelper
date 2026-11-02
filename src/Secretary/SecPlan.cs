using System.Text.RegularExpressions;

namespace MishkaHelper.Core;

public class SecTask
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Text { get; set; } = "";
    public DateTime? Due { get; set; }
    public bool HasTime { get; set; }
    public DateTime? DoneAt { get; set; }
    public DateTime Created { get; set; } = DateTime.Now;
}

public class AppTrigger { public string Key { get; set; } = ""; public string Text { get; set; } = ""; }

/// <summary>What a typed or dictated line means. Pure, so the phrases are covered by tests; acting on it is SecPlan.Apply.</summary>
public record Plan(string Kind, string Text, DateTime? At = null, bool HasTime = false, string Repeat = "", int LeadDays = 0, string Key = "")
{
    public bool IsTask { get; init; }
}

public static class SecPlan
{
    const RegexOptions Ru = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    static readonly (string Stem, DayOfWeek Day)[] Days =
    {
        ("понедельник", DayOfWeek.Monday), ("вторник", DayOfWeek.Tuesday), ("сред", DayOfWeek.Wednesday), ("сред", DayOfWeek.Wednesday), ("четверг", DayOfWeek.Thursday),
        ("пятниц", DayOfWeek.Friday), ("суббот", DayOfWeek.Saturday), ("воскресень", DayOfWeek.Sunday),
    };

    static readonly Regex OnOpen = new(@"^\s*когда\s+(?:открою|запущу|зайду\s+(?:на|в)|открою\s+сайт)\s+(?:сайт\s+)?([^,:]+?)\s*[,:]\s*(.+)$", Ru);
    static readonly Regex Birthday = new(@"день\s+рождени|\bдр\b|годовщин", Ru);
    static readonly Regex TaskWord = new(@"^\s*(надо|нужно|не\s+забыть|сделать|купить|сдать|позвонить|написать|оплатить|заплатить|отправить|записаться|подготовить)\b", Ru);

    static readonly Regex RepeatDay = new(@"(?:\bкаждый\s+день\b|\bежедневно\b)", Ru);
    static readonly Regex RepeatWeekDay = new(@"(?:\bкажд\w+|\bпо)\s+(понедельник\w*|вторник\w*|сред\w*|четверг\w*|пятниц\w*|суббот\w*|воскресень\w*)", Ru);
    static readonly Regex RepeatWeek = new(@"(?:\bкаждую\s+неделю\b|\bеженедельно\b)", Ru);
    static readonly Regex RepeatMonthDay = new(@"\bкажд\w+\s+(\d{1,2})(?:\s*-?\s*(?:е|го|ое|ого))?(?:\s+числ\w*)?(?=\s|$|,)", Ru);
    static readonly Regex RepeatMonth = new(@"(?:\bкаждый\s+месяц\b|\bежемесячно\b)", Ru);
    static readonly Regex RepeatYear = new(@"(?:\bкаждый\s+год\b|\bежегодно\b)", Ru);

    public static Plan Understand(string text, DateTime now)
    {
        text = text.Trim();
        var m = OnOpen.Match(text);
        if (m.Success) return new Plan("open", Reminders.Cap(m.Groups[2].Value.Trim()), Key: m.Groups[1].Value.Trim());

        if (ParseRepeat(text, now) is { } rep) return rep;

        var when = When.Parse(text, now);
        if (when != null && Birthday.IsMatch(text))
            return new Plan("repeat", Reminders.Cap(text), new DateTime(when.At.Year, when.At.Month, when.At.Day), false, "yearly", 3);
        bool task = TaskWord.IsMatch(text) || (when != null && TaskWord.IsMatch(when.Rest));
        if (when != null && when.At > now)
            return new Plan("remind", Reminders.Cap(when.Rest.Length > 0 ? Original(text, when.Rest) : text), when.At, when.HasTime) { IsTask = task };
        return task ? new Plan("task", Reminders.Cap(Regex.Replace(text, @"^\s*(надо|нужно|не\s+забыть)\s+", "", Ru))) { IsTask = true } : new Plan("note", text);
    }

    /// <summary>The date parser returns lower case without ё. Words it kept are taken back from what the person actually wrote: "Иван", "отчёт".</summary>
    internal static string Original(string written, string rest)
    {
        var want = rest.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var res = new List<string>();
        int i = 0;
        foreach (var w in written.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (i >= want.Length) break;
            if (RuText.Clean(w) == RuText.Clean(want[i])) { res.Add(w); i++; }
        }
        return i == want.Length ? string.Join(' ', res).Trim(',', '.', ';', ':', ' ') : rest;
    }

    static Plan? ParseRepeat(string text, DateTime now)
    {
        Match m;
        string kind; int weekDay = -1, monthDay = 0;
        if ((m = RepeatWeekDay.Match(text)).Success) { kind = "weekly"; var v = m.Groups[1].Value.ToLowerInvariant(); weekDay = (int)Days.First(d => v.StartsWith(d.Stem)).Day; }
        else if ((m = RepeatMonthDay.Match(text)).Success && int.Parse(m.Groups[1].Value) is >= 1 and <= 31) { kind = "monthly"; monthDay = int.Parse(m.Groups[1].Value); }
        else if ((m = RepeatDay.Match(text)).Success) kind = "daily";
        else if ((m = RepeatWeek.Match(text)).Success) kind = "weekly";
        else if ((m = RepeatMonth.Match(text)).Success) { kind = "monthly"; monthDay = now.Day; }
        else if ((m = RepeatYear.Match(text)).Success) kind = "yearly";
        else return null;

        var rest = (text[..m.Index] + " " + text[(m.Index + m.Length)..]).Trim();
        var when = When.Parse(rest, now);
        var label = when != null && when.Rest.Length > 0 ? Original(rest, when.Rest) : Regex.Replace(rest, @"\s+", " ").Trim(' ', ',', '.');
        bool hasTime = when?.HasTime == true;
        var time = hasTime ? when!.At.TimeOfDay : TimeSpan.FromHours(10);
        DateTime at;
        switch (kind)
        {
            case "daily":
                at = now.Date + time; if (at <= now) at = at.AddDays(1);
                break;
            case "weekly":
                int target = weekDay >= 0 ? weekDay : (int)now.DayOfWeek;
                at = now.Date.AddDays(((target - (int)now.DayOfWeek) + 7) % 7) + time;
                if (at <= now) at = at.AddDays(7);
                break;
            case "monthly":
                at = Clamp(now.Year, now.Month, monthDay) + time;
                if (at <= now) { var n = now.AddMonths(1); at = Clamp(n.Year, n.Month, monthDay) + time; }
                break;
            default:   // yearly needs a date to mean anything
                if (when == null) return null;
                at = when.At;
                break;
        }
        return new Plan("repeat", Reminders.Cap(label.Length > 0 ? label : text), at, hasTime, kind) { IsTask = false };
    }

    static DateTime Clamp(int y, int m, int d) => new(y, m, Math.Min(d, DateTime.DaysInMonth(y, m)));

    /// <summary>A far deadline gets early warnings: a week, three days and a day before.</summary>
    public static List<(DateTime At, string Text)> MilestonesFor(string text, DateTime due, DateTime now)
    {
        var res = new List<(DateTime, string)>();
        foreach (var d in new[] { 7, 3, 1 })
        {
            var at = due.Date.AddDays(-d).AddHours(10);
            if (at > now.AddHours(1)) res.Add((at, $"{text}: {(d == 1 ? "завтра срок" : $"до срока {d} дн.")}"));
        }
        return res;
    }

    /// <summary>The clock time a schedule line starts with or contains: "9:00 Матан", "Пара 09.30–11.05".</summary>
    public static TimeSpan? LineTime(string line)
    {
        var m = Regex.Match(line, @"(?<![\d.:])([01]?\d|2[0-3])[:.]([0-5]\d)(?![\d.])");
        return m.Success ? new TimeSpan(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), 0) : null;
    }

    /// <summary>Busy blocks stay where they are; tasks go into free gaps of 45+ minutes, in the given order, until 22:00.</summary>
    public static List<string> PlanDay(DateTime now, IEnumerable<(TimeSpan From, TimeSpan To, string Text)> busy, IEnumerable<string> tasks)
    {
        var end = TimeSpan.FromHours(22);
        var slot = TimeSpan.FromMinutes(45);
        var start = TimeSpan.FromMinutes(Math.Ceiling(Math.Max(now.TimeOfDay.TotalMinutes, 9 * 60) / 15) * 15);
        var blocks = busy.OrderBy(b => b.From).ToList();
        var todo = new Queue<string>(tasks);
        var res = new List<(TimeSpan At, string Text)>();
        var t = start;
        foreach (var b in blocks.Append((end, end, "")))
        {
            while (todo.Count > 0 && t + slot <= b.Item1) { res.Add((t, todo.Dequeue())); t += slot; }
            if (b.Item3.Length > 0) res.Add((b.Item1, b.Item3));
            if (b.Item2 > t) t = b.Item2;
        }
        var lines = res.OrderBy(x => x.At).Select(x => x.Text != "" && Regex.IsMatch(x.Text, @"^\d{1,2}[:.]\d{2}") ? x.Text : $"{x.At:hh\\:mm} {x.Text}").ToList();
        if (todo.Count > 0) lines.Add($"Не влезло в день: {todo.Count}");
        return lines;
    }

    /// <summary>Does a task talk about this program or site? Only by clear names: a Latin word or a known alias, not every word of the sentence.</summary>
    internal static bool TaskMentions(string task, string process, string title, string? url)
    {
        foreach (var w in Regex.Split(task.ToLowerInvariant(), @"[^\p{L}\p{N}.]+").Where(w => w.Length >= 2))
            if ((Aliases.ContainsKey(w) || (Regex.IsMatch(w, "^[a-z0-9.]{3,}$") && !w.All(char.IsDigit))) && Matches(w, process, title, url)) return true;
        return false;
    }

    /// <summary>Does it. The line always stays as a note too, with the program it was written in. Returns what to say back.</summary>
    public static string Apply(string text, string app)
    {
        var s = Settings.Current.Secretary;
        var now = DateTime.Now;
        var p = Understand(text, now);
        Notes.Add(app.Length > 0 ? $"[{app}] {text}" : text);
        var store = SecStore.Cur;
        string said = "Записала";
        switch (p.Kind)
        {
            case "open" when s.AppTriggers:
                store.Triggers.Add(new AppTrigger { Key = p.Key, Text = p.Text });
                said = $"Напомню, когда откроешь {p.Key}";
                break;
            case "repeat" when s.Repeats:
                var r = Reminders.Add(p.Text, p.At!.Value, p.HasTime);
                r.Repeat = p.Repeat; r.RepeatDay = p.At.Value.Day; r.LeadDays = p.LeadDays;
                Settings.Save();
                said = $"Буду напоминать {RepeatName(p.Repeat)}. Первый раз {When.Say(p.At.Value, p.HasTime, now)}";
                break;
            case "remind":
                Reminders.Add(p.Text, p.At!.Value, p.HasTime);
                said = $"Напомню {When.Say(p.At.Value, p.HasTime, now)}";
                break;
        }
        if (p.IsTask && s.Tasks)
        {
            store.Tasks.Add(new SecTask { Text = p.Text, Due = p.At, HasTime = p.HasTime });
            if (s.Milestones && p.At is { } due)
                foreach (var (at, label) in MilestonesFor(p.Text, due, now)) Reminders.Add(label, at, true);
            if (p.Kind == "task") said = "Добавила в дела";
        }
        SecStore.Save();
        Secretary.Apply();
        return said;
    }

    public static string RepeatName(string r) => r switch { "daily" => "каждый день", "weekly" => "каждую неделю", "monthly" => "каждый месяц", "yearly" => "каждый год", _ => "" };

    // ---------- on-open triggers ----------
    // People say the Russian name; windows carry the English one.
    static readonly Dictionary<string, string> Aliases = new()
    {
        ["ворд"] = "word", ["эксель"] = "excel", ["хром"] = "chrome", ["ютуб"] = "youtube", ["вк"] = "vk.com", ["вконтакте"] = "vk.com", ["фотошоп"] = "photoshop",
        ["телеграм"] = "telegram", ["дискорд"] = "discord", ["стим"] = "steam", ["госуслуги"] = "gosuslugi", ["госуслугах"] = "gosuslugi", ["почту"] = "mail", ["почта"] = "mail",
        ["кинопоиск"] = "kinopoisk", ["гитхаб"] = "github", ["блокнот"] = "notepad", ["пауэрпоинт"] = "powerpnt",
    };

    internal static bool Matches(string key, string process, string title, string? url)
    {
        var k = key.Trim().ToLowerInvariant();
        if (k.Length < 2) return false;
        var all = $"{process} {title} {url}".ToLowerInvariant();
        return all.Contains(k) || (Aliases.TryGetValue(k, out var a) && all.Contains(a));
    }

    // ---------- today's lines of a schedule page ----------
    static readonly string[] WeekWords = { "воскресень", "понедельник", "вторник", "сред", "четверг", "пятниц", "суббот" };
    static readonly string[] Months = { "январ", "феврал", "март", "апрел", "ма", "июн", "июл", "август", "сентябр", "октябр", "ноябр", "декабр" };

    static bool IsDayHeading(string line) =>
        line.Length < 40 && (WeekWords.Any(w => Regex.IsMatch(line, @"\b" + w, Ru)) || Regex.IsMatch(line, @"^(пн|вт|ср|чт|пт|сб|вс)\b", Ru) || Regex.IsMatch(line, @"^\s*\d{1,2}\.\d{1,2}(\.\d{2,4})?\s*$") || Regex.IsMatch(line, @"^\s*\d{1,2}\s+(" + string.Join("|", Months) + @")\w*", Ru));

    /// <summary>The block under the heading of that day, or null: pages differ too much to guess beyond a clear heading.</summary>
    public static List<string>? ScheduleFor(string pageText, DateTime day)
    {
        var lines = pageText.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
        var word = WeekWords[(int)day.DayOfWeek];
        var dated = new[] { day.ToString("dd.MM"), day.ToString("d.MM"), $"{day.Day} {Months[day.Month - 1]}", $"{day:dd} {Months[day.Month - 1]}" };
        var shortDay = new[] { "вс", "пн", "вт", "ср", "чт", "пт", "сб" }[(int)day.DayOfWeek];
        int start = Array.FindIndex(lines, l => l.Length < 40 && dated.Any(d => l.Contains(d, StringComparison.OrdinalIgnoreCase)));
        if (start < 0) start = Array.FindIndex(lines, l => l.Length < 40 && Regex.IsMatch(l, @"\b" + word, Ru));
        if (start < 0) start = Array.FindIndex(lines, l => l.Length < 40 && Regex.IsMatch(l, @"^" + shortDay + @"\b", Ru));
        if (start < 0) return null;
        var res = new List<string>();
        for (int i = start + 1; i < lines.Length && res.Count < 12; i++)
        {
            if (IsDayHeading(lines[i])) break;
            res.Add(lines[i]);
        }
        return res.Count > 0 ? res : null;
    }
}
