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
    [InlineData("MishkaHelper-Setup-0.5.0.exe", "Setup-", ".exe", true, true)]
    [InlineData("MishkaHelper-Lite-Setup-0.5.0.exe", "Setup-", ".exe", true, false)]
    [InlineData("MishkaHelper-Lite-Setup-0.5.0.exe", "Setup-", ".exe", false, true)]
    [InlineData("MishkaHelper-Setup-0.5.0.exe", "Setup-", ".exe", false, false)]
    [InlineData("MishkaHelper-Portable-0.5.0.zip", "Portable-", ".zip", true, true)]
    [InlineData("MishkaHelper-Portable-0.5.0.zip", "Setup-", ".exe", true, false)]
    public void UpdaterAsset(string name, string kind, string ext, bool full, bool match) => Assert.Equal(match, Updater.AssetMatches(name, kind, ext, full));

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
