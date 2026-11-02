using System.IO;
using System.Net;
using MishkaHelper.Core;
using Xunit;

namespace MishkaHelper.Tests;

public class ParsingTests
{
    [Theory]
    [InlineData("громкость тридцать пять", 35)]
    [InlineData("20 минут", 20)]
    [InlineData("сто", 100)]
    public void Number(string s, int expected) => Assert.Equal(expected, Intents.Number(s));

    [Fact] public void NumberNone() => Assert.Null(Intents.Number("включи свет"));

    [Fact] public void Clean() => Assert.Equal("джарвис включи wi fi на 50%", Ears.Clean("Джарвис, включи Wi-Fi на 50%!"));

    [Theory]
    [InlineData("джарвис включи музыку", 0)]
    [InlineData("эй джервис пауза", 1)]
    [InlineData("включи музыку", -1)]
    public void WakeWord(string text, int index)
    {
        Settings.Current.Jarvis.WakeWord = "джарвис";
        Assert.Equal(index, Ears.WakeIndex(text));
    }

    [Fact] public void Distance() { Assert.Equal(1, Ears.Distance("джервис", "джарвис")); Assert.Equal(0, Ears.Distance("abc", "abc")); }

    [Fact]
    public void Hotkeys()
    {
        Assert.True(MishkaHelper.Core.Hotkeys.TryParse("Ctrl+Alt+R", out var mods, out var vk));
        Assert.Equal(0x52u, vk);
        Assert.NotEqual(0u, mods);
        Assert.False(MishkaHelper.Core.Hotkeys.TryParse("R", out _, out _));          // no modifier
        Assert.False(MishkaHelper.Core.Hotkeys.TryParse("Ctrl+Nope", out _, out _));
        Assert.Equal("Ctrl+Alt+=", MishkaHelper.Core.Hotkeys.Pretty("Ctrl+Alt+OemPlus"));
    }

    [Theory]
    [InlineData("MishkaHelper-Setup-0.5.0.exe", "Setup-", ".exe", "MishkaHelper-", true)]
    [InlineData("MishkaHelper-Lite-Setup-0.5.0.exe", "Setup-", ".exe", "MishkaHelper-", false)]
    [InlineData("MishkaHelper-Secretary-Setup-1.2.0.exe", "Setup-", ".exe", "MishkaHelper-", false)]
    [InlineData("MishkaHelper-Jarvis-Portable-1.2.0.zip", "Portable-", ".zip", "MishkaHelper-", false)]
    [InlineData("MishkaHelper-Lite-Setup-0.5.0.exe", "Setup-", ".exe", "MishkaHelper-Lite-", true)]
    [InlineData("MishkaHelper-Setup-0.5.0.exe", "Setup-", ".exe", "MishkaHelper-Lite-", false)]
    [InlineData("MishkaHelper-Secretary-Setup-1.2.0.exe", "Setup-", ".exe", "MishkaHelper-Secretary-", true)]
    [InlineData("MishkaHelper-Jarvis-Setup-1.2.0.exe", "Setup-", ".exe", "MishkaHelper-Secretary-", false)]
    [InlineData("MishkaHelper-Portable-0.5.0.zip", "Portable-", ".zip", "MishkaHelper-", true)]
    [InlineData("MishkaHelper-Portable-0.5.0.zip", "Setup-", ".exe", "MishkaHelper-", false)]
    public void UpdaterAsset(string name, string kind, string ext, string prefix, bool match) => Assert.Equal(match, Updater.AssetMatches(name, kind, ext, prefix));

    [Theory]
    [InlineData("192.168.1.5", true)]
    [InlineData("10.0.0.2", true)]
    [InlineData("172.20.1.1", true)]
    [InlineData("172.32.1.1", false)]
    [InlineData("127.0.0.1", true)]
    [InlineData("8.8.8.8", false)]
    [InlineData("::ffff:192.168.0.10", true)]
    public void RemotePrivateOnly(string ip, bool ok) => Assert.Equal(ok, Remote.IsPrivate(IPAddress.Parse(ip)));

    [Fact]
    public void YouTubeSubsFromPageText()
    {
        var text = "Подписки\nВсе\nВсё о Linux\n@vsyo.o.linux•120 тыс. подписчиков\nОписание канала\nМуд Мишка\n@mud_mishka\n1 млн подписчиков\n@lonely\n";
        var subs = Remote.ParseSubs(text);
        Assert.Equal(2, subs.Count);
        Assert.Equal(("Всё о Linux", "https://www.youtube.com/@vsyo.o.linux"), subs[0]);
        Assert.Equal(("Муд Мишка", "https://www.youtube.com/@mud_mishka"), subs[1]);
    }
}

public class SecretaryTests
{
    static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0);

    [Theory]
    [InlineData("Сдать отчёт до пятницы, срок завтра в 18:00", true)]
    [InlineData("Встреча с куратором завтра в 15:00", true)]
    [InlineData("Оплатить квартиру 25.11", true)]
    [InlineData("Завтра будет солнечно и тепло", false)]            // a date without business words is not an offer
    [InlineData("Сдать отчёт как можно скорее", false)]             // no date
    [InlineData("срок", false)]
    public void Deadlines(string text, bool found) => Assert.Equal(found, Secretary.FindDeadline(text, Now) != null);

    [Fact]
    public void DeadlinePastIsIgnored() => Assert.Null(Secretary.FindDeadline("Экзамен был 01.11.2020", Now));

    [Theory]
    [InlineData("https://lms.example.ru/schedule?g=12", true)]
    [InlineData("vk.com/video", true)]
    [InlineData("example.org", true)]
    [InlineData("как приготовить борщ", false)]
    [InlineData("hello", false)]
    public void BrowserAddress(string v, bool url) => Assert.Equal(url, SecCapture.LooksLikeUrl(v));

    [Theory]
    [InlineData("Расписание группы — Google Chrome", "chrome", "Расписание группы")]
    [InlineData("Журнал - Яндекс Браузер", "browser", "Журнал")]
    [InlineData("Документ1 - Word", "winword", "Документ1 - Word")]
    [InlineData("", "chrome", "chrome")]
    public void Titles(string title, string app, string want) => Assert.Equal(want, SecCapture.CleanTitle(title, app));

    [Theory]
    [InlineData("Расписание группы", true)]
    [InlineData("Смешные коты", false)]
    public void Daily(string s, bool yes) => Assert.Equal(yes, SecCapture.LooksDaily(s));

    [Theory]
    [InlineData("отчёт.PDF", "Документы")]
    [InlineData("фото.jpg", "Картинки")]
    [InlineData("setup.exe", "Установщики")]
    [InlineData("фильм.mkv.crdownload", null)]   // still downloading: never touched
    [InlineData("data.xyz", null)]
    public void DownloadSort(string name, string? category) => Assert.Equal(category, SecDownloads.Sort(name)?.Category);

    [Theory]
    [InlineData("me@yandex.ru", "imap.yandex.ru")]
    [InlineData("me@bk.ru", "imap.mail.ru")]
    [InlineData("me@gmail.com", "imap.gmail.com")]
    [InlineData("me@company.org", null)]
    public void MailServer(string email, string? host) => Assert.Equal(host, SecMail.Provider(email)?.Host);

    [Fact]
    public void VoiceLevel()
    {
        Assert.Equal(0, SecVoice.Level(new byte[400], 400));
        var loud = new byte[400];
        for (int i = 0; i + 1 < 400; i += 2) BitConverter.TryWriteBytes(loud.AsSpan(i), (short)(i % 4 == 0 ? 8000 : -8000));
        Assert.True(SecVoice.Level(loud, 400) > 500);
    }

    [Fact]
    public void SupportErrorsAreReadable()
    {
        Assert.Contains("Подожди", Support.Friendly(400, "{\"message\":\"rate_limit\"}"));
        Assert.Contains("длинное", Support.Friendly(400, "{\"message\":\"too_long\"}"));
        Assert.Contains("500", Support.Friendly(500, "boom"));
    }
}

