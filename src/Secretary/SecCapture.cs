using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using NAudio.Wave;
using Vosk;

namespace MishkaHelper.Core;

/// <summary>Hotkey windows: a note for the program in front, a quick note by text or voice, "remember this page".</summary>
public static class SecCapture
{
    static Window? _w;
    static SecretarySettings S => Settings.Current.Secretary;

    static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase) { "chrome", "msedge", "firefox", "browser", "opera", "brave", "vivaldi" };

    /// <summary>The address of a browser's active tab, read from its address bar through UI Automation. Null if it isn't a browser or hides it.</summary>
    public static string? BrowserUrl(IntPtr window, string process)
    {
        if (!Browsers.Contains(process)) return null;
        try
        {
            var root = AutomationElement.FromHandle(window);
            foreach (AutomationElement e in root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit)))
            {
                if (!e.TryGetCurrentPattern(ValuePattern.Pattern, out var p)) continue;
                var v = ((ValuePattern)p).Current.Value.Trim();
                if (LooksLikeUrl(v)) return v.Contains("://") ? v : "https://" + v;
            }
        }
        catch (Exception e) { Log.Error(e); }
        return null;
    }

    internal static bool LooksLikeUrl(string v) =>
        v.Length > 3 && !v.Contains(' ') && (v.Contains("://") || System.Text.RegularExpressions.Regex.IsMatch(v, @"^[\w.-]+\.[a-zа-я]{2,}(/|$|\?)", System.Text.RegularExpressions.RegexOptions.IgnoreCase));

    static Task<(string Key, string Label, string? Url, string Title, string App)> Context()
    {
        var (_, name, title) = Secretary.Foreground();
        var window = Secretary.ForegroundWindow();
        // The hotkey may fire over our own window: then the last program the secretary saw is meant.
        if (name.Equals("MishkaHelper", StringComparison.OrdinalIgnoreCase)) { name = Secretary.CurrentApp; title = Secretary.CurrentTitle; window = IntPtr.Zero; }
        return Task.Run(() =>
        {
            var url = window != IntPtr.Zero ? BrowserUrl(window, name) : null;
            var host = url != null && Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.Replace("www.", "") : null;
            return (Key: (host ?? name).ToLowerInvariant(), Label: host ?? name, Url: url, Title: title, App: name);
        });
    }

    static Window Show(UIElement content, TextBox focus)
    {
        _w?.Close();
        var w = Floating.Create(clickThrough: false, noActivate: false);
        var panel = Floating.Panel(content, 16);
        panel.Padding = new Thickness(14);
        w.Content = panel;
        w.Closed += (_, _) => { if (_w == w) _w = null; };
        _w = w;
        w.Show();
        var wa = SystemParameters.WorkArea;
        w.Left = wa.Left + (wa.Width - w.ActualWidth) / 2;
        w.Top = wa.Top + wa.Height * 0.2;
        Native.SetForegroundWindow(new WindowInteropHelper(w).Handle);
        w.Activate();
        focus.Focus();
        focus.CaretIndex = focus.Text.Length;
        return w;
    }

    // ---------- note for the program in front ----------
    public static async void AppNote()
    {
        if (!Secretary.Require(S.AppNotes, "Заметки к программам")) return;
        var ctx = await Context();
        SecStore.Cur.AppNotes.TryGetValue(ctx.Key, out var note);
        var box = new TextBox { Text = note ?? "", FontSize = 15, Width = 560, MinHeight = 120, MaxHeight = 400, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Padding = new Thickness(10), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var sp = new StackPanel();
        sp.Children.Add(UiKit.Text("Заметка: " + ctx.Label, 15, false, FontWeights.SemiBold));
        sp.Children.Add(new Border { Height = 8 });
        sp.Children.Add(box);
        sp.Children.Add(UiKit.Text("Сохраняется сама · Esc — закрыть · появится, когда откроешь это снова", 11, true));
        void Save()
        {
            var t = box.Text.Trim();
            if (t.Length == 0) SecStore.Cur.AppNotes.Remove(ctx.Key); else SecStore.Cur.AppNotes[ctx.Key] = t;
            SecStore.Save();
            Secretary.Apply();
        }
        var w = Show(sp, box);
        box.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) w.Close(); };
        w.Closing += (_, _) => Save();
    }

    // ---------- quick note: text, or voice into the same box ----------
    public static void Quick(bool voice = false)
    {
        if (!Secretary.Require(S.Capture, "Быстрая заметка")) return;
        var box = new TextBox { FontSize = 16, Width = 600, Padding = new Thickness(10), BorderThickness = new Thickness(0) };
        var status = UiKit.Text("Enter — сохранить · Esc — закрыть · «завтра в 6 сдать отчёт» станет напоминанием", 11, true);
        var mic = new Button { Content = "", FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["IconFont"], Padding = new Thickness(10, 6, 10, 6), Margin = new Thickness(8, 0, 0, 0), ToolTip = "Сказать голосом" };
        var row = new DockPanel();
        DockPanel.SetDock(mic, Dock.Right); row.Children.Add(mic);
        row.Children.Add(UiKit.WithHint(box, "Что записать?"));
        var sp = new StackPanel();
        sp.Children.Add(row);
        sp.Children.Add(status);
        var w = Show(sp, box);
        var app = Secretary.CurrentApp;

        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { SecVoice.Stop(); w.Close(); return; }
            if (e.Key != Key.Enter || box.Text.Trim().Length == 0) return;
            e.Handled = true;
            var t = box.Text.Trim();
            SecVoice.Stop();
            w.Close();
            Save(t, app);
        };
        async void Listen()
        {
            if (!Secretary.Active(S.Voice)) { status.Text = "Голосовые заметки выключены на странице «Секретарша»."; return; }
            status.Text = "Говори… Пауза 3 секунды или кнопка — конец записи.";
            var text = await SecVoice.Record(s => status.Text = s);
            if (text.Length > 0) box.Text = (box.Text + " " + text).Trim();
            box.CaretIndex = box.Text.Length;
            box.Focus();
            status.Text = text.Length > 0 ? "Проверь текст и нажми Enter." : "Ничего не расслышала.";
        }
        mic.Click += (_, _) => { if (SecVoice.Recording) SecVoice.Stop(); else Listen(); };
        if (voice) Listen();
    }

    /// <summary>A date makes it a reminder too; the note always stays, with the program it was made in.</summary>
    public static string Save(string text, string app)
    {
        Notes.Add(app.Length > 0 ? $"[{app}] {text}" : text);
        var when = When.Parse(text, DateTime.Now);
        if (when != null && when.At > DateTime.Now)
        {
            Reminders.Add(Reminders.Cap(when.Rest.Length > 0 ? when.Rest : text), when.At, when.HasTime);
            var said = $"Записала и напомню {When.Say(when.At, when.HasTime, DateTime.Now)}";
            Osd.Show("", said);
            return said;
        }
        Osd.Show("", "Записала");
        return "Записала";
    }

    // ---------- "запомни": this page or window ----------
    public static async void Remember()
    {
        if (!Secretary.Require(S.Remember, "Запомнить страницу")) return;
        var ctx = await Context();
        var target = ctx.Url ?? ctx.Title;
        var title = CleanTitle(ctx.Title, ctx.App);
        var box = new TextBox { Text = title, FontSize = 16, Width = 600, Padding = new Thickness(10), BorderThickness = new Thickness(0) };
        var daily = new CheckBox { Style = (Style)Application.Current.Resources["Switch"], Content = "Показывать в утренней и вечерней сводке (расписание и всё, что смотришь каждый день)", IsChecked = LooksDaily(title + " " + target) };
        var sp = new StackPanel();
        sp.Children.Add(UiKit.Text("Запомнить: " + (target.Length > 80 ? target[..80] + "…" : target), 12, true));
        sp.Children.Add(new Border { Height = 6 });
        sp.Children.Add(box);
        sp.Children.Add(daily);
        sp.Children.Add(UiKit.Text("Enter — сохранить · Esc — закрыть · потом найдётся по названию на странице «Секретарша»", 11, true));
        var w = Show(sp, box);
        box.SelectAll();
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) { w.Close(); return; }
            if (e.Key != Key.Enter || box.Text.Trim().Length == 0) return;
            e.Handled = true;
            SecStore.Cur.Remembered.Insert(0, new RememberedItem { Title = box.Text.Trim(), Target = target, App = ctx.App, Daily = daily.IsChecked == true });
            SecStore.Save();
            Secretary.Apply();
            w.Close();
            Osd.Show("", "Запомнила");
        };
    }

    internal static bool LooksDaily(string s) => System.Text.RegularExpressions.Regex.IsMatch(s, @"расписан|schedule|timetable|занят|дневник|календар", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    // "Расписание — Google Chrome" → "Расписание"
    internal static string CleanTitle(string title, string app)
    {
        var t = System.Text.RegularExpressions.Regex.Replace(title, @"\s*[-—–]\s*(Google Chrome|Microsoft.? Edge|Mozilla Firefox|Яндекс\s?Браузер|Yandex|Opera|Brave|Vivaldi)\s*$", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim();
        return t.Length > 0 ? t : app;
    }
}

/// <summary>Voice note without Jarvis: microphone → Vosk (offline, 44 MB, downloaded once). Stops on 3 s of silence after speech.</summary>
public static class SecVoice
{
    const int Rate = 16000;
    static Model? _model;
    static TaskCompletionSource? _stop;

    public static bool Recording => _stop != null;
    public static void Stop() => _stop?.TrySetResult();

    // ponytail: fixed energy threshold; a noisy room just ends the note later (60 s cap). Vosk-small over Whisper so this edition stays light.
    internal static double Level(byte[] b, int n)
    {
        long sum = 0;
        for (int i = 0; i + 1 < n; i += 2) { int v = BitConverter.ToInt16(b, i); sum += v * v; }
        return n < 2 ? 0 : Math.Sqrt(sum / (double)(n / 2));
    }

    public static async Task<string> Record(Action<string> status)
    {
        if (Recording) return "";
        try
        {
            if (!Models.Has(Models.Vosk))
            {
                status("Скачиваю распознавание речи (один раз, 44 МБ)…");
                await Models.Download(Models.Vosk, p => Application.Current.Dispatcher.BeginInvoke(() => status($"Скачиваю распознавание речи: {p:P0}")));
            }
            _model ??= await Task.Run(() => { Vosk.Vosk.SetLogLevel(-1); return new Model(Models.PathOf(Models.Vosk)); });
        }
        catch (Exception e) { Log.Error(e); status("Не получилось подготовить распознавание: " + e.Message); return ""; }

        var stop = new TaskCompletionSource();
        _stop = stop;
#if JARVIS
        bool wasMuted = Ears.Muted; Ears.Muted = true;   // Jarvis must not take a dictated note for a command
#endif
        try
        {
            using var rec = new VoskRecognizer(_model, Rate);
            using var mic = new WaveInEvent { WaveFormat = new WaveFormat(Rate, 16, 1), BufferMilliseconds = 100 };
            var heard = false; var lastVoice = DateTime.Now; var began = DateTime.Now;
            var sync = new object();
            mic.DataAvailable += (_, a) =>
            {
                lock (sync) rec.AcceptWaveform(a.Buffer, a.BytesRecorded);
                if (Level(a.Buffer, a.BytesRecorded) > 500) { heard = true; lastVoice = DateTime.Now; }
                if ((heard && (DateTime.Now - lastVoice).TotalSeconds > 3) || (!heard && (DateTime.Now - began).TotalSeconds > 10) || (DateTime.Now - began).TotalSeconds > 60) stop.TrySetResult();
            };
            mic.StartRecording();
            await stop.Task;
            mic.StopRecording();
            await Task.Delay(150);
            lock (sync)
            {
                using var doc = JsonDocument.Parse(rec.FinalResult());
                return doc.RootElement.GetProperty("text").GetString()?.Trim() ?? "";
            }
        }
        catch (Exception e) { Log.Error(e); status("Микрофон недоступен: " + e.Message); return ""; }
        finally
        {
            _stop = null;
#if JARVIS
            Ears.Muted = wasMuted;
#endif
        }
    }
}
