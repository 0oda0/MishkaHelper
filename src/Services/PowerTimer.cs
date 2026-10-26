using System.Diagnostics;

namespace MishkaHelper.Core;

/// <summary>
/// Shutdown from the couch: in N minutes, right now, or when the download finishes.
/// Uses Windows' own shutdown timer, so it still happens if Mishka is closed; any timeout > 0 already closes apps by force.
/// </summary>
public static class PowerTimer
{
    public static DateTime? At { get; private set; }
    public static bool WaitDownload { get; private set; }
    static System.Windows.Threading.DispatcherTimer? _watch;
    static int _quietSec;

    static void Run(string args)
    {
        try { Process.Start(new ProcessStartInfo(Shell.Sys("shutdown"), args) { CreateNoWindow = true, UseShellExecute = false })?.WaitForExit(5000); }
        catch (Exception e) { Log.Error(e); }
    }

    public static string In(int minutes)
    {
        minutes = Math.Clamp(minutes, 1, 24 * 60);
        Cancel(quiet: true);
        Run($"/s /t {minutes * 60}");
        At = DateTime.Now.AddMinutes(minutes);
        return $"ПК выключится в {At:HH:mm}";
    }

    public static string Now()
    {
        Cancel(quiet: true);
        Run("/s /t 15");
        At = DateTime.Now.AddSeconds(15);
        return "Закрываю всё — выключение через 15 с";
    }

    /// <summary>Waits until 3 minutes pass without real network or disk traffic, then a 1-minute warning and shutdown.</summary>
    public static string AfterDownload()
    {
        Cancel(quiet: true);
        WaitDownload = true; _quietSec = 0;
        _watch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        _watch.Tick += (_, _) =>
        {
            var s = SysMon.Last;
            // ponytail: fixed thresholds (150 KB/s down, 5 MB/s disk write); a slow trickle download looks finished.
            bool busy = s.NetDown > 150_000 || s.DiskWrite > 5_000_000;
            _quietSec = busy ? 0 : _quietSec + 10;
            if (_quietSec < 180) return;
            StopWatch();
            In(1);
            Toast.Show("", "Загрузка закончилась", "ПК выключится через минуту. Отмена — на телефоне или кнопкой здесь.", () => Cancel());
            _ = Phone.Say("Загрузка закончилась — ПК выключится через минуту");
        };
        _watch.Start();
        return "Выключу, когда 3 минуты не будет загрузки";
    }

    static void StopWatch() { _watch?.Stop(); _watch = null; WaitDownload = false; }

    public static string Cancel(bool quiet = false)
    {
        StopWatch();
        if (At != null || !quiet) Run("/a");
        At = null;
        return "Выключение отменено";
    }

    public static string Describe() =>
        WaitDownload ? $"Жду конца загрузки (тихо {_quietSec / 60} из 3 мин)"
        : At is { } t && t > DateTime.Now ? $"Выключится в {t:HH:mm}" : "";
}