public class SecPlanTests
{
    static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0);   // Monday
    static Plan P(string s) => SecPlan.Understand(s, Now);

    [Fact]
    public void WeeklyOnDay()
    {
        var p = P("каждый вторник в 18:00 тренировка");
        Assert.Equal("repeat", p.Kind); Assert.Equal("weekly", p.Repeat);
        Assert.Equal(new DateTime(2026, 11, 3, 18, 0, 0), p.At); Assert.True(p.HasTime); Assert.Equal("Тренировка", p.Text);
    }

    [Fact]
    public void WeeklyToday_PastTime_NextWeek() => Assert.Equal(new DateTime(2026, 11, 9, 9, 0, 0), P("по понедельникам в 9:00 планёрка").At);

    [Fact]
    public void MonthlyOnDay()
    {
        var p = P("каждое 5-е оплатить интернет");
        Assert.Equal("monthly", p.Repeat); Assert.Equal(new DateTime(2026, 11, 5, 10, 0, 0), p.At); Assert.Equal("Оплатить интернет", p.Text);
    }

    [Fact] public void MonthlyPassed() => Assert.Equal(11 + 1, P("каждое 1 число отчёт").At!.Value.Month);
    [Fact] public void Daily() => Assert.Equal(new DateTime(2026, 11, 3, 9, 0, 0), P("каждый день в 9:00 зарядка").At);

    [Fact]
    public void BirthdayIsYearlyWithWarning()
    {
        var p = P("день рождения мамы 12 мая");
        Assert.Equal("yearly", p.Repeat); Assert.Equal(3, p.LeadDays); Assert.Equal(new DateTime(2027, 5, 12), p.At); Assert.False(p.HasTime);
    }

    [Fact]
    public void OnOpen()
    {
        var p = P("Когда открою Word, проверить титульник");
        Assert.Equal("open", p.Kind); Assert.Equal("Word", p.Key); Assert.Equal("Проверить титульник", p.Text);
        Assert.Equal("Госуслуги", P("когда зайду на Госуслуги: проверить штраф").Key);
    }

    [Fact]
    public void Tasks()
    {
        var t = P("надо сдать отчёт");
        Assert.Equal("task", t.Kind); Assert.True(t.IsTask); Assert.Equal("Сдать отчёт", t.Text);
        var d = P("завтра в 18:00 сдать отчёт");
        Assert.Equal("remind", d.Kind); Assert.True(d.IsTask);
        var r = P("завтра в 18:00 концерт");
        Assert.Equal("remind", r.Kind); Assert.False(r.IsTask);
        Assert.Equal("note", P("пароль от вайфая на холодильнике").Kind);
    }

    [Theory]
    [InlineData("word", "WINWORD", "Документ1 - Word", null, true)]
    [InlineData("ворд", "WINWORD", "Документ1 - Word", null, true)]
    [InlineData("госуслуги", "chrome", "Главная", "https://www.gosuslugi.ru/", true)]
    [InlineData("word", "chrome", "Коты", "https://cats.ru", false)]
    public void TriggerMatch(string key, string process, string title, string? url, bool yes) => Assert.Equal(yes, SecPlan.Matches(key, process, title, url));

    [Fact]
    public void RepeatingSkipsMissedAndKeepsDay()
    {
        var weekly = new Reminder { At = new DateTime(2026, 10, 1, 18, 0, 0), HasTime = true, Repeat = "weekly" };
        Assert.True(Reminders.NextAfter(weekly, Now) > Now);
        Assert.Equal(DayOfWeek.Thursday, Reminders.NextAfter(weekly, Now).DayOfWeek);
        var m = new Reminder { At = new DateTime(2027, 1, 31, 10, 0, 0), HasTime = true, Repeat = "monthly", RepeatDay = 31 };
        Assert.Equal(new DateTime(2027, 2, 28, 10, 0, 0), Reminders.NextAfter(m, new DateTime(2027, 2, 1)));
        m.At = new DateTime(2027, 2, 28, 10, 0, 0);
        Assert.Equal(new DateTime(2027, 3, 31, 10, 0, 0), Reminders.NextAfter(m, new DateTime(2027, 3, 1)));
    }

    [Fact]
    public void ScheduleBlockOfThatDay()
    {
        var page = "Расписание группы\nПонедельник\n9:00 Матан, ауд. 312\n10:45 Физика\nВторник\n9:00 История\nСреда\nНет занятий";
        Assert.Equal(new[] { "9:00 Матан, ауд. 312", "10:45 Физика" }, SecPlan.ScheduleFor(page, Now));
        Assert.Equal(new[] { "9:00 История" }, SecPlan.ScheduleFor(page, Now.AddDays(1)));
        Assert.Equal(new[] { "Пара 1" }, SecPlan.ScheduleFor("3 ноября\nПара 1\n4 ноября\nПара 2", Now.AddDays(1)));
        Assert.Null(SecPlan.ScheduleFor("Просто текст без дней", Now));
    }

    [Theory]
    [InlineData("Яндекс <noreply@yandex.ru>", false, true)]
    [InlineData("Иван <ivan@mail.ru>", true, true)]
    [InlineData("Иван <ivan@mail.ru>", false, false)]
    public void Newsletter(string from, bool unsub, bool bulk) => Assert.Equal(bulk, SecMail.IsBulk(from, unsub));

    [Fact]
    public void RuleFolders()
    {
        Assert.True(SecDownloads.FolderAllowed(@"C:\Users\me\Documents\Учёба", @"C:\Users\me"));
        Assert.False(SecDownloads.FolderAllowed(@"C:\Windows\System32", @"C:\Users\me"));
        Assert.False(SecDownloads.FolderAllowed(@"C:\Users\me\..\..\Windows", @"C:\Users\me"));
        Assert.False(SecDownloads.FolderAllowed(@"C:\Users\me2\Documents", @"C:\Users\me"));
        Assert.Equal("лаба", SecDownloads.RuleFor("Лаба3.pdf", new[] { new DownloadRule { Word = "лаба", Folder = "x" } })?.Word);
        Assert.Null(SecDownloads.RuleFor("кот.jpg", new[] { new DownloadRule { Word = "лаба", Folder = "x" } }));
    }
}

