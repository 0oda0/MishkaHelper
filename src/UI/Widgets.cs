using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using NAudio.CoreAudioApi;

namespace MishkaHelper.Core;

/// <summary>Desktop widgets: draggable, pinned to desktop or always on top, per-widget opacity.</summary>
public static partial class Widgets
{
    public static readonly Dictionary<string, string> Names = new()
    {
        ["system"] = "Система (CPU / GPU / RAM / температуры)",
        ["battery"] = "Батарея и наушники",
        ["clock"] = "Часы и погода",
        ["network"] = "Сеть",
        ["profiles"] = "Профили",
        ["audio"] = "Звук",
        ["launcher"] = "Программы",
        ["notes"] = "Заметки",
        ["todo"] = "Список дел",
        ["timer"] = "Помодоро и секундомер",
        ["media"] = "Музыка",
        ["ping"] = "Пинг и интернет",
        ["mic"] = "Индикатор микрофона",
        ["disks"] = "Место на дисках",
        ["calendar"] = "Календарь",
        ["currency"] = "Курсы валют (ЦБ РФ)",
        ["procs"] = "Топ процессов",
        ["fans"] = "Вентиляторы Nitro",
        ["worldclock"] = "Мировое время",
        ["countdown"] = "Обратный отсчёт",
        ["events"] = "Ближайшие дела",
    };

    /// <summary>Widgets with text input need real focus; the rest never steal it.</summary>
    static bool NeedsFocus(string kind) => kind is "notes" or "todo";

    static readonly Dictionary<string, Window> Open = new();
    static IDisposable? _live;
    static bool _hiddenAll;

    public static bool IsOpen(string kind) => Open.ContainsKey(kind);

    public static void RestoreAll()
    {
        foreach (var c in Settings.Current.Widgets.Where(w => w.Visible)) Show(c.Kind);
    }

    public static void SaveAll()
    {
        foreach (var (kind, w) in Open)
            if (Config(kind) is { } c) { c.Left = w.Left; c.Top = w.Top; }
        Settings.Save();
    }

    public static void ToggleAll()
    {
        _hiddenAll = !_hiddenAll;
        if (Open.Count == 0 && _hiddenAll) { _hiddenAll = false; foreach (var c in Settings.Current.Widgets.Take(3)) Set(c.Kind, true); return; }
        foreach (var w in Open.Values) w.Visibility = _hiddenAll ? Visibility.Hidden : Visibility.Visible;
    }

    static WidgetConfig Config(string kind)
    {
        var c = Settings.Current.Widgets.FirstOrDefault(w => w.Kind == kind);
        if (c == null) { c = new WidgetConfig { Kind = kind }; Settings.Current.Widgets.Add(c); }
        return c;
    }

    public static void Set(string kind, bool visible)
    {
        Config(kind).Visible = visible;
        Settings.Save();
        if (visible) Show(kind);
        else if (Open.TryGetValue(kind, out var w)) w.Close();
    }

    public static void SetOpacity(string kind, double v)
    {
        Config(kind).Opacity = v;
        if (Open.TryGetValue(kind, out var w)) w.Opacity = v;
    }

    public static void Refresh(string kind)
    {
        if (kind == "clock") _weatherAt = default;
        if (!Open.TryGetValue(kind, out var w)) return;
        int at = Order.IndexOf(kind);
        w.Close();
        Show(kind);
        Order.Remove(kind); Order.Insert(Math.Min(at, Order.Count), kind);   // keep its place in the column
        Layout();
    }

