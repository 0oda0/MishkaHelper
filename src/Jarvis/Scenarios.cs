using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using NAudio.CoreAudioApi;

namespace MishkaHelper.Core;

/// <summary>Runs user scenarios and watches their triggers (voice, hotkey, time, system events).</summary>
public static class Scenarios
{
    public static readonly (string Key, string Name, string Hint)[] StepKinds =
    {
        ("command", "Команда Джарвису", "Как сказал бы голосом: «запусти фотошоп», «профиль Игра»"),
        ("action", "Действие Mishka", "Любое действие из горячих клавиш"),
        ("launch", "Открыть программу / сайт / файл", "Путь, ссылка или имя программы"),
        ("close", "Закрыть программу", "Имя процесса, например chrome"),
        ("profile", "Профиль", "Имя профиля"),
        ("volume", "Громкость", "0–100"),
        ("brightness", "Яркость", "0–100"),
        ("wait", "Пауза", "Секунды"),
        ("keys", "Нажать клавиши", "Например Ctrl+Shift+Esc или Win+D"),
        ("type", "Напечатать текст", "Текст в активное окно"),
        ("say", "Сказать вслух", "Текст"),
        ("notify", "Уведомление", "Текст"),
        ("note", "Записать в заметки", "Текст"),
        ("script", "Команда / скрипт", "Например ping ya.ru или путь к .bat/.ps1"),
        ("if", "Условие (иначе стоп)", "battery / ac / process:имя / time:09:00-18:00"),
    };

    public static readonly (string Key, string Name)[] Events =
    {
        ("startup", "Запуск Windows"), ("ac_on", "Подключил зарядку"), ("ac_off", "Отключил зарядку"),
        ("battery_low", "Батарея ниже N%"), ("process_start", "Запустилась программа"), ("process_stop", "Закрылась программа"),
        ("audio_new", "Подключились наушники/колонки"), ("usb_drive", "Вставлена флешка"),
    };

    public static Scenario? Find(string phrase)
    {
        var q = Ears.Clean(phrase);
        return Settings.Current.Scenarios.FirstOrDefault(s => s.Enabled && Ears.Clean(s.Name) == q)
               ?? Settings.Current.Scenarios.FirstOrDefault(s => s.Enabled && Ears.Distance(Ears.Clean(s.Name), q) <= 2);
    }

    /// <summary>Voice trigger: the phrase matches one of the scenario's voice triggers.</summary>
    public static Scenario? ByVoice(string phrase)
    {
        var q = Ears.Clean(phrase);
        return Settings.Current.Scenarios.FirstOrDefault(s => s.Enabled && s.Triggers.Any(t => t.Kind == "voice" && t.Value.Length > 0 &&
            (Ears.Clean(t.Value) == q || Ears.Distance(Ears.Clean(t.Value), q) <= Math.Max(1, q.Length / 8))));
    }

    static readonly HashSet<string> Running = new();

    public static async Task Run(Scenario s)
    {
        lock (Running) if (!Running.Add(s.Id)) return;
        Log.Info("scenario " + s.Name);
        try
        {
            foreach (var st in s.Steps)
                if (!await RunStep(st)) break;
        }
        catch (Exception e) { Log.Error(e); Toast.Show("", $"Сценарий «{s.Name}»", e.Message); }
        finally { lock (Running) Running.Remove(s.Id); }
    }