public class BackupTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "mishka-backup-" + Guid.NewGuid().ToString("N"));
    string Src => Path.Combine(_dir, "src");
    string Dst => Path.Combine(_dir, "dst");
    string Zip => Path.Combine(_dir, "b.zip");

    public BackupTests()
    {
        Directory.CreateDirectory(Src);
        File.WriteAllText(Path.Combine(Src, "settings.json"), "{\"Osd\":false}");
        File.WriteAllText(Path.Combine(Src, "secretary.json"), "{\"Tasks\":[]}");
        File.WriteAllText(Path.Combine(Src, "jarvis-notes.json"), "[]");
        File.WriteAllText(Path.Combine(Src, "log.txt"), "not for the backup");
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void RoundTripKeepsNotesAndSecretary()
    {
        Backup.Make(Src, Zip);
        Assert.Equal(3, Backup.Restore(Zip, Dst));
        Assert.Equal("{\"Tasks\":[]}", File.ReadAllText(Path.Combine(Dst, "secretary.json")));
        Assert.True(File.Exists(Path.Combine(Dst, "jarvis-notes.json")));
        Assert.False(File.Exists(Path.Combine(Dst, "log.txt")));
    }

    [Fact]
    public void OldBareSettingsFileStillWorks()
    {
        Assert.Equal(1, Backup.Restore(Path.Combine(Src, "settings.json"), Dst));
        Assert.True(File.Exists(Path.Combine(Dst, "settings.json")));
    }

    [Fact]
    public void CraftedArchiveWritesNothingOutside()
    {
        using (var z = System.IO.Compression.ZipFile.Open(Zip, System.IO.Compression.ZipArchiveMode.Create))
        {
            foreach (var name in new[] { "settings.json", "..\\evil.json", "../evil.json", "evil.exe" })
                using (var w = new StreamWriter(z.CreateEntry(name).Open())) w.Write("{}");
        }
        Assert.Equal(1, Backup.Restore(Zip, Dst));
        Assert.False(File.Exists(Path.Combine(_dir, "evil.json")));
        Assert.False(File.Exists(Path.Combine(Dst, "evil.exe")));
    }

    [Fact]
    public void BadBackupChangesNothing()
    {
        using (var z = System.IO.Compression.ZipFile.Open(Zip, System.IO.Compression.ZipArchiveMode.Create))
        {
            using (var w = new StreamWriter(z.CreateEntry("settings.json").Open())) w.Write("{}");
            using (var w = new StreamWriter(z.CreateEntry("secretary.json").Open())) w.Write("broken{");
        }
        Assert.ThrowsAny<Exception>(() => Backup.Restore(Zip, Dst));
        Assert.False(File.Exists(Path.Combine(Dst, "settings.json")));
    }

    [Fact]
    public void ForeignZipIsRefused()
    {
        using (var z = System.IO.Compression.ZipFile.Open(Zip, System.IO.Compression.ZipArchiveMode.Create))
            using (var w = new StreamWriter(z.CreateEntry("photo.txt").Open())) w.Write("x");
        Assert.ThrowsAny<Exception>(() => Backup.Restore(Zip, Dst));
    }
}