    static void Show(string kind)
    {
        if (Open.ContainsKey(kind) || !Names.ContainsKey(kind)) return;
        var cfg = Config(kind);
        var w = Floating.Create(clickThrough: false, noActivate: !NeedsFocus(kind));
        w.Topmost = cfg.Topmost;
        w.Opacity = Math.Clamp(cfg.Opacity, 0.2, 1);
        var (content, onSample, slow) = Build(kind);
        var border = new Border
        {
            Child = content, CornerRadius = new CornerRadius(18), Padding = new Thickness(16, 14, 16, 14), Margin = new Thickness(14),
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 14, 14, 24)),
            BorderBrush = (Brush)Application.Current.Resources["AccentBrush"], BorderThickness = new Thickness(1),
            Effect = (System.Windows.Media.Effects.Effect)Application.Current.Resources["SoftGlow"],
        };
        w.Content = border;
        w.Left = cfg.Left; w.Top = cfg.Top;
        EnsureOnScreen(w);

        border.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState != MouseButtonState.Pressed || IsInteractive(e.OriginalSource as DependencyObject)) return;
            var from = new Point(w.Left, w.Top);
            try { w.DragMove(); } catch { }
            // A real drag takes the widget out of the column; the rest close the gap.
            if (Math.Abs(w.Left - from.X) + Math.Abs(w.Top - from.Y) > 6 && !cfg.Manual) { cfg.Manual = true; Settings.Save(); Layout(); }
        };
        w.LocationChanged += (_, _) => { cfg.Left = w.Left; cfg.Top = w.Top; };
        w.SizeChanged += (_, _) => Layout();
        border.ContextMenu = Menu(kind, w, cfg);

        Action<Sample>? handler = null;
        if (onSample != null)
        {
            handler = s => w.Dispatcher.BeginInvoke(() => onSample(s));
            SysMon.Updated += handler;
            onSample(SysMon.Last);
            if (Open.Values.Count(x => x.Tag is true) == 0) _live = SysMon.Live();
            w.Tag = true;
        }
        DispatcherTimer? t = null;
        if (slow != null)
        {
            slow();
            t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(kind switch
            {
                "clock" or "timer" or "worldclock" or "countdown" or "mic" or "media" => 1,
                "ping" or "procs" => 2,
                "currency" => 3600,
                "events" => 60,
                _ => 30,
            }) };
            t.Tick += (_, _) => slow();
            t.Start();
        }

        w.Closed += (_, _) =>
        {
            Open.Remove(kind);
            Order.Remove(kind);
            Layout();
            t?.Stop();
            if (handler != null) SysMon.Updated -= handler;
            if (Open.Values.All(x => x.Tag is not true)) { _live?.Dispose(); _live = null; }
            Settings.Save();
        };
        if (!cfg.Topmost)
            w.Activated += (_, _) => PinBottom(w);
        Open[kind] = w;
        Order.Add(kind);
        w.Show();
        if (!cfg.Topmost) PinBottom(w);
        Layout();
    }

    /// <summary>Opening order = order in the column.</summary>
    static readonly List<string> Order = new();
    const double Glow = 14;   // transparent margin around each card (for the glow)

    /// <summary>Stack non-dragged widgets from the top-right corner downwards; a full column continues to its left.</summary>
    public static void Layout()
    {
        var wa = SystemParameters.WorkArea;
        double right = wa.Right + Glow - 8, y = wa.Top - Glow + 8, colWidth = 0;
        foreach (var kind in Order)
        {
            if (!Open.TryGetValue(kind, out var w) || Config(kind).Manual || w.ActualHeight == 0) continue;
            if (y + w.ActualHeight > wa.Bottom + Glow && y > wa.Top) { right -= colWidth - Glow; y = wa.Top - Glow + 8; colWidth = 0; }
            double left = right - w.ActualWidth, top = y;
            if (Math.Abs(w.Left - left) > 0.5) w.Left = left;
            if (Math.Abs(w.Top - top) > 0.5) w.Top = top;
            y += w.ActualHeight - Glow;
            colWidth = Math.Max(colWidth, w.ActualWidth);
        }
    }

    public static void ToColumn(string kind) { Config(kind).Manual = false; Settings.Save(); Layout(); }

    static void PinBottom(Window w) =>
        Native.SetWindowPos(new WindowInteropHelper(w).Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);

    static void EnsureOnScreen(Window w)
    {
        var v = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop, SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!v.Contains(new Point(w.Left + 40, w.Top + 40))) { w.Left = 80; w.Top = 80; }
    }

    static ContextMenu Menu(string kind, Window w, WidgetConfig cfg)
    {
        var m = new ContextMenu();
        var top = new MenuItem { Header = "Поверх всех окон", IsCheckable = true, IsChecked = cfg.Topmost };
        top.Click += (_, _) => { cfg.Topmost = top.IsChecked; Refresh(kind); };
        m.Items.Add(top);
        var op = new MenuItem { Header = "Прозрачность" };
        foreach (var v in new[] { 1.0, 0.9, 0.75, 0.6, 0.45 })
        {
            var i = new MenuItem { Header = $"{v * 100:0}%", IsCheckable = true, IsChecked = Math.Abs(cfg.Opacity - v) < 0.01 };
            i.Click += (_, _) => { cfg.Opacity = v; w.Opacity = v; Settings.Save(); };
            op.Items.Add(i);
        }
        m.Items.Add(op);
        var col = new MenuItem { Header = "Вернуть в колонку справа" };
        col.Click += (_, _) => ToColumn(kind);
        m.Opened += (_, _) => col.Visibility = cfg.Manual ? Visibility.Visible : Visibility.Collapsed;
        m.Items.Add(col);
        var hide = new MenuItem { Header = "Скрыть виджет" };
        hide.Click += (_, _) => Set(kind, false);
        m.Items.Add(hide);
        return m;
    }

    // ---------- builders ----------

    static (UIElement, Action<Sample>?, Action?) Build(string kind) => kind switch
    {
        "system" => SystemWidget(),
        "battery" => BatteryWidget(),
        "clock" => ClockWidget(),
        "network" => NetworkWidget(),
        "profiles" => (Header("", "Профили", UiKit.ProfileButtons()), null, null),
        "audio" => AudioWidget(),
        "launcher" => LauncherWidget(),
        "notes" => NotesWidget(),
        "todo" => TodoWidget(),
        "timer" => TimerWidget(),
        "media" => MediaWidget(),
        "ping" => PingWidget(),
        "mic" => MicWidget(),
        "disks" => DisksWidget(),
        "calendar" => CalendarWidget(),
        "currency" => CurrencyWidget(),
        "procs" => ProcsWidget(),
        "fans" => FansWidget(),
        "worldclock" => WorldClockWidget(),
        "countdown" => CountdownWidget(),
        "events" => EventsWidget(),
        _ => (new TextBlock(), null, null),
    };

    static StackPanel Header(string icon, string title, UIElement? body = null)
    {
        var sp = new StackPanel { MinWidth = 200 };
        var h = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        h.Children.Add(new TextBlock { Text = icon, FontFamily = (FontFamily)Application.Current.Resources["IconFont"], Foreground = (Brush)Application.Current.Resources["AccentBrush"], FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
        h.Children.Add(new TextBlock { Text = title.ToUpperInvariant(), FontSize = 11, FontWeight = FontWeights.Bold, Margin = new Thickness(8, 0, 0, 0), Foreground = (Brush)Application.Current.Resources["DimBrush"] });
        sp.Children.Add(h);
        if (body != null) sp.Children.Add(body);
        return sp;
    }

    static (UIElement, Action<Sample>?, Action?) SystemWidget()
    {
        var rings = new StackPanel { Orientation = Orientation.Horizontal };
        Ring R(string cap) { var r = new Ring { Width = 78, Height = 78, Caption = cap, Margin = new Thickness(0, 0, 8, 0), Thickness = 6 }; rings.Children.Add(r); return r; }
        var cpu = R("CPU"); var gpu = R("GPU"); var ram = R("RAM");
        var info = UiKit.Text("", 12, true);
        info.Margin = new Thickness(0, 10, 0, 0);
        var root = Header("", "Система", rings);
        root.Children.Add(info);
        return (root, s =>
        {
            cpu.Value = s.Cpu; cpu.Label = $"{s.Cpu:0}%"; cpu.InvalidateVisual();
            gpu.Value = s.Gpu; gpu.Label = $"{s.Gpu:0}%"; gpu.InvalidateVisual();
            ram.Value = s.Ram; ram.Label = $"{s.Ram:0}%"; ram.InvalidateVisual();
            info.Text = (s.CpuTemp > 0 ? $"CPU {s.CpuTemp}°C" + (s.GpuTemp > 0 ? $"   GPU {s.GpuTemp}°C" : "") : "") +
                        (s.CpuFan > 0 ? $"\nВентиляторы {s.CpuFan} / {s.GpuFan} об/мин" : "") +
                        $"\nRAM {s.RamUsedGb:0.0} / {s.RamTotalGb:0} ГБ";
        }, null);
    }

    static (UIElement, Action<Sample>?, Action?) BatteryWidget()
    {
        var list = new StackPanel();
        var root = Header("", "Заряд", list);
        async void Load()
        {
            var rows = new List<(string, string, int?)>();
            var b = Power.Battery();
            if (b.Present) rows.Add(("", b.OnAc ? (b.Charging ? "Ноутбук · заряжается" : "Ноутбук · от сети") : "Ноутбук", b.Percent));
            try
            {
                foreach (var d in (await Devices.List()).Where(d => d.Connected && d.Battery != null))
                    rows.Add((d.Icon, d.Name, d.Battery));
            }
            catch { }
            list.Children.Clear();
            foreach (var (icon, name, pct) in rows)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 8), Width = 240 };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var ic = UiKit.Icon(icon, 15); g.Children.Add(ic);
                var col = new StackPanel(); Grid.SetColumn(col, 1);
                col.Children.Add(new TextBlock { Text = name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
                var pb = new ProgressBar { Value = pct ?? 0, Margin = new Thickness(0, 4, 8, 0), Height = 4 };
                if (pct < 20) pb.Foreground = (Brush)Application.Current.Resources["BadBrush"];
                col.Children.Add(pb);
                g.Children.Add(col);
                var t = UiKit.Text($"{pct}%", 13, weight: FontWeights.SemiBold); Grid.SetColumn(t, 2); g.Children.Add(t);
                list.Children.Add(g);
            }
            if (rows.Count == 0) list.Children.Add(UiKit.Text("Нет устройств с батареей", 12, true));
        }
        return (root, null, Load);
    }

    static string _weather = "";
    static DateTime _weatherAt;
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    static (UIElement, Action<Sample>?, Action?) ClockWidget()
    {
        var time = new TextBlock { FontSize = 44, FontWeight = FontWeights.Bold, Foreground = (Brush)Application.Current.Resources["AccentBrush"], Effect = (System.Windows.Media.Effects.Effect)Application.Current.Resources["Glow"] };
        var date = UiKit.Text("", 13, true);
        var weather = UiKit.Text("", 14);
        weather.Margin = new Thickness(0, 6, 0, 0);
        var sp = new StackPanel { MinWidth = 200 };
        sp.Children.Add(time); sp.Children.Add(date); sp.Children.Add(weather);
        var ru = new System.Globalization.CultureInfo("ru-RU");
        return (sp, null, () =>
        {
            var now = DateTime.Now;
            time.Text = now.ToString("HH:mm");
            date.Text = now.ToString("dddd, d MMMM", ru);
            weather.Text = _weather;
            if ((now - _weatherAt).TotalMinutes > 30) { _weatherAt = now; _ = LoadWeather(); }
        });
    }

    /// <summary>Current weather as one sentence (used by Jarvis).</summary>
    public static async Task<string> WeatherNow()
    {
        if ((DateTime.Now - _weatherAt).TotalMinutes > 20 || _weather.Length == 0) { _weatherAt = DateTime.Now; await LoadWeather(); }
        return _weather.Length == 0 ? "" : "Сейчас " + _weather.Replace("  ·  ", ", ").Replace("·", ",");
    }

    static async Task LoadWeather()
    {
        try
        {
            var city = Uri.EscapeDataString(Settings.Current.WeatherCity);
            using var geo = JsonDocument.Parse(await Http.GetStringAsync($"https://geocoding-api.open-meteo.com/v1/search?name={city}&count=1&language=ru"));
            var r = geo.RootElement.GetProperty("results")[0];
            var (lat, lon) = (r.GetProperty("latitude").GetDouble(), r.GetProperty("longitude").GetDouble());
            using var f = JsonDocument.Parse(await Http.GetStringAsync(FormattableString.Invariant(
                $"https://api.open-meteo.com/v1/forecast?latitude={lat}&longitude={lon}&current=temperature_2m,weather_code,wind_speed_10m")));
            var cur = f.RootElement.GetProperty("current");
            var t = cur.GetProperty("temperature_2m").GetDouble();
            var code = cur.GetProperty("weather_code").GetInt32();
            _weather = $"{WeatherText(code)}  {t:+0;-0;0}°  ·  {r.GetProperty("name").GetString()}";
        }
        catch { _weather = ""; _weatherAt = DateTime.Now.AddMinutes(-25); }
    }

    static string WeatherText(int c) => c switch
    {
        0 => "☀ Ясно", 1 or 2 => "⛅ Облачно", 3 => "☁ Пасмурно", 45 or 48 => "🌫 Туман",
        >= 51 and <= 57 => "🌦 Морось", >= 61 and <= 67 => "🌧 Дождь", >= 71 and <= 77 => "❄ Снег",
        >= 80 and <= 82 => "🌧 Ливень", 85 or 86 => "🌨 Снегопад", >= 95 => "⛈ Гроза", _ => "",
    };

    static (UIElement, Action<Sample>?, Action?) NetworkWidget()
    {
        var down = new Sparkline { Height = 46, Width = 230, Max = 0 };
        var up = new Sparkline { Height = 30, Width = 230, Max = 0, Stroke = (Brush)Application.Current.Resources["Accent2Brush"] };
        var txt = UiKit.Text("", 12);
        var root = Header("", "Сеть", down);
        root.Children.Add(up); root.Children.Add(txt);
        var hd = new List<double>(); var hu = new List<double>();
        return (root, s =>
        {
            hd.Add(s.NetDown); hu.Add(s.NetUp);
            if (hd.Count > 60) { hd.RemoveAt(0); hu.RemoveAt(0); }
            down.Values = hd.ToArray(); up.Values = hu.ToArray();
            down.InvalidateVisual(); up.InvalidateVisual();
            txt.Text = $"↓ {SysMon.Speed(s.NetDown)}    ↑ {SysMon.Speed(s.NetUp)}";
        }, null);
    }

    static (UIElement, Action<Sample>?, Action?) AudioWidget()
    {
        var cb = new ComboBox { Width = 240, DisplayMemberPath = "Name", Margin = new Thickness(0, 0, 0, 10) };
        var vol = new Slider { Maximum = 100, Width = 240 };
        bool loading = true;
        cb.SelectionChanged += (_, _) => { if (!loading && cb.Tag == null && cb.SelectedItem is AudioDevice d && !d.IsDefault) Audio.SetDefault(d.Id); };
        vol.ValueChanged += (_, e) => { if (!loading) Audio.Volume = (int)e.NewValue; };
        var root = Header("", "Звук", cb);
        root.Children.Add(vol);
        return (root, null, () =>
        {
            loading = true;
            if (!cb.IsDropDownOpen) UiKit.FillOutputs(cb);
            if (Mouse.LeftButton != MouseButtonState.Pressed) vol.Value = Audio.Volume;
            loading = false;
        });
    }

    static (UIElement, Action<Sample>?, Action?) LauncherWidget()
    {
        var wp = new WrapPanel { MaxWidth = 280 };
        for (int i = 0; i < Settings.Current.Launchers.Count; i++)
        {
            var l = Settings.Current.Launchers[i];
            int idx = i;
            var b = new Button { Content = l.Name };
            b.Click += (_, _) => Actions.Run("launch:" + idx);
            wp.Children.Add(b);
        }
        return (Header("", "Программы", wp), null, null);
    }
}
