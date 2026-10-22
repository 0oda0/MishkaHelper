using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Windows.Media.Control;

namespace MishkaHelper.Core;

/// <summary>Second batch of desktop widgets.</summary>
public static partial class Widgets
{
    static Brush B(string key) => (Brush)Application.Current.Resources[key];

    static bool IsInteractive(DependencyObject? d)
    {
        for (; d != null; d = d is Visual or System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (d is ButtonBase or TextBoxBase or Slider or ComboBox or ScrollBar) return true;
        return false;
    }

    // ---------- notes ----------
    static (UIElement, Action<Sample>?, Action?) NotesWidget()
    {
        var tb = new TextBox
        {
            Text = Settings.Current.Notes, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Width = 260, MinHeight = 140, MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = 13,
        };
        var save = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        save.Tick += (_, _) => { save.Stop(); Settings.Current.Notes = tb.Text; Settings.Save(); };
        tb.TextChanged += (_, _) => { save.Stop(); save.Start(); };
        return (Header("", "Заметки", tb), null, null);
    }

    // ---------- todo ----------
    static (UIElement, Action<Sample>?, Action?) TodoWidget()
    {
        var list = new StackPanel { Width = 260 };
        var add = new TextBox { Margin = new Thickness(0, 8, 0, 0), FontSize = 13 };
        void Render()
        {
            list.Children.Clear();
            foreach (var t in Settings.Current.Todos.ToList())
            {
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                var del = new Button { Content = "", FontFamily = (FontFamily)Application.Current.Resources["IconFont"], FontSize = 10, Style = (Style)Application.Current.Resources["GhostButton"] };
                del.Click += (_, _) => { Settings.Current.Todos.Remove(t); Settings.Save(); Render(); };
                DockPanel.SetDock(del, Dock.Right);
                row.Children.Add(del);
                var cb = new CheckBox { Content = new TextBlock { Text = t.Text, TextWrapping = TextWrapping.Wrap, MaxWidth = 210, TextDecorations = t.Done ? TextDecorations.Strikethrough : null, Opacity = t.Done ? 0.5 : 1 }, IsChecked = t.Done };
                cb.Click += (_, _) => { t.Done = cb.IsChecked == true; Settings.Save(); Render(); };
                row.Children.Add(cb);
                list.Children.Add(row);
            }
            if (Settings.Current.Todos.Count == 0) list.Children.Add(UiKit.Text("Пусто. Напиши задачу ниже и нажми Enter", 12, true));
        }
        add.KeyDown += (_, e) =>
        {
            if (e.Key != Key.Enter || add.Text.Trim().Length == 0) return;
            Settings.Current.Todos.Add(new TodoItem { Text = add.Text.Trim() });
            Settings.Save(); add.Text = ""; Render();
        };
        Render();
        var root = Header("", "Дела", list);
        root.Children.Add(add);
        return (root, null, null);
    }

    // ---------- pomodoro / stopwatch ----------
    static (UIElement, Action<Sample>?, Action?) TimerWidget()
    {
        var big = new TextBlock { FontSize = 40, FontWeight = FontWeights.Bold, Foreground = B("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Center };
        var phase = UiKit.Text("Помодоро 25 / 5", 12, true); phase.HorizontalAlignment = HorizontalAlignment.Center;
        var btns = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        var start = new Button { Content = "Старт" };
        var reset = new Button { Content = "Сброс" };
        var mode = new Button { Content = "Секундомер" };
        btns.Children.Add(start); btns.Children.Add(reset); btns.Children.Add(mode);
        bool pomodoro = true, running = false, work = true;
        TimeSpan left = TimeSpan.FromMinutes(25), elapsed = TimeSpan.Zero;
        DateTime last = DateTime.Now;
        void Show()
        {
            big.Text = pomodoro ? left.ToString(@"mm\:ss") : elapsed.ToString(elapsed.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss\.f");
            phase.Text = pomodoro ? (work ? "Работа · 25 мин" : "Перерыв · 5 мин") : "Секундомер";
            start.Content = running ? "Пауза" : "Старт";
        }
        start.Click += (_, _) => { running = !running; last = DateTime.Now; Show(); };
        reset.Click += (_, _) => { running = false; work = true; left = TimeSpan.FromMinutes(25); elapsed = TimeSpan.Zero; Show(); };
        mode.Click += (_, _) => { pomodoro = !pomodoro; mode.Content = pomodoro ? "Секундомер" : "Помодоро"; running = false; Show(); };
        var fast = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        fast.Tick += (_, _) =>
        {
            if (!running) return;
            var now = DateTime.Now; var dt = now - last; last = now;
            if (pomodoro)
            {
                left -= dt;
                if (left <= TimeSpan.Zero)
                {
                    work = !work;
                    left = TimeSpan.FromMinutes(work ? 25 : 5);
                    Toast.Show("", work ? "Перерыв окончен" : "Время перерыва!", work ? "Снова за работу — 25 минут" : "Отдохни 5 минут, разомнись");
                    System.Media.SystemSounds.Exclamation.Play();
                }
            }
            else elapsed += dt;
            Show();
        };
        fast.Start();
        big.Unloaded += (_, _) => fast.Stop();
        Show();
        var root = Header("", "Таймер", big);
        root.Children.Add(phase); root.Children.Add(btns);
        return (root, null, null);
    }

    // ---------- media ----------
    static GlobalSystemMediaTransportControlsSessionManager? _media;

    static (UIElement, Action<Sample>?, Action?) MediaWidget()
    {
        var title = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Width = 250 };
        var artist = UiKit.Text("", 12, true);
        var app = UiKit.Text("", 10, true);
        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 8, 0, 0) };
        Button Btn(string g, Func<GlobalSystemMediaTransportControlsSession, Task> act)
        {
            var b = new Button { Content = g, FontFamily = (FontFamily)Application.Current.Resources["IconFont"], FontSize = 16, Width = 46 };
            b.Click += async (_, _) => { try { if (_media?.GetCurrentSession() is { } s) await act(s); } catch { } };
            btns.Children.Add(b);
            return b;
        }
        Btn("", async s => await s.TrySkipPreviousAsync());
        var play = Btn("", async s => await s.TryTogglePlayPauseAsync());
        Btn("", async s => await s.TrySkipNextAsync());
        var root = Header("", "Сейчас играет", title);
        root.Children.Add(artist); root.Children.Add(app); root.Children.Add(btns);
        async void Load()
        {
            try
            {
                _media ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var s = _media.GetCurrentSession();
                if (s == null) { title.Text = "Ничего не играет"; artist.Text = ""; app.Text = ""; return; }
                var p = await s.TryGetMediaPropertiesAsync();
                title.Text = string.IsNullOrEmpty(p.Title) ? "Без названия" : p.Title;
                artist.Text = p.Artist;
                app.Text = s.SourceAppUserModelId.Split('!')[0].Split('.').LastOrDefault() ?? "";
                play.Content = s.GetPlaybackInfo().PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing ? "" : "";
            }
            catch { title.Text = "Медиа недоступно"; }
        }
        return (root, null, Load);
    }

    // ---------- ping ----------
    static (UIElement, Action<Sample>?, Action?) PingWidget()
    {
        var chart = new Sparkline { Width = 230, Height = 50, Max = 0 };
        var txt = UiKit.Text("…", 14, weight: FontWeights.SemiBold);
        var sub = UiKit.Text(Settings.Current.PingHost, 11, true);
        var hist = new List<double>();
        int lost = 0, total = 0;
        var root = Header("", "Пинг", txt);
        root.Children.Add(chart); root.Children.Add(sub);
        async void Load()
        {
            var ms = await Toolbox.Ping(Settings.Current.PingHost);
            total++;
            if (ms < 0) lost++;
            hist.Add(Math.Max(0, ms));
            if (hist.Count > 60) hist.RemoveAt(0);
            chart.Values = hist.ToArray(); chart.InvalidateVisual();
            txt.Text = ms < 0 ? "Нет ответа" : $"{ms} мс";
            txt.Foreground = B(ms < 0 || ms > 150 ? "BadBrush" : ms > 60 ? "WarnBrush" : "GoodBrush");
            sub.Text = $"{Settings.Current.PingHost} · потери {lost * 100 / Math.Max(1, total)}% · ср. {(hist.Count > 0 ? hist.Where(h => h > 0).DefaultIfEmpty(0).Average() : 0):0} мс";
        }
        return (root, null, Load);
    }

    // ---------- mic ----------
    static (UIElement, Action<Sample>?, Action?) MicWidget()
    {
        var icon = new TextBlock { FontFamily = (FontFamily)Application.Current.Resources["IconFont"], FontSize = 34, HorizontalAlignment = HorizontalAlignment.Center };
        var txt = UiKit.Text("", 13, weight: FontWeights.SemiBold); txt.HorizontalAlignment = HorizontalAlignment.Center;
        var btn = new Button { Content = new StackPanel { Children = { icon, txt } }, Style = (Style)Application.Current.Resources["GhostButton"], Width = 140, ToolTip = "Клик — вкл/выкл микрофон" };
        btn.Click += (_, _) => { Audio.MicMuted = !Audio.MicMuted; };
        void Load()
        {
            bool m = Audio.MicMuted;
            icon.Text = m ? "" : "";
            icon.Foreground = B(m ? "BadBrush" : "GoodBrush");
            txt.Text = m ? "Микрофон выкл" : "Микрофон вкл";
        }
        return (btn, null, Load);
    }

    // ---------- disks ----------
    static (UIElement, Action<Sample>?, Action?) DisksWidget()
    {
        var list = new StackPanel { Width = 240 };
        void Load()
        {
            list.Children.Clear();
            foreach (var d in SystemInfo.Drives())
            {
                double used = 1 - (double)d.AvailableFreeSpace / d.TotalSize;
                list.Children.Add(UiKit.Text($"{d.Name.TrimEnd('\\')}  свободно {Cleaner.Size(d.AvailableFreeSpace)}", 12));
                var pb = new ProgressBar { Value = used * 100, Margin = new Thickness(0, 3, 0, 8), Height = 5 };
                if (used > 0.9) pb.Foreground = B("BadBrush");
                list.Children.Add(pb);
            }
        }
        return (Header("", "Диски", list), null, Load);
    }

    // ---------- calendar ----------
    static (UIElement, Action<Sample>?, Action?) CalendarWidget()
    {
        var grid = new UniformGrid { Columns = 7, Width = 238 };
        var head = UiKit.Text("", 14, weight: FontWeights.SemiBold);
        var ru = new System.Globalization.CultureInfo("ru-RU");
        DateTime shown = DateTime.MinValue;
        void Load()
        {
            var today = DateTime.Today;
            if (shown == today) return;
            shown = today;
            head.Text = ru.TextInfo.ToTitleCase(today.ToString("MMMM yyyy", ru));
            grid.Children.Clear();
            foreach (var d in new[] { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" })
                grid.Children.Add(new TextBlock { Text = d, FontSize = 11, Foreground = B("DimBrush"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 4) });
            var first = new DateTime(today.Year, today.Month, 1);
            int offset = ((int)first.DayOfWeek + 6) % 7;
            for (int i = 0; i < offset; i++) grid.Children.Add(new Border());
            for (int day = 1; day <= DateTime.DaysInMonth(today.Year, today.Month); day++)
            {
                bool isToday = day == today.Day;
                var dow = new DateTime(today.Year, today.Month, day).DayOfWeek;
                var tb = new TextBlock
                {
                    Text = day.ToString(), FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = isToday ? new SolidColorBrush(Color.FromRgb(10, 10, 18)) : dow is DayOfWeek.Saturday or DayOfWeek.Sunday ? B("Accent2Brush") : B("TextBrush"),
                    FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                };
                grid.Children.Add(new Border { Child = tb, Height = 26, CornerRadius = new CornerRadius(13), Background = isToday ? B("AccentBrush") : Brushes.Transparent });
            }
        }
        var root = Header("", "Календарь", head);
        root.Children.Add(grid);
        return (root, null, Load);
    }

    // ---------- currency ----------
    static readonly HttpClient Http2 = new() { Timeout = TimeSpan.FromSeconds(10) };

    static (UIElement, Action<Sample>?, Action?) CurrencyWidget()
    {
        var list = new StackPanel { Width = 200 };
        async void Load()
        {
            try
            {
                using var doc = JsonDocument.Parse(await Http2.GetStringAsync("https://www.cbr-xml-daily.ru/daily_json.js"));
                var v = doc.RootElement.GetProperty("Valute");
                list.Children.Clear();
                foreach (var (code, sym) in new[] { ("USD", "$"), ("EUR", "€"), ("CNY", "¥"), ("KZT", "₸") })
                {
                    var c = v.GetProperty(code);
                    double val = c.GetProperty("Value").GetDouble() / c.GetProperty("Nominal").GetDouble(), prev = c.GetProperty("Previous").GetDouble() / c.GetProperty("Nominal").GetDouble();
                    var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                    var ch = UiKit.Text($"{(val >= prev ? "▲" : "▼")} {Math.Abs(val - prev):0.00}", 11);
                    ch.Foreground = B(val >= prev ? "BadBrush" : "GoodBrush"); DockPanel.SetDock(ch, Dock.Right); ch.VerticalAlignment = VerticalAlignment.Center;
                    row.Children.Add(ch);
                    row.Children.Add(UiKit.Text($"{sym} {code}   {val:0.00} ₽", 14, weight: FontWeights.SemiBold));
                    list.Children.Add(row);
                }
            }
            catch { if (list.Children.Count == 0) list.Children.Add(UiKit.Text("Нет связи с ЦБ", 12, true)); }
        }
        return (Header("", "Курсы ЦБ", list), null, Load);
    }

    // ---------- top processes ----------
    static (UIElement, Action<Sample>?, Action?) ProcsWidget()
    {
        var list = new StackPanel { Width = 250 };
        SystemInfo.TopProcesses();
        void Load()
        {
            list.Children.Clear();
            foreach (var p in SystemInfo.TopProcesses(6))
            {
                var row = new DockPanel();
                var v = UiKit.Text($"{p.Cpu:0.0}% · {p.RamMb:0} МБ", 11, true); DockPanel.SetDock(v, Dock.Right);
                row.Children.Add(v);
                row.Children.Add(new TextBlock { Text = p.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
                list.Children.Add(row);
            }
        }
        return (Header("", "Процессы", list), null, Load);
    }

    // ---------- Acer fans ----------
    static (UIElement, Action<Sample>?, Action?) FansWidget()
    {
        if (!Acer.Available) return (Header("", "Вентиляторы", UiKit.Text("Только для Acer Nitro/Predator", 12, true)), null, null);
        var txt = UiKit.Text("", 13);
        var modes = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
        foreach (var (m, n) in new[] { (Acer.FanMode.Auto, "Авто"), (Acer.FanMode.Max, "Макс") })
        {
            var b = new Button { Content = n, Padding = new Thickness(12, 4, 12, 4) };
            b.Click += (_, _) => { Acer.Fans = m; Osd.Show("", "Вентиляторы: " + n.ToLower()); };
            modes.Children.Add(b);
        }
        var root = Header("", "Вентиляторы", txt);
        root.Children.Add(modes);
        return (root, s => txt.Text = $"CPU {s.CpuTemp}°C · {s.CpuFan} об/мин\nGPU {s.GpuTemp}°C · {s.GpuFan} об/мин", null);
    }

    // ---------- world clock ----------
    static (UIElement, Action<Sample>?, Action?) WorldClockWidget()
    {
        var list = new StackPanel { Width = 220 };
        void Load()
        {
            list.Children.Clear();
            foreach (var id in Settings.Current.WorldClocks)
            {
                TimeZoneInfo tz;
                try { tz = TimeZoneInfo.FindSystemTimeZoneById(id); } catch { continue; }
                var t = TimeZoneInfo.ConvertTime(DateTime.Now, tz);
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var time = UiKit.Text(t.ToString("HH:mm"), 16, weight: FontWeights.SemiBold); DockPanel.SetDock(time, Dock.Right);
                row.Children.Add(time);
                row.Children.Add(UiKit.Text(id.Split('/').Last().Replace('_', ' '), 13, true));
                list.Children.Add(row);
            }
        }
        return (Header("", "Мировое время", list), null, Load);
    }

    // ---------- countdown ----------
    static (UIElement, Action<Sample>?, Action?) CountdownWidget()
    {
        var big = new TextBlock { FontSize = 30, FontWeight = FontWeights.Bold, Foreground = B("AccentBrush") };
        var sub = UiKit.Text("", 12, true);
        var root = Header("", Settings.Current.CountdownName, big);
        root.Children.Add(sub);
        void Load()
        {
            var left = Settings.Current.CountdownTo - DateTime.Now;
            if (left < TimeSpan.Zero) { big.Text = "Наступило!"; sub.Text = ""; return; }
            big.Text = $"{left.Days} д {left.Hours:00}:{left.Minutes:00}:{left.Seconds:00}";
            sub.Text = Settings.Current.CountdownTo.ToString("d MMMM yyyy", new System.Globalization.CultureInfo("ru-RU"));
        }
        return (root, null, Load);
    }
}