public class SecDeskTests
{
    static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0);   // Monday

    [Theory]
    [InlineData("что завтра?", "agenda")]
    [InlineData("что у меня в пятницу", "agenda")]
    [InlineData("расписание на завтра", "schedule")]
    [InlineData("где расписание", "schedule")]
    [InlineData("какие пары завтра?", "schedule")]
    [InlineData("что с почтой?", "mail")]
    [InlineData("перенеси отчёт на пятницу", "move")]
    [InlineData("сделал отчёт", "done")]
    [InlineData("где договор", "find")]
    [InlineData("куда я сохранил договор?", "find")]
    [InlineData("что просрочено", "overdue")]
    [InlineData("какой план", "plan")]
    [InlineData("завтра в 6 тренировка", "note")]
    [InlineData("надо купить молоко", "note")]
    [InlineData("обновить расписание в журнале группы", "note")]
    public void Routes(string text, string kind) => Assert.Equal(kind, SecAsk.Route(text).Kind);

    class T { public string Text = ""; }

    [Fact]
    public void BestMatchByStems()
    {
        var items = new[] { new T { Text = "Сдать отчёт по физике" }, new T { Text = "Купить молоко" } };
        Assert.Equal("Сдать отчёт по физике", SecAsk.Best("отчет", items, t => t.Text)?.Text);
        Assert.Equal("Купить молоко", SecAsk.Best("молока", items, t => t.Text)?.Text);
        Assert.Null(SecAsk.Best("концерт", items, t => t.Text));
    }

    static SecContext.Hint? H(string process, string title = "", string? url = null, bool full = false, string[]? tasks = null, Dictionary<string, string>? notes = null, RememberedItem[]? pages = null) =>
        SecContext.For(process, title, url, full, notes ?? new(), pages ?? Array.Empty<RememberedItem>(), tasks ?? Array.Empty<string>());

    [Theory]
    [InlineData("explorer")]
    [InlineData("Taskmgr")]
    [InlineData("CalculatorApp")]
    [InlineData("MishkaHelper")]
    public void SystemWindowsStayQuiet(string p) => Assert.Null(H(p, "x", null, false, new[] { "explorer x taskmgr" }, new() { [p.ToLowerInvariant()] = "заметка" }));

    [Fact]
    public void GamesStayQuiet() => Assert.Null(H("game", "Игра", null, true, null, new() { ["game"] = "заметка" }));

    [Fact]
    public void NoteComesFirst() => Assert.StartsWith("Заметка", H("winword", "Док", null, false, new[] { "проверить word" }, new() { ["winword"] = "поля 2 см" })!.Text);

    [Fact]
    public void SiteIsRecognised()
    {
        var page = new RememberedItem { Title = "Расписание", Target = "https://lk.mtuci.ru/student/schedule" };
        Assert.Contains("Расписание", H("msedge", "ЛК", "https://lk.mtuci.ru/student", false, null, null, new[] { page })!.Text);
        Assert.Null(H("msedge", "Коты", "https://cats.ru/", false, null, null, new[] { page }));
    }

    [Fact]
    public void TaskAboutThisProgram()
    {
        Assert.Contains("титульник", H("WINWORD", "Документ1 - Word", null, false, new[] { "Проверить титульник в ворд" })!.Text);
        Assert.Null(H("notepad", "Безымянный", null, false, new[] { "Сдать отчёт по физике" }));
    }

    [Fact]
    public void FarDeadlineGetsWarnings()
    {
        var m = SecPlan.MilestonesFor("Курсовая", new DateTime(2026, 11, 20), Now);
        Assert.Equal(new[] { new DateTime(2026, 11, 13, 10, 0, 0), new DateTime(2026, 11, 17, 10, 0, 0), new DateTime(2026, 11, 19, 10, 0, 0) }, m.Select(x => x.At));
        Assert.Contains("завтра", m[2].Text);
        Assert.Empty(SecPlan.MilestonesFor("Скоро", new DateTime(2026, 11, 3), Now));
    }

    [Theory]
    [InlineData("9:00 Матан", 9, 0)]
    [InlineData("Пара 09.30–11.05 Физика", 9, 30)]
    [InlineData("Ауд. 312", -1, 0)]
    [InlineData("25.11.2026 экзамен", -1, 0)]
    public void LineTimes(string line, int h, int m) => Assert.Equal(h < 0 ? null : new TimeSpan(h, m, 0), SecPlan.LineTime(line));

    [Fact]
    public void PlanFitsTasksAroundLessons()
    {
        var busy = new[] { (new TimeSpan(13, 0, 0), new TimeSpan(14, 30, 0), "13:00 Матан") };
        var plan = SecPlan.PlanDay(Now, busy, new[] { "Отчёт", "Молоко", "Лаба" });
        Assert.Equal(new[] { "12:00 Отчёт", "13:00 Матан", "14:30 Молоко", "15:15 Лаба" }, plan);
    }

    [Fact]
    public void PlanSaysWhatDidNotFit()
    {
        var late = new DateTime(2026, 11, 2, 21, 30, 0);
        Assert.Equal(new[] { "Не влезло в день: 1" }, SecPlan.PlanDay(late, Array.Empty<(TimeSpan, TimeSpan, string)>(), new[] { "Отчёт" }));
    }

    [Fact]
    public void ScheduleWithShortDayNames()
    {
        Assert.Equal(new[] { "9:00 История" }, SecPlan.ScheduleFor("Пн\n9:00 Матан\nВт\n9:00 История\nСр\nНет", Now.AddDays(1)));
        Assert.Equal(new[] { "Физика" }, SecPlan.ScheduleFor("Понедельник, 2 ноября\nФизика\nВторник, 3 ноября\nХимия", Now));
    }

    [Fact]
    public void HabitNeedsThreeDays()
    {
        var log = new Dictionary<string, List<string>>();
        var asked = new HashSet<string>();
        var d = new DateTime(2026, 11, 3, 18, 10, 0);
        SecHabits.Note("Discord", d, log); SecHabits.Note("Discord", d.AddMinutes(5), log);
        Assert.Empty(SecHabits.Found(log, asked, d));
        SecHabits.Note("Discord", d.AddDays(7), log); SecHabits.Note("Discord", d.AddDays(14), log);
        var found = SecHabits.Found(log, asked, d.AddDays(14)).ToList();
        Assert.Equal(new[] { "2|18|discord" }, found);
        asked.Add(found[0]);
        Assert.Empty(SecHabits.Found(log, asked, d.AddDays(14)));
    }

    [Fact]
    public void ReplyDraftOpensInTheirOwnMail()
    {
        var y = SecMail.DraftUrl("https://mail.yandex.ru", "a@b.ru", "Отчёт", "Иван");
        Assert.StartsWith("https://mail.yandex.ru/compose?to=a%40b.ru&subject=Re%3A", y);
        Assert.StartsWith("mailto:", SecMail.DraftUrl("", "a@b.ru", "Re: x", "Иван"));
        Assert.DoesNotContain("Re%3A%20Re", SecMail.DraftUrl("", "a@b.ru", "Re: x", "Иван"));
    }

    [Fact]
    public void WeekCheckForNextWeekPress()
    {
        Assert.True(SecSchedule.SameWeek(new DateTime(2026, 11, 8), Now));
        Assert.False(SecSchedule.SameWeek(new DateTime(2026, 11, 9), Now));
    }
}

public class FullScreenTests
{
    static readonly (int, int, int, int) Screen = (0, 0, 1920, 1080);

    [Fact] public void GameOrVideo() => Assert.True(Focus.Fills(false, (0, 0, 1920, 1080), Screen));
    [Fact] public void NormalWindow() => Assert.False(Focus.Fills(false, (100, 100, 900, 700), Screen));
    [Fact] public void MaximizedAboveTaskbar() => Assert.False(Focus.Fills(true, (-8, -8, 1928, 1048), Screen));
    [Fact] public void MaximizedWithAutoHiddenTaskbarIsNotFullScreen() => Assert.False(Focus.Fills(true, (-8, -8, 1928, 1088), Screen));
    [Fact] public void OtherMonitorIsCompared() => Assert.False(Focus.Fills(false, (0, 0, 1920, 1080), (1920, 0, 3840, 1080)));
}

public class ImprovementTests
{
    static readonly DateTime Now = new(2026, 11, 2, 12, 0, 0);

    [Fact] public void TextKeepsCapitalsAndYo() => Assert.Equal("Сдать отчёт Ивану", SecPlan.Understand("завтра в 18:00 сдать отчёт Ивану", Now).Text);
    [Fact] public void RepeatKeepsCapitals() => Assert.Equal("Звонок Ивану", SecPlan.Understand("каждый вторник в 18:00 Звонок Ивану", Now).Text);
    [Fact] public void FillerWordsStayOut() => Assert.Equal("Сдать лабу", SecPlan.Understand("завтра позарез надо сдать лабу", Now).Text);
    [Fact] public void OriginalFallsBack() => Assert.Equal("другой текст", SecPlan.Original("совсем не то", "другой текст"));

    [Theory]
    [InlineData("a.png", 1000, true)]
    [InlineData("a.JPG", 1000, true)]
    [InlineData("a.exe", 1000, false)]
    [InlineData("a.svg", 1000, false)]
    [InlineData("a.png", 6 * 1024 * 1024, false)]
    public void SupportPictures(string name, long size, bool ok) => Assert.Equal(ok, Support.PictureProblem(name, size) == null);
}

public class LayoutFixTests
{
    [Theory]
    [InlineData(0x25, 1u)]   // left
    [InlineData(0x2E, 1u)]   // delete
    [InlineData(0x43, 0u)]   // C
    [InlineData(0x11, 0u)]   // ctrl
    public void ArrowsAreExtendedKeys(int vk, uint flag) => Assert.Equal(flag, InputSim.ExtendedFlag((ushort)vk));

