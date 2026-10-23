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
