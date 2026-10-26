using System.Windows;
using System.Windows.Threading;

namespace MishkaHelper.Core;

/// <summary>Background services lifetime + periodic jobs (scheduled scan/clean, app rules, battery alerts).</summary>
public static class Services
{
    static DispatcherTimer? _jobs;
    static int _tick;

    public static void Start()
    {
        Try(Audio.Start);
        Try(SysMon.Start);
        Try(Hotkeys.Start);
        Try(Profiles.StartAuto);
        Try(Widgets.RestoreAll);
        Try(Remote.Apply);
        if (Settings.Current.BleRemoteEnabled) _ = BleRemote.Start().ContinueWith(t => { if (t.Result != null) Log.Info("BLE remote: " + t.Result); });
        Try(ScreenTime.Start);
        Try(Reminders.Start);
        Try(ClipHistory.Start);
#if !LITE
        Try(Scenarios.Start);
        // "Не включать при запуске": off at launch whatever it was last time; the switch on the Jarvis page turns it on for this session.
        if (!Settings.Current.Jarvis.StartWithApp && Settings.Current.Jarvis.Enabled) { Settings.Current.Jarvis.Enabled = false; Settings.Save(); }
        _ = Jarvis.Start();
#endif

        _jobs = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _jobs.Tick += async (_, _) =>
        {
            _tick++;
            Try(Profiles.CheckApps);
            Try(Focus.Check);
            if (_tick % 12 == 3) Try(AutoTheme);                         // every minute
            if (_tick % 60 == 1) await Devices.CheckBatteries();          // every 5 min
            if (_tick % 360 == 2) await Scheduled();                     // every 30 min
        };
        _jobs.Start();

        // Caps/Num Lock OSD (the Nitro keyboard has no Caps LED). Cheap poll, only while enabled.
        bool caps = Toolbox.CapsLock, num = Toolbox.NumLock;
        var keys = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        keys.Tick += (_, _) =>
        {
            if (!Settings.Current.LockKeysOsd) return;
            bool c = Toolbox.CapsLock, n = Toolbox.NumLock;
            if (c != caps) Osd.Show("", c ? "CAPS LOCK вкл" : "Caps Lock выкл");
            if (n != num) Osd.Show("", n ? "Num Lock вкл" : "Num Lock выкл");
            (caps, num) = (c, n);
        };
        keys.Start();
    }

    static bool? _wasDark;

    /// <summary>Switch Windows light/dark by time of day; applies once per transition so manual changes stick until the next one.</summary>
    static void AutoTheme()
    {
        var s = Settings.Current;
        if (!s.AutoTheme || !TimeSpan.TryParse(s.LightAt, out var light) || !TimeSpan.TryParse(s.DarkAt, out var dark)) return;
        var now = DateTime.Now.TimeOfDay;
        bool shouldBeDark = light < dark ? now < light || now >= dark : now >= dark && now < light;
        if (_wasDark == shouldBeDark) return;
        _wasDark = shouldBeDark;
        if (Tweaks.DarkTheme != shouldBeDark) Tweaks.DarkTheme = shouldBeDark;
    }

    public static void Stop()
    {
        _jobs?.Stop();
#if !LITE
        Try(Jarvis.Stop);
        Try(Brain.Unload);
#endif
        Try(SysMon.Stop);
        Try(Remote.Stop);
        Try(BleRemote.Stop);
        Try(ScreenTime.Save);
        Try(Hotkeys.Stop);
        Try(Widgets.SaveAll);
        Try(Settings.Save);
    }

    static async Task Scheduled()
    {
        var s = Settings.Current;
        if (s.CheckUpdates && (DateTime.Now - s.LastUpdateCheck).TotalDays >= 1) await Updater.CheckInBackground(silent: true);
        if (s.ScanIntervalDays > 0 && (DateTime.Now - s.LastScan).TotalDays >= s.ScanIntervalDays)
        {
            Toast.Show("", "Плановая проверка", "Быстрая проверка Defender запущена");
            var (threats, _) = await Security.Scan(Security.ScanKind.Quick);
            Toast.Show(threats ? "" : "", threats ? "Найдены угрозы!" : "Угроз не найдено", threats ? "Открой раздел «Защита»" : "Быстрая проверка завершена",
                () => App.Current.Navigate("Security"));
        }
        if (s.CleanIntervalDays > 0 && (DateTime.Now - s.LastClean).TotalDays >= s.CleanIntervalDays)
        {
            var freed = await Cleaner.BoostAll();
            Toast.Show("", "Плановая очистка", $"Освобождено {Cleaner.Size(freed)}");
        }
    }

    static void Try(Action a) { try { a(); } catch (Exception e) { Log.Error(e); } }
}

public static class Autostart
{
    const string Task = "MishkaHelper";

    public static bool Enabled => Shell.Run("schtasks", $"/query /tn {Task}").Contains(Task);

    public static bool PointsHere =>
        Shell.Run("schtasks", $"/query /tn {Task} /xml").Contains(Environment.ProcessPath!, StringComparison.OrdinalIgnoreCase);

    /// <summary>Logon task with highest privileges: starts elevated without a UAC prompt.</summary>
    public static void Set(bool on)
    {
        if (on)
        {
            var exe = Environment.ProcessPath!;
            var xml = $"""
                <?xml version="1.0" encoding="UTF-16"?>
                <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
                  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{Environment.UserDomainName}\{Environment.UserName}</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>
                  <Principals><Principal id="Author"><UserId>{Environment.UserDomainName}\{Environment.UserName}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
                  <Settings>
                    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                    <Priority>5</Priority>
                  </Settings>
                  <Actions Context="Author"><Exec><Command>"{exe}"</Command><Arguments>--autostart</Arguments></Exec></Actions>
                </Task>
                """;
            // Protected folder, not %TEMP%: a swapped XML here would become an admin logon task.
            var tmp = System.IO.Path.Combine(AppPaths.Shared, "mishka-task.xml");
            System.IO.File.WriteAllText(tmp, xml, System.Text.Encoding.Unicode);
            Shell.Run("schtasks", $"/create /tn {Task} /xml \"{tmp}\" /f");
            System.IO.File.Delete(tmp);
        }
        else Shell.Run("schtasks", $"/delete /tn {Task} /f");
    }
}