    [Theory]
    [InlineData("ghbdtn", "привет")]
    [InlineData("Ghbdtn", "Привет")]
    [InlineData("GHBDTN", "ПРИВЕТ")]
    [InlineData("руддщ", "hello")]
    [InlineData("Руддщ", "Hello")]
    [InlineData("ghbdtn vbh", "привет мир")]
    [InlineData("rfr ltkf?", "как дела,")]
    [InlineData("cgfcb,j", "спасибо")]
    [InlineData("\\\\", "\\\\")]
    public void Converts(string from, string to) => Assert.Equal(to, LayoutFix.Convert(from) ?? from);

    [Theory]
    [InlineData("12345")]
    [InlineData("  ")]
    [InlineData("!!!")]
    public void NoLettersNothingToDo(string s) => Assert.Null(LayoutFix.Convert(s));

    [Fact] public void BothWaysComeBack() => Assert.Equal("ghbdtn", LayoutFix.Convert(LayoutFix.Convert("ghbdtn")!));
    [Fact] public void KeyboardFollowsTheText() { Assert.True(LayoutFix.EndsRussian("привет")); Assert.False(LayoutFix.EndsRussian("hello")); }
    [Fact] public void WholeLineCopyIsNotASelection() { Assert.True(LayoutFix.IsLineCopy("строка\r\n")); Assert.False(LayoutFix.IsLineCopy("слово")); }
}

public class AutoLayoutTests
{
    [Theory]
    [InlineData("ghbdtn", "привет")]
    [InlineData("Ghbdtn", "Привет")]
    [InlineData("cgfcb,j", "спасибо")]
    [InlineData("hfcgbcfybt", "расписание")]
    [InlineData("pfdnhf", "завтра")]
    [InlineData("hf,jnf", "работа")]
    [InlineData("rjvgm.nth", "компьютер")]
    [InlineData("ctujlyz", "сегодня")]
    [InlineData("руддщ", "hello")]
    [InlineData("цштвщцы", "windows")]
    [InlineData("пщщвнуфк", "goodyear")]
    public void WrongLayoutIsFixed(string typed, string want) => Assert.Equal(want, AutoLayout.Decide(typed));

    [Theory]
    // real words stay, in either language
    [InlineData("привет")] [InlineData("расписание")] [InlineData("компьютер")] [InlineData("объект")] [InlineData("Иван")] [InlineData("щётка")] [InlineData("вообще")] [InlineData("здравствуйте")]
    [InlineData("hello")] [InlineData("windows")] [InlineData("github")] [InlineData("Telegram")] [InlineData("docker")] [InlineData("python")] [InlineData("mishka")] [InlineData("privet")] [InlineData("https")] [InlineData("json")]
    // short, acronyms, digits, mixed: not worth guessing
    [InlineData("git")] [InlineData("вк")] [InlineData("HTML")] [InlineData("МТУСИ")] [InlineData("win11")] [InlineData("abc1")] [InlineData("kotlin")] [InlineData("qwerty")]
    public void RightWordsStay(string typed) => Assert.Null(AutoLayout.Decide(typed));

    [Theory]
    [InlineData("йогурт")]
    [InlineData("ёлка")]
    [InlineData("хрущёв")]
    [InlineData("гжель")]
    [InlineData("мгла")]
    [InlineData("ткань")]
    [InlineData("взгляд")]
    [InlineData("вздрогнуть")]
    [InlineData("счастье")]
    [InlineData("пчела")]
    [InlineData("льдина")]
    [InlineData("шхуна")]
    [InlineData("чтобы")]
    [InlineData("подъезжать")]
    [InlineData("съешь")]
    [InlineData("фейерверк")]
    [InlineData("аэропорт")]
    [InlineData("дуэт")]
    [InlineData("пьеса")]
    [InlineData("вьюга")]
    [InlineData("тьма")]
    [InlineData("обьём")]
    [InlineData("вьетнам")]
    [InlineData("мгновение")]
    [InlineData("ящерица")]
    [InlineData("жюри")]
    [InlineData("брошюра")]
    [InlineData("парашют")]
    [InlineData("экзамен")]
    [InlineData("щавель")]
    [InlineData("эксперимент")]
    [InlineData("кофе")]
    [InlineData("метро")]
    [InlineData("такси")]
    [InlineData("шоссе")]
    [InlineData("кенгуру")]
    [InlineData("интервью")]
    [InlineData("ютуб")]
    [InlineData("гугл")]
    [InlineData("дискорд")]
    [InlineData("телеграм")]
    [InlineData("виндовс")]
    [InlineData("кринж")]
    [InlineData("yandex")]
    [InlineData("kubernetes")]
    [InlineData("nginx")]
    [InlineData("postgres")]
    [InlineData("linux")]
    [InlineData("ubuntu")]
    [InlineData("xbox")]
    [InlineData("steam")]
    [InlineData("wifi")]
    [InlineData("ozon")]
    [InlineData("avito")]
    [InlineData("webp")]
    [InlineData("photoshop")]
    [InlineData("excel")]
    [InlineData("word")]
    [InlineData("outlook")]
    [InlineData("chrome")]
    [InlineData("firefox")]
    [InlineData("rhythm")]
    [InlineData("strength")]
    [InlineData("twelfth")]
    [InlineData("sphinx")]
    [InlineData("lynx")]
    [InlineData("crypt")]
    [InlineData("psychology")]
    [InlineData("schnitzel")]
    [InlineData("nvidia")]
    [InlineData("intel")]
    [InlineData("acer")]
    [InlineData("lenovo")]
    [InlineData("samsung")]
    [InlineData("xiaomi")]
    [InlineData("huawei")]
    [InlineData("android")]
    [InlineData("iphone")]
    [InlineData("gmail")]
    [InlineData("facebook")]
    [InlineData("instagram")]
    [InlineData("tiktok")]
    [InlineData("spotify")]
    [InlineData("netflix")]
    public void UncommonWordsStay(string typed) => Assert.Null(AutoLayout.Decide(typed));

    [Theory]
    [InlineData(0x47u, false, 'g')] [InlineData(0x47u, true, 'G')] [InlineData(0xBCu, false, ',')] [InlineData(0xBEu, false, '.')] [InlineData(0xDBu, false, '[')] [InlineData(0x31u, false, '\0')]
    public void PhysicalKeys(uint vk, bool shift, char c) => Assert.Equal(c, AutoLayout.KeyChar(vk, shift));

