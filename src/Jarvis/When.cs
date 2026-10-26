using System.Text.RegularExpressions;

namespace MishkaHelper.Core;

/// <summary>
/// Russian time phrases → a moment: "через минут 5", "через полчаса", "завтра в 6 вечера", "в следующее воскресенье",
/// "1 ноября в 18:30", "25.10". Returns what's left of the sentence with the time words cut out.
/// Offline and deterministic; Jarvis repeats the date back, so a wrong guess is visible at once.
/// </summary>
public static class When
{
    public record Result(DateTime At, bool HasTime, string Rest);

    static readonly string[] Months = { "январ", "феврал", "март", "апрел", "ма", "июн", "июл", "август", "сентябр", "октябр", "ноябр", "декабр" };
    static readonly (string Stem, DayOfWeek Day)[] Days =
    {
        ("понедельник", DayOfWeek.Monday), ("вторник", DayOfWeek.Tuesday), ("сред", DayOfWeek.Wednesday), ("четверг", DayOfWeek.Thursday),
        ("пятниц", DayOfWeek.Friday), ("суббот", DayOfWeek.Saturday), ("воскресень", DayOfWeek.Sunday),
    };

    static int? Num(string s) => s switch
    {
        "пару" or "пара" => 2, "полчаса" => 30, "минуту" or "час" or "день" or "неделю" => 1,
        _ => Intents.Number(s),
    };