    /// <summary>false = stop the scenario (failed "if"). Public for the editor's "test step" button.</summary>
    public static async Task<bool> RunStep(ScenarioStep st)
    {
        var v = st.Value.Trim();
        var ui = System.Windows.Application.Current.Dispatcher;
        switch (st.Kind)
        {
            case "action": await ui.InvokeAsync(() => Actions.Run(v)); break;
            case "command":
                // The user scheduled it themselves, so a confirmation ("выключить?") counts as already given.
                var r = await Intents.Try(v);
                if (r == null) await ui.InvokeAsync(() => Toast.Show("", "Сценарий", $"Не понял команду «{v}»"));
                else if (r.Confirm != null) await r.Confirm();
                break;
            case "launch":
                if (v.Contains("://") || v.Contains('\\') || v.Contains('.') && File.Exists(Environment.ExpandEnvironmentVariables(v))) Shell.Open(Environment.ExpandEnvironmentVariables(v), st.Arg);
                else if (Intents.Launch(Ears.Clean(v)) == null) Shell.Open(v, st.Arg);
                break;
            case "close":
                foreach (var p in Process.GetProcessesByName(v.Replace(".exe", "", StringComparison.OrdinalIgnoreCase)))
                {
                    try { if (!p.CloseMainWindow() || !p.WaitForExit(3000)) p.Kill(); } catch { }
                    p.Dispose();
                }
                break;
            case "profile": await ui.InvokeAsync(() => Profiles.Apply(v)); break;
            case "volume": if (int.TryParse(v, out var vol)) Audio.Volume = vol; break;
            case "brightness": if (int.TryParse(v, out var br)) Display.Brightness = br; break;
            case "wait": if (double.TryParse(v.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture, out var sec)) await Task.Delay(TimeSpan.FromSeconds(Math.Min(sec, 3600))); break;
            case "keys": Keys.Combo(v); await Task.Delay(150); break;
            case "type": await ui.InvokeAsync(() => Keys.Type(v)); break;
            case "say": await Voice.Say(v); break;
            case "notify": await ui.InvokeAsync(() => Toast.Show("", "Mishka", v)); break;
            case "note": Notes.Add(v); break;
            case "script":
                var exp = Environment.ExpandEnvironmentVariables(v);
                if (exp.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) await Shell.RunAsync("powershell", $"-NoProfile -ExecutionPolicy Bypass -File \"{exp}\"", 10 * 60_000);
                else if (exp.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) || exp.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)) await Shell.RunAsync("cmd", $"/c \"{exp}\"", 10 * 60_000);
                else await Shell.RunAsync("cmd", "/c " + exp, 10 * 60_000);
                break;
            case "if": return Check(v);
        }
        return true;
    }

    static bool Check(string cond)
    {
        cond = cond.Trim().ToLowerInvariant();
        if (cond == "battery") return !Power.OnAc;
        if (cond == "ac") return Power.OnAc;
        if (cond.StartsWith("process:")) return Process.GetProcessesByName(cond[8..].Replace(".exe", "")).Length > 0;
        if (cond.StartsWith("!process:")) return Process.GetProcessesByName(cond[9..].Replace(".exe", "")).Length == 0;
        if (cond.StartsWith("time:"))
        {
            var p = cond[5..].Split('-');
            if (p.Length == 2 && TimeSpan.TryParse(p[0], out var a) && TimeSpan.TryParse(p[1], out var b))
            {
                var now = DateTime.Now.TimeOfDay;
                return a <= b ? now >= a && now <= b : now >= a || now <= b;
            }
        }
        return true;
    }

    // ---------- trigger watcher ----------
    static DispatcherTimer? _timer;
    static HashSet<string> _procs = new(StringComparer.OrdinalIgnoreCase);
    static bool _ac;
    static int _battery = 100;
    static HashSet<string> _drives = new();
    static HashSet<string> _audio = new();
    static string _lastMinute = "";

    public static void Start()
    {
        _ac = Power.OnAc;
        _battery = Power.Battery().Percent;
        _procs = Snapshot();
        _drives = Drives();
        _audio = Audio.Devices(DataFlow.Render).Select(d => d.Id).ToHashSet();
        Fire("startup");

        Audio.Changed += () => System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
        {
            var now = Audio.Devices(DataFlow.Render).Select(d => d.Id).ToHashSet();
            if (now.Except(_audio).Any()) Fire("audio_new");
            _audio = now;
        });

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    static HashSet<string> Snapshot() => Process.GetProcesses().Select(p => { var n = p.ProcessName; p.Dispose(); return n; }).ToHashSet(StringComparer.OrdinalIgnoreCase);
    static HashSet<string> Drives() => DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady).Select(d => d.Name).ToHashSet();

    static void Tick()
    {
        var all = Settings.Current.Scenarios;
        if (all.Count == 0 || !all.Any(s => s.Enabled && s.Triggers.Any(t => t.Kind is "time" or "event" or "once"))) return;

        // One-time scenarios ("через 5 минут запусти фотошоп"): run once, then they are gone.
        foreach (var s in all.Where(s => s.Enabled && s.Triggers.Any(t => t.Kind == "once" && DateTime.TryParse(t.Value, out var at) && at <= DateTime.Now)).ToList())
        {
            s.Triggers.RemoveAll(t => t.Kind == "once");
            if (s.Triggers.Count == 0) all.Remove(s);
            Settings.Save();
            _ = Run(s);
        }

        var minute = DateTime.Now.ToString("HH:mm");
        if (minute != _lastMinute)
        {
            _lastMinute = minute;
            var day = ((int)DateTime.Now.DayOfWeek + 6) % 7 + 1;   // 1 = Monday
            foreach (var s in all.Where(s => s.Enabled))
                if (s.Triggers.Any(t => t.Kind == "time" && t.Value == minute && (t.Arg.Length == 0 || t.Arg.Contains(day.ToString()))))
                    _ = Run(s);
        }

        bool needProcs = all.Any(s => s.Triggers.Any(t => t.Value is "process_start" or "process_stop"));
        if (needProcs)
        {
            var now = Snapshot();
            foreach (var started in now.Except(_procs)) Fire("process_start", started);
            foreach (var stopped in _procs.Except(now)) Fire("process_stop", stopped);
            _procs = now;
        }

        bool ac = Power.OnAc;
        if (ac != _ac) { Fire(ac ? "ac_on" : "ac_off"); _ac = ac; }

        var bat = Power.Battery().Percent;
        foreach (var s in all.Where(s => s.Enabled))
            foreach (var t in s.Triggers.Where(t => t.Kind == "event" && t.Value == "battery_low"))
                if (int.TryParse(t.Arg, out var lim) && _battery > lim && bat <= lim && !ac) _ = Run(s);
        _battery = bat;

        var drives = Drives();
        if (drives.Except(_drives).Any()) Fire("usb_drive");
        _drives = drives;
    }

    static void Fire(string ev, string arg = "")
    {
        foreach (var s in Settings.Current.Scenarios.Where(s => s.Enabled))
            if (s.Triggers.Any(t => t.Kind == "event" && t.Value == ev &&
                                    (arg.Length == 0 || t.Arg.Length == 0 || string.Equals(t.Arg.Replace(".exe", ""), arg, StringComparison.OrdinalIgnoreCase))))
                _ = Run(s);
    }
}