    [Fact]
    public void NotesWithoutMarkup()
    {
        var p = Updater.PlainNotes("## Что нового в 1.4\n\n**Секретарша**\n- **Кнопка:** всегда `рядом`\n* ещё");
        Assert.Equal("Секретарша\n• Кнопка: всегда рядом\n• ещё", p);
        Assert.EndsWith("…", Updater.PlainNotes("1\n2\n3", 2));
    }

    [Fact]
    public void WeekSummaryCountsHonestly()
    {
        var now = new DateTime(2026, 11, 8, 20, 0, 0);
        var l = Secretary.WeekLog(now, new[]
        {
            new SecTask { DoneAt = now.AddDays(-1) }, new SecTask { DoneAt = now.AddDays(-9) },
            new SecTask { Due = now.AddDays(-2) }, new SecTask { Due = now.AddDays(3) }, new SecTask { Due = now.AddDays(30) },
        });
        Assert.Equal("За неделю сделано: 1, просрочено: 1", l[0]);
        Assert.Equal("На следующей неделе дел со сроком: 1", l[1]);
    }

    [Fact]
    public void BackupFromToolsPageAlsoRefusesStrangeFiles() => Assert.Contains("settings.json", Backup.Files);
}

public class AutoLayoutRecallTests
{
    readonly Xunit.Abstractions.ITestOutputHelper _o;
    public AutoLayoutRecallTests(Xunit.Abstractions.ITestOutputHelper o) => _o = o;

    const string Ru = "как что это кто где так там нет все для мне уже вот при про над под без или дела есть будет была было который когда только может очень после нужно надо потом здесь больше почему потому этого если чтобы тоже всегда никогда сейчас теперь думаю знаю хочу могу буду делать работа сегодня завтра вчера человек время жизнь день рука глаз голова дверь стол город страна мир вопрос ответ слово место лицо друг дом сторона часть деньги машина вода ночь утро вечер неделя месяц привет спасибо пожалуйста хорошо плохо нормально понятно конечно наверное можно нельзя давай пойдем пошли ладно окей короче вообще кстати просто реально очень сильно быстро медленно большой маленький новый старый первый последний другой каждый любой один два три четыре пять шесть семь восемь девять десять программа компьютер телефон сообщение письмо документ файл папка отчет задание экзамен лекция университет преподаватель расписание занятие группа студент проект задача ошибка проблема решение настройка обновление секретарша заметка напоминание скачать установить запустить открыть закрыть написать отправить посмотреть проверить сделать исправить";
    const string En = "the and for you are not with this that have from was but all can what your about which their would there could other these some time people because hello world thanks please today tomorrow yesterday work home house great good right wrong maybe really think know want need make take give find tell help where when while why how program computer phone message letter document file folder report project problem error update settings download install open close write send check meeting schedule teacher student university morning evening weekend money family friend";

    [Fact]
    public void WordsTheModelNeverSaw()
    {
        const string ru = "кошка собака молоко погода история математика физика химия литература вокзал больница аптека библиотека гитара карандаш тетрадь рюкзак наушники зарядка кровать холодильник сковорода бабушка дедушка ребята девушка парень мужчина женщина красный зелёный синий тёплый холодный весёлый грустный уставший голодный";
        const string en = "kitchen garden summer winter history science doctor market guitar pencil backpack headphones charger bottle coffee sugar butter cheese chicken yellow purple orange simple strong weak happy tired hungry";
        const string real = "vasya petya sasha moscow ivanov smirnov kolya dima roma anton maxim nikita lena katya olya sveta privet poka spasibo kak dela норм кек лол пон чел хз бро кринж рофл имба нуб лаба препод общага стипуха зачётка пятёрка чётко мтуси вуз яндекс сбер озон авито wb ozon yandex sber vk tg dota csgo valorant minecraft roblox fortnite genshin nvidia amd ryzen geforce rtx gtx ssd hdd usb hdmi wifi bluetooth todo readme config localhost admin root sudo bash grep curl wget mkdir chmod async await const void null true false string";
        int found(string list) { var w = list.Split(' '); var miss = w.Where(x => AutoLayout.Decide(LayoutFix.Convert(x)!) != x).ToList(); _o.WriteLine($"unseen {w.Length - miss.Count}/{w.Length}, missed: {string.Join(' ', miss)}"); return 100 * (w.Length - miss.Count) / w.Length; }
        var wrong = real.Split(' ').Where(x => AutoLayout.Decide(x) != null).ToList();
        _o.WriteLine($"false fixes {wrong.Count}/{real.Split(' ').Length}: {string.Join(' ', wrong.Select(x => x + "→" + AutoLayout.Decide(x)))}");
        found(ru); found(en);
    }

    [Fact]
    public void MostWrongLayoutWordsAreFound()
    {
        int Rate(string list) { var w = list.Split(' '); var missed = w.Where(x => AutoLayout.Decide(LayoutFix.Convert(x)!) != x).ToList(); _o.WriteLine($"{w.Length - missed.Count}/{w.Length} missed: {string.Join(' ', missed)}"); return 100 * (w.Length - missed.Count) / w.Length; }
        Assert.True(Rate(Ru) >= 85);
        Assert.True(Rate(En) >= 85);
    }
}

public class OptionalWindowsPartsTests
{
    // Regression: a SpeechSynthesizer in a static field took the whole Jarvis page down on Windows without speech.
    [Fact]
    public void VoiceDoesNotTouchWindowsSpeechWhenLoaded()
    {
        var eager = typeof(Voice).GetFields(System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public)
            .Where(f => f.FieldType == typeof(Windows.Media.SpeechSynthesis.SpeechSynthesizer)).Select(f => f.GetValue(null));
        Assert.All(eager, Assert.Null);
    }

    [Fact]
    public void DefenderStatusSaysWhenItCantTell() => Assert.False(new DefenderStatus(false, false, false, "?", default, default, default, "", false).Available);
}

public class PaletteSearchTests
{
    [Theory]
    [InlineData("замена текста")]
    [InlineData("заменить")]
    [InlineData("раскладка")]
    [InlineData("карамба")]
    [InlineData("ghbdtn")]
    [InlineData("текст ошибка")]
    public void LayoutFixIsFoundByOrdinaryWords(string q) => Assert.True(CommandPalette.Matches(Defaults.HotkeyNames["layoutFix"], "layoutFix", q));

    [Fact] public void OtherCommandsAreNotDragged() => Assert.False(CommandPalette.Matches("Громкость +5", "volUp", "раскладка"));
    [Fact] public void ReplacementIsNotANote() => Assert.False(CommandPalette.Matches(Defaults.HotkeyNames["secQuick"], "secQuick", "замена текста"));
    [Fact] public void FullWordsAsTyped() => Assert.True(CommandPalette.Matches(Defaults.HotkeyNames["layoutFix"], "layoutFix", "замена текста"));
}