    /// <summary>Lower case, ё→е, punctuation → spaces, but keeps "." and ":" that dates and clock times need.</summary>
    static string Clean(string s) =>
        Regex.Replace(new string(s.ToLowerInvariant().Replace('ё', 'е').Select(c => char.IsLetterOrDigit(c) || c is ' ' or '.' or ':' ? c : ' ').ToArray()), @"(?<!\d)[.:]|[.:](?!\d)", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Aggregate("", (a, w) => a.Length == 0 ? w : a + " " + w);

    public static Result? Parse(string text, DateTime now)
    {
        var t = " " + Clean(text) + " ";
        Match m;

        // "через 5 минут", "через минут 5", "через полчаса", "через пару часов", "через 2 дня"
        m = Regex.Match(t, @" через (?:(\S+) )?(секунд\w*|минут\w*|полчаса|час\w*|д(?:ень|ня|ней)|недел\w*)(?: (\d+|\S+))?(?= )");
        if (m.Success)
        {
            var unit = m.Groups[2].Value;
            int? n = unit == "полчаса" ? 30 : m.Groups[1].Success ? Num(m.Groups[1].Value) : m.Groups[3].Success ? Num(m.Groups[3].Value) : null;
            n ??= 1;   // "через час", "через минуту"
            bool tailUsed = !m.Groups[1].Success && m.Groups[3].Success && Num(m.Groups[3].Value) != null;
            var span = unit.StartsWith("секунд") ? TimeSpan.FromSeconds(n.Value) : unit.StartsWith("минут") || unit == "полчаса" ? TimeSpan.FromMinutes(n.Value)
                : unit.StartsWith("час") ? TimeSpan.FromHours(n.Value) : unit.StartsWith("недел") ? TimeSpan.FromDays(7 * n.Value) : TimeSpan.FromDays(n.Value);
            var cut = tailUsed ? m.Value : m.Value[..(m.Groups[3].Success ? m.Groups[3].Index - m.Index : m.Length)];
            bool hasTime = span < TimeSpan.FromDays(1);
            return new(now + span, hasTime, Tidy(t.Remove(m.Index, cut.Length)));
        }

        DateTime? date = null;
        int cutAt = -1, cutLen = 0;
        void Cut(Match x) { if (cutAt < 0 || x.Index < cutAt) { cutLen = cutAt < 0 ? x.Length : Math.Max(cutAt + cutLen, x.Index + x.Length) - x.Index; cutAt = x.Index; } else cutLen = Math.Max(cutLen, x.Index + x.Length - cutAt); }

        // "сегодня", "завтра", "послезавтра"
        m = Regex.Match(t, @" (сегодня|послезавтра|завтра)(?= )");
        if (m.Success) { date = now.Date.AddDays(m.Groups[1].Value == "сегодня" ? 0 : m.Groups[1].Value == "завтра" ? 1 : 2); Cut(m); }

        // "в (следующее|это|ближайшее)? воскресенье" — the nearest one ahead; next week's if it's today
        if (date == null)
        {
            m = Regex.Match(t, @" (?:в|во|к|до)? ?(?:(следующ\w*|эт\w*|ближайш\w*) )?(понедельник\w*|вторник\w*|сред\w*|четверг\w*|пятниц\w*|суббот\w*|воскресень\w*)(?= )");
            if (m.Success)
            {
                var day = Days.First(d => m.Groups[2].Value.StartsWith(d.Stem)).Day;
                int ahead = ((int)day - (int)now.DayOfWeek + 7) % 7;
                if (ahead == 0) ahead = 7;
                date = now.Date.AddDays(ahead);
                Cut(m);
            }
        }

        // "1 ноября", "25 октября", "25.10", "25.10.2026"
        if (date == null)
        {
            m = Regex.Match(t, @" (\d{1,2}) (январ\w*|феврал\w*|март\w*|апрел\w*|ма[яй]\w*|июн\w*|июл\w*|август\w*|сентябр\w*|октябр\w*|ноябр\w*|декабр\w*)(?= )");
            if (m.Success)
            {
                int month = Array.FindIndex(Months, s => m.Groups[2].Value.StartsWith(s)) + 1;
                date = Future(now, int.Parse(m.Groups[1].Value), month);
                Cut(m);
            }
            else if ((m = Regex.Match(t, @" (\d{1,2})\.(\d{1,2})(?:\.(\d{2,4}))?(?= )")).Success && int.Parse(m.Groups[2].Value) is >= 1 and <= 12)
            {
                date = m.Groups[3].Success
                    ? SafeDate(int.Parse(m.Groups[3].Value) is var y && y < 100 ? 2000 + y : y, int.Parse(m.Groups[2].Value), int.Parse(m.Groups[1].Value))
                    : Future(now, int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
                Cut(m);
            }
        }

        // Time of day: "в 18:30", "в 18 30", "в 6 вечера", "в 9 утра", "к 10"
        TimeSpan? time = null;
        m = Regex.Match(t, @" (?:в|к|на) (\d{1,2})(?:[: ](\d{2}))?(?: (утра|дня|вечера|ночи))?(?= )");
        if (!m.Success) m = Regex.Match(t, @" (?:в|к) (\S+)(?: (утра|дня|вечера|ночи))(?= )");   // "в шесть вечера"
        if (m.Success)
        {
            bool word = !char.IsDigit(m.Groups[1].Value[0]);
            int? h = word ? Num(m.Groups[1].Value) : int.Parse(m.Groups[1].Value);
            int min = !word && m.Groups[2].Success && m.Groups[2].Value.All(char.IsDigit) ? int.Parse(m.Groups[2].Value) : 0;
            var part = word ? m.Groups[2].Value : m.Groups[3].Value;
            if (h is >= 0 and <= 23 && min < 60)
            {
                if (part is "вечера" or "дня" && h < 12) h += 12;
                if (part == "ночи" && h == 12) h = 0;
                time = new TimeSpan(h.Value, min, 0);
                Cut(m);
            }
        }

        if (date == null && time == null) return null;
        var at = (date ?? now.Date) + (time ?? TimeSpan.Zero);
        if (date == null && at <= now) at = at.AddDays(1);   // "в 9 утра" said at 10:00 = tomorrow
        return new(at, time != null, Tidy(t.Remove(cutAt, cutLen)));
    }

    static DateTime Future(DateTime now, int day, int month)
    {
        var d = SafeDate(now.Year, month, day);
        return d < now.Date ? SafeDate(now.Year + 1, month, day) : d;
    }

    static DateTime SafeDate(int y, int m, int d) => new(y, m, Math.Clamp(d, 1, DateTime.DaysInMonth(y, m)));

    /// <summary>Drop filler around the task: "позарез надо сделать лабу" → "сделать лабу".</summary>
    static string Tidy(string s)
    {
        s = Regex.Replace(" " + s + " ", @" (позарез|кровь из носа|обязательно|срочно|надо|нужно|не забыть|не забудь|мне|пожалуйста) ", " ");
        s = Regex.Replace(s, @" (позарез|надо|нужно|обязательно) ", " ");
        return Regex.Replace(s, @"\s+", " ").Trim();
    }

    /// <summary>"в воскресенье, 1 ноября" / "сегодня в 18:30" — said back so a wrong guess is caught.</summary>
    public static string Say(DateTime at, bool hasTime, DateTime now)
    {
        var ru = new System.Globalization.CultureInfo("ru-RU");
        string day = at.Date == now.Date ? "сегодня" : at.Date == now.Date.AddDays(1) ? "завтра" : at.Date == now.Date.AddDays(2) ? "послезавтра"
            : (at - now).TotalDays < 7 ? $"в {at.ToString("dddd", ru)}, {at.ToString("d MMMM", ru)}" : at.ToString("d MMMM", ru);
        return hasTime ? $"{day} в {at:H:mm}" : day;
    }
}
