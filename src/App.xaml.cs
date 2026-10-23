using System.IO;
using System.Windows;
using System.Windows.Threading;
using MishkaHelper.Core;

namespace MishkaHelper;

public partial class App : Application
{
    Mutex? _single;
    EventWaitHandle? _showSignal;
    Tray? _tray;
    MainWindow? _main;

    public static new App Current => (App)Application.Current;

    protected override void OnStartup(StartupEventArgs e)
    {
        _single = new Mutex(true, "MishkaHelper.Single", out bool first);
        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "MishkaHelper.Show");
        var quitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, "MishkaHelper.Quit");
        if (!first && !e.Args.Contains("--shots") && !e.Args.Contains("--selftest"))   // dev runs may sit beside the installed copy
        {
            // Ask the running copy to show itself, or to exit (used by the installer/updater).
            (e.Args.Contains("--quit") ? quitSignal : _showSignal).Set();
            Shutdown();
            return;
        }
        if (e.Args.Contains("--quit")) { Shutdown(); return; }
        new Thread(() => { if (quitSignal.WaitOne()) Dispatcher.Invoke(Quit); }) { IsBackground = true }.Start();

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error(ex.Exception);
            ex.Handled = true;
        };

        Settings.Load();
        foreach (var n in AppPaths.Notes) Log.Info(n);
        ThemeManager.Apply();
        _tray = new Tray();

        new Thread(() =>
        {
            while (_showSignal.WaitOne()) Dispatcher.Invoke(ShowMain);
        }) { IsBackground = true }.Start();

        Services.Start();

        Task.Run(Updater.CleanLegacyUninstallEntry);

        // Keep the logon task pointing at this exe (first run, or the app was moved/updated).
        if (Settings.Current.StartWithWindows)
            Task.Run(() => { if (!Autostart.PointsHere) Autostart.Set(true); });

        if (Array.IndexOf(e.Args, "--shots") is int si and >= 0 && si + 1 < e.Args.Length)
        {
            ShowMain();
            Shots.Run(e.Args[si + 1], _main!);
            return;
        }

#if !LITE
        if (Array.IndexOf(e.Args, "--selftest") is int ti and >= 0 && ti + 1 < e.Args.Length)
        {
            ShowMain();
            SelfTest.Run(e.Args[ti + 1], _main!, ti + 2 < e.Args.Length ? e.Args[ti + 2] : "");
            return;
        }
#endif

        bool tray = e.Args.Contains("--tray") || (e.Args.Contains("--autostart") && Settings.Current.StartMinimized);
        if (!tray) ShowMain();
    }

    public void ShowMain()
    {
        if (_main == null)
        {
            _main = new MainWindow();
            _main.Closed += (_, _) => _main = null;
        }
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    public void Navigate(string page)
    {
        ShowMain();
        _main?.Navigate(page);
    }

    public void Quit()
    {
        Services.Stop();
        _tray?.Dispose();
        Shutdown();
    }
}

public static class Log
{
    static readonly string FilePath = Path.Combine(Settings.Dir, "log.txt");

    public static void Error(Exception ex) => Write("ERR " + ex);
    public static void Info(string s) => Write(s);

    static void Write(string s)
    {
        try
        {
            Directory.CreateDirectory(Settings.Dir);
            var fi = new FileInfo(FilePath);
            if (fi.Exists && fi.Length > 1_000_000) fi.Delete();   // ponytail: crude cap, rotate if logs ever matter
            File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {s}\n");
        }
        catch { }
    }
}