public class EdgeGecTests
{
    // Reference values from edge-tts drm.generate_sec_ms_gec_v2 (Python) for the same timestamps.
    [Theory]
    [InlineData(1700000000.0, "2CD23A2CA0B2595A-F3C846C370E05F9CC83ACC5F995C5F5D87A4D0F14CE8936A0F902E64099E38A2")]
    [InlineData(1761200000.5, "C18EDA1D9D6E9E1A-020D623AC0DF9B38882CE331BEBD65FD080406B574104C1D939CD3CC8DAA5662")]
    [InlineData(1761200100.0, "17E5B724471DC36D-716848FA739D1BF437A4D47F4D9680A713D80AE6FDA92669439165A9B5AD3A10")]
    [InlineData(1761210599.999, "D34EE2E9C8ADF566-6468D3DF9912EE46F6A1E84EEF882AE6854CFBECBA162BF0D77A3030C98FCDED")]
    [InlineData(0.0, "52BC7A17B50DFEC9-28C4394775C8751CD2F6859C030C06DB1489ACAB3B3A7651831FC26895177ED6")]
    public void MatchesEdgeTts(double t, string expected) => Assert.Equal(expected, EdgeGec.Generate(t, "6A5AA1D4EAFF4E9FB37E23D68491D6F4"));
}

public class WhenTests
{
    static readonly DateTime Now = new(2026, 10, 26, 14, 0, 0);   // Monday

    static When.Result P(string s) => When.Parse(s, Now)!;

    [Fact] public void InMinutesReversed() { var r = P("Через минут 5 запусти фотошоп и включи режим работы"); Assert.Equal(Now.AddMinutes(5), r.At); Assert.Equal("запусти фотошоп и включи режим работы", r.Rest); }
    [Fact] public void InMinutes() { var r = P("через 10 минут выключи звук"); Assert.Equal(Now.AddMinutes(10), r.At); Assert.Equal("выключи звук", r.Rest); }
    [Fact] public void HalfHour() => Assert.Equal(Now.AddMinutes(30), P("через полчаса позвонить маме").At);
    [Fact] public void Hour() => Assert.Equal(Now.AddHours(1), P("через час проверить духовку").At);
    [Fact]
    public void NextSunday()
    {
        var r = P("В следующее воскресенье позарез надо сделать лабу по физике");
        Assert.Equal(new DateTime(2026, 11, 1), r.At); Assert.False(r.HasTime); Assert.Equal("сделать лабу по физике", r.Rest);
    }
    [Fact] public void Weekday() => Assert.Equal(new DateTime(2026, 10, 30), P("в пятницу сдать отчёт").At);
    [Fact] public void SameWeekdayMeansNextWeek() => Assert.Equal(new DateTime(2026, 11, 2), P("в понедельник созвон").At);
    [Fact] public void TomorrowEvening() { var r = P("завтра в 6 вечера тренировка"); Assert.Equal(new DateTime(2026, 10, 27, 18, 0, 0), r.At); Assert.Equal("тренировка", r.Rest); }
    [Fact] public void TomorrowClock() => Assert.Equal(new DateTime(2026, 10, 27, 9, 30, 0), P("завтра в 9:30 пара").At);
    [Fact] public void DateWords() => Assert.Equal(new DateTime(2026, 11, 1, 18, 30, 0), P("1 ноября в 18:30 концерт").At);
    [Fact] public void DateDots() => Assert.Equal(new DateTime(2026, 12, 25), P("25.12 купить подарки").At);
    [Fact] public void PastDateIsNextYear() => Assert.Equal(new DateTime(2027, 1, 5), P("5 января экзамен").At);
    [Fact] public void ClockAlreadyPassedIsTomorrow() => Assert.Equal(new DateTime(2026, 10, 27, 9, 0, 0), P("в 9 утра встать").At);
    [Fact] public void ClockLaterToday() => Assert.Equal(new DateTime(2026, 10, 26, 20, 0, 0), P("в 20:00 сериал").At);
    [Fact] public void NoTime() => Assert.Null(When.Parse("открой браузер", Now));
    [Theory]
    [InlineData("Через минут 5 запусти фотошоп и включи режим работы", true)]
    [InlineData("через 10 минут выключи звук", true)]
    [InlineData("В следующее воскресенье позарез надо сделать лабу по физике", false)]
    [InlineData("напомни завтра в 9 включить чайник", false)]
    [InlineData("завтра в 6 вечера тренировка", false)]
    public void ActionsOrReminder(string text, bool actions) => Assert.Equal(actions, Intents.IsActionPlan(P(text).Rest));

    [Fact] public void QuestionsAreNotPlans() { Assert.True(Intents.IsQuestion(P("какая погода завтра").Rest)); Assert.False(Intents.IsQuestion(P("завтра в 6 вечера тренировка").Rest)); }

    [Fact] public void Spoken() => Assert.Equal("в воскресенье, 1 ноября", When.Say(new DateTime(2026, 11, 1), false, Now));
}

public class OfflineCommandTests
{
    [Theory]
    [InlineData("дискорд", "discord", true)]
    [InlineData("яндекс", "yandex", true)]
    [InlineData("телеграмм", "telegram", true)]
    [InlineData("стим", "steam", true)]
    [InlineData("обс", "obs", true)]
    [InlineData("хром", "google chrome", true)]
    [InlineData("блокнот", "discord", false)]
    [InlineData("тетрис", "telegram", false)]
    [InlineData("дискорт", "discord", true)]
    [InlineData("ворд", "word", true)]
    [InlineData("свет", "steam", false)]
    public void FuzzyNames(string spoken, string name, bool match) => Assert.Equal(match, Intents.Fit(spoken, name) <= 0.3);

    [Theory]
    [InlineData("режим учебы", "Учёба")]
    [InlineData("профиль игры", "Игра")]
    [InlineData("режим работы", null)]
    public void ProfileByStem(string phrase, string? expected)
    {
        var keep = Settings.Current.Profiles.ToList();
        Settings.Current.Profiles = new() { new Profile { Name = "Игра" }, new Profile { Name = "Учёба" } };
        try { Assert.Equal(expected, Intents.FindProfileFor(phrase)?.Name); }
        finally { Settings.Current.Profiles = keep; }
    }

    [Fact] public void Translit() => Assert.Equal("yandeks brauzer", Intents.Translit("яндекс браузер"));

    [Fact]
    public void UserPhrases()
    {
        var keep = Settings.Current.Jarvis.Commands.ToList();
        Settings.Current.Jarvis.Commands = new() { new VoiceCommand { Phrases = "игровой режим; погнали играть", Action = "profile:Игра", ActionName = "Профиль: Игра" } };
        try
        {
            Assert.NotNull(Intents.MatchCommand("погнали играть"));
            Assert.NotNull(Intents.MatchCommand("игровой режин"));      // one letter misheard
            Assert.Null(Intents.MatchCommand("игровой ноутбук купить"));
        }
        finally { Settings.Current.Jarvis.Commands = keep; }
    }
}

