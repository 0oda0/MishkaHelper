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