public class YandexMusicTests
{
    static YandexMusic.Found? P(string best) => YandexMusic.Parse(System.Text.Json.JsonDocument.Parse("{\"best\":" + best + "}").RootElement);

    [Fact] public void Artist() => Assert.Equal(new YandexMusic.Found("artist", "artist:4462686", "Yanix"), P("{\"type\":\"artist\",\"result\":{\"id\":4462686,\"name\":\"Yanix\"}}"));
    [Fact] public void Track() => Assert.Equal(new YandexMusic.Found("track", "track:136352390", "Yanix — Party"),
        P("{\"type\":\"track\",\"result\":{\"id\":136352390,\"title\":\"Party\",\"artists\":[{\"name\":\"Yanix\"}]}}"));
    [Fact] public void Playlist() => Assert.Equal("playlist:1399409368_1000", P("{\"type\":\"playlist\",\"result\":{\"uid\":1399409368,\"kind\":1000,\"title\":\"Музыка звёзд\"}}")!.Seed);
    [Fact] public void NoBest() => Assert.Null(YandexMusic.Parse(System.Text.Json.JsonDocument.Parse("{\"best\":null}").RootElement));
    [Fact] public void Links()
    {
        Assert.Equal("yandexmusic://?deeplink=play-vibe&openPlayer=true&seeds=artist%3A1", YandexMusic.Link(YandexMusic.WavePath("artist:1"), app: true));
        Assert.Equal("https://music.yandex.ru/?deeplink=play-vibe&openPlayer=true&stationId=user:onyourwave", YandexMusic.Link(YandexMusic.WavePath(), app: false));
    }
}

public class FullCleanTests
{
    [Theory]
    [InlineData("Steam", @"""C:\Program Files (x86)\Steam\steam.exe"" -silent", FullClean.Verdict.Junk)]
    [InlineData("EpicGamesLauncher", @"""C:\Program Files (x86)\Epic Games\Launcher\Portal\Binaries\Win64\EpicGamesLauncher.exe"" -silent", FullClean.Verdict.Junk)]
    [InlineData("SunJavaUpdateSched", @"""C:\Program Files (x86)\Common Files\Java\Java Update\jusched.exe""", FullClean.Verdict.Junk)]
    [InlineData("MicrosoftEdgeAutoLaunch_58E8", @"""C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"" --no-startup-window --win-session-start", FullClean.Verdict.Junk)]
    [InlineData("Opera GX Browser Assistant", @"C:\Users\u\AppData\Local\Programs\Opera GX\assistant\browser_assistant.exe", FullClean.Verdict.Junk)]
    [InlineData("SecurityHealth", @"%windir%\system32\SecurityHealthSystray.exe", FullClean.Verdict.Keep)]
    [InlineData("RtkAudUService", @"""C:\WINDOWS\System32\DriverStore\FileRepository\realtekservice.inf_amd64\RtkAudUService64.exe"" -background", FullClean.Verdict.Keep)]
    [InlineData("PenTablet", @"C:\Program Files\Pentablet\PenTablet.exe /mini", FullClean.Verdict.Keep)]
    [InlineData("OneDrive", @"""C:\Program Files\Microsoft OneDrive\OneDrive.exe"" /background", FullClean.Verdict.Keep)]
    [InlineData("Discord", @"C:\Users\u\AppData\Local\Discord\Update.exe --processStart Discord.exe", FullClean.Verdict.Junk)]
    [InlineData("Rainmeter.lnk", @"C:\Users\u\Start Menu\Programs\Startup\Rainmeter.lnk", FullClean.Verdict.Ask)]
    [InlineData("browser", @"C:\Users\u\AppData\Local\Yandex\YandexBrowser\Application\browser.exe", FullClean.Verdict.Ask)]
    [InlineData("chrome", @"C:\Program Files\Google\Chrome\Application\chrome.exe", FullClean.Verdict.Ask)]
    [InlineData("MishkaHelper", @"C:\Program Files\Mishka Helper\MishkaHelper.exe", FullClean.Verdict.Keep)]
    public void Classify(string name, string cmd, FullClean.Verdict expected) => Assert.Equal(expected, FullClean.Classify(name, cmd));
}

public class CalendarTests
{
    const string Ics = """
BEGIN:VCALENDAR
BEGIN:VEVENT
DTSTART:20261019T090000
DTEND:20261019T103000
RRULE:FREQ=WEEKLY;BYDAY=MO,WE;UNTIL=20261231T000000Z
EXDATE:20261021T090000
SUMMARY:Матанализ\, лекция
LOCATION:ауд. 401
END:VEVENT
BEGIN:VEVENT
DTSTART;VALUE=DATE:20261023
SUMMARY:Дедлайн
  курсовой
END:VEVENT
BEGIN:VEVENT
DTSTART:20261020T120000
RRULE:FREQ=WEEKLY;INTERVAL=2;COUNT=3
SUMMARY:Физра
END:VEVENT
END:VCALENDAR
""";

    static List<string> On(string day) => Calendar.Parse(Ics).Where(r => Calendar.Occurs(r, DateTime.Parse(day))).Select(r => r.Title).ToList();

    [Fact] public void Parses() { var l = Calendar.Parse(Ics); Assert.Equal(3, l.Count); Assert.Equal("Матанализ, лекция", l[0].Title); Assert.Equal("Дедлайн курсовой", l[1].Title); Assert.True(l[1].AllDay); }
    [Fact] public void WeeklyByDay() { Assert.Contains("Матанализ, лекция", On("2026-10-26")); Assert.Contains("Матанализ, лекция", On("2026-10-28")); Assert.DoesNotContain("Матанализ, лекция", On("2026-10-27")); }
    [Fact] public void ExDate() => Assert.DoesNotContain("Матанализ, лекция", On("2026-10-21"));
    [Fact] public void Until() => Assert.DoesNotContain("Матанализ, лекция", On("2027-01-04"));
    [Fact] public void BeforeStart() => Assert.Empty(On("2026-10-12"));
    [Fact] public void Single() { Assert.Contains("Дедлайн курсовой", On("2026-10-23")); Assert.DoesNotContain("Дедлайн курсовой", On("2026-10-30")); }
    [Fact]
    public void IntervalAndCount()
    {
        Assert.Contains("Физра", On("2026-10-20"));
        Assert.DoesNotContain("Физра", On("2026-10-27"));   // every second week
        Assert.Contains("Физра", On("2026-11-03"));
        Assert.Contains("Физра", On("2026-11-17"));         // 3rd occurrence
        Assert.DoesNotContain("Физра", On("2026-12-01"));   // COUNT=3 exhausted
    }
}
