using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;

namespace MishkaHelper.Core;

/// <summary>Second batch of desktop widgets.</summary>
public static partial class Widgets
{
    /// <summary>Glyph per widget kind (Widgets page, radial menu).</summary>
    public static readonly Dictionary<string, string> Icons = new()
    {
        ["system"] = "", ["battery"] = "", ["clock"] = "", ["network"] = "",
        ["profiles"] = "", ["audio"] = "", ["launcher"] = "",
        ["notes"] = "", ["todo"] = "", ["timer"] = "", ["media"] = "", ["ping"] = "", ["mic"] = "",
        ["disks"] = "", ["calendar"] = "", ["currency"] = "", ["procs"] = "", ["fans"] = "",
        ["worldclock"] = "", ["countdown"] = "", ["events"] = "\uE787",
    };

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

    // ---------- music ----------
    static GlobalSystemMediaTransportControlsSessionManager? _media;
    static string? _mediaPick;   // AUMID the user switched to; null = whatever Windows considers current

    static string Fmt(TimeSpan t) => t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");

    static FontFamily Icon => (FontFamily)Application.Current.Resources["IconFont"];

    /// <summary>Music: cover (also as a blurred backdrop), title, progress with seeking, shuffle/repeat, the player's own volume, source switch.</summary>
    static (UIElement, Action<Sample>?, Action?) MediaWidget()
    {
        const double W = 290;
        var backdrop = new Image { Stretch = Stretch.UniformToFill, Opacity = 0.28, Width = W, Height = 160, Effect = new System.Windows.Media.Effects.BlurEffect { Radius = 28 }, IsHitTestVisible = false };
        var coverImg = new Image { Stretch = Stretch.UniformToFill };
        var coverIcon = new TextBlock { Text = "\uE8D6", FontFamily = Icon, FontSize = 34, Foreground = B("AccentBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var cover = new Border
        {
            Width = 88, Height = 88, CornerRadius = new CornerRadius(12), Background = new SolidColorBrush(Color.FromArgb(0x40, 255, 255, 255)),
            Child = new Grid { Children = { coverIcon, coverImg } }, ClipToBounds = true,
        };
        cover.Loaded += (_, _) => cover.Clip = new RectangleGeometry(new Rect(0, 0, 88, 88), 12, 12);

        var title = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, MaxHeight = 42, TextTrimming = TextTrimming.CharacterEllipsis };
        var artist = new TextBlock { FontSize = 12, Foreground = B("DimBrush"), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
        var source = new Button { Style = (Style)Application.Current.Resources["GhostButton"], Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, FontSize = 11, Margin = new Thickness(0, 4, 0, 0), ToolTip = "Переключить плеер" };
        var info = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Width = W - 100 };
        info.Children.Add(title); info.Children.Add(artist); info.Children.Add(source);
        var top = new StackPanel { Orientation = Orientation.Horizontal };
        top.Children.Add(cover); top.Children.Add(info);

        var progress = new Slider { Width = W, Minimum = 0, Maximum = 1, Margin = new Thickness(0, 10, 0, 0), IsMoveToPointEnabled = true };
        var pos = UiKit.Text("0:00", 10, true); var len = UiKit.Text("0:00", 10, true);
        var times = new DockPanel { Width = W };
        DockPanel.SetDock(len, Dock.Right); times.Children.Add(len); times.Children.Add(pos);

        var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 0) };
        GlobalSystemMediaTransportControlsSession? cur = null;
        Action reload = () => { };   // buttons refresh right away; set to Load below
        Button Btn(string glyph, Func<GlobalSystemMediaTransportControlsSession, Task> act, double size = 15, bool accent = false)
        {
            var b = new Button { Content = glyph, FontFamily = Icon, FontSize = size, Width = accent ? 54 : 42, Height = accent ? 42 : 36, Margin = new Thickness(3, 0, 3, 0) };
            if (accent) b.Style = (Style)Application.Current.Resources["AccentButton"];
            b.Click += async (_, _) =>
            {
                try
                {
                    if (cur != null) { await act(cur); await Task.Delay(150); reload(); }
                    else if (glyph == "\uE768") YandexMusic.MyWave();   // nothing playing: ▶ starts «Моя волна»
                }
                catch { }
            };
            btns.Children.Add(b);
            return b;
        }
        var shuffle = Btn("\uE8B1", async s => await s.TryChangeShuffleActiveAsync(!(s.GetPlaybackInfo().IsShuffleActive ?? false)), 13);
        Btn("\uE892", async s => await s.TrySkipPreviousAsync());
        var play = Btn("\uE768", async s => await s.TryTogglePlayPauseAsync(), 18, accent: true);
        Btn("\uE893", async s => await s.TrySkipNextAsync());
        var repeat = Btn("\uE8EE", async s =>
        {
            var m = s.GetPlaybackInfo().AutoRepeatMode ?? Windows.Media.MediaPlaybackAutoRepeatMode.None;
            await s.TryChangeAutoRepeatModeAsync(m switch
            {
                Windows.Media.MediaPlaybackAutoRepeatMode.None => Windows.Media.MediaPlaybackAutoRepeatMode.List,
                Windows.Media.MediaPlaybackAutoRepeatMode.List => Windows.Media.MediaPlaybackAutoRepeatMode.Track,
                _ => Windows.Media.MediaPlaybackAutoRepeatMode.None,
            });
        }, 13);

        var volIcon = new TextBlock { Text = "\uE767", FontFamily = Icon, FontSize = 13, Foreground = B("DimBrush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var vol = new Slider { Width = W - 24, Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center };
        var volRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed, ToolTip = "Громкость этого плеера" };
        volRow.Children.Add(volIcon); volRow.Children.Add(vol);
        AudioApp? player = null;
        vol.ValueChanged += (_, e) => { if (vol.IsMouseCaptureWithin && player != null) try { player.Session.SimpleAudioVolume.Volume = (float)(e.NewValue / 100); } catch { } };

        var body = new StackPanel();
        body.Children.Add(top); body.Children.Add(progress); body.Children.Add(times); body.Children.Add(btns); body.Children.Add(volRow);
        var root = Header("\uE8D6", "Музыка");
        // The backdrop takes exactly the size of the content; otherwise the picture's own size stretches the widget.
        body.SizeChanged += (_, e) => { backdrop.Width = e.NewSize.Width; backdrop.Height = e.NewSize.Height; };
        root.Children.Add(new Grid { Children = { backdrop, body }, ClipToBounds = true });
        root.Width = W + 4;

        // Seek when the user lets go of the slider.
        progress.PreviewMouseLeftButtonUp += async (_, _) =>
        {
            try { if (cur != null) await cur.TryChangePlaybackPositionAsync(TimeSpan.FromSeconds(progress.Value).Ticks); } catch { }
        };

        source.Click += (_, _) =>
        {
            var all = _media?.GetSessions().ToList() ?? new();
            if (all.Count < 2) return;
            int i = all.FindIndex(s => s.SourceAppUserModelId == cur?.SourceAppUserModelId);
            _mediaPick = all[(i + 1) % all.Count].SourceAppUserModelId;
            reload();
        };

        string shownKey = "";
        static string AppName(string aumid)
        {
            var a = aumid.Split('!')[0];
            a = a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? Path.GetFileNameWithoutExtension(a) : a.Split('.').Last().Split('_')[0];
            return a.Length > 0 ? char.ToUpper(a[0]) + a[1..] : "Плеер";
        }

        async void Load()
        {
            try
            {
                _media ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                var sessions = _media.GetSessions().ToList();
                cur = sessions.FirstOrDefault(s => s.SourceAppUserModelId == _mediaPick) ?? _media.GetCurrentSession() ?? sessions.FirstOrDefault();
                if (cur == null)
                {
                    title.Text = "Ничего не играет"; artist.Text = YandexMusic.Installed ? "▶ — «Моя волна» в Яндекс Музыке" : "Включи музыку в любом плеере"; source.Content = "";
                    coverImg.Source = backdrop.Source = null; coverIcon.Visibility = Visibility.Visible; shownKey = "";
                    progress.Maximum = 1; progress.Value = 0; pos.Text = len.Text = "0:00"; volRow.Visibility = Visibility.Collapsed;
                    return;
                }
                var p = await cur.TryGetMediaPropertiesAsync();
                var name = AppName(cur.SourceAppUserModelId);
                source.Content = sessions.Count > 1 ? $"{name}  ⇄ {sessions.Count}" : name;
                var key = $"{cur.SourceAppUserModelId}|{p.Title}|{p.Artist}";
                if (key != shownKey)
                {
                    shownKey = key;
                    title.Text = string.IsNullOrEmpty(p.Title) ? "Без названия" : p.Title;
                    artist.Text = string.IsNullOrEmpty(p.Artist) ? p.AlbumTitle : p.Artist;
                    BitmapImage? img = null;
                    if (p.Thumbnail != null)
                    {
                        using var ras = await p.Thumbnail.OpenReadAsync();
                        var ms = new MemoryStream();
                        await ras.AsStreamForRead().CopyToAsync(ms);
                        ms.Position = 0;
                        img = new BitmapImage();
                        img.BeginInit(); img.StreamSource = ms; img.DecodePixelWidth = 176; img.CacheOption = BitmapCacheOption.OnLoad; img.EndInit(); img.Freeze();
                    }
                    coverImg.Source = backdrop.Source = img;
                    coverIcon.Visibility = img == null ? Visibility.Visible : Visibility.Collapsed;
                    // The player's own volume: match the audio session by process name.
                    var lname = name.ToLowerInvariant();
                    player = Audio.Apps().FirstOrDefault(a => a.Name.ToLowerInvariant() is var n && n.Length > 2 && (n.Contains(lname) || lname.Contains(n)));
                    volRow.Visibility = player != null ? Visibility.Visible : Visibility.Collapsed;
                }
                if (player != null && !vol.IsMouseCaptureWithin) try { vol.Value = player.Session.SimpleAudioVolume.Volume * 100; } catch { }

                var pb = cur.GetPlaybackInfo();
                bool playing = pb.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
                play.Content = playing ? "\uE769" : "\uE768";
                shuffle.Opacity = pb.IsShuffleActive == true ? 1 : 0.45;
                shuffle.Visibility = pb.Controls.IsShuffleEnabled ? Visibility.Visible : Visibility.Collapsed;
                var rep = pb.AutoRepeatMode ?? Windows.Media.MediaPlaybackAutoRepeatMode.None;
                repeat.Content = rep == Windows.Media.MediaPlaybackAutoRepeatMode.Track ? "\uE8ED" : "\uE8EE";
                repeat.Opacity = rep == Windows.Media.MediaPlaybackAutoRepeatMode.None ? 0.45 : 1;
                repeat.Visibility = pb.Controls.IsRepeatEnabled ? Visibility.Visible : Visibility.Collapsed;

                // Players report the position only now and then: move it forward ourselves while playing.
                var tl = cur.GetTimelineProperties();
                var end = tl.EndTime - tl.StartTime;
                var at = tl.Position - tl.StartTime + (playing ? DateTimeOffset.Now - tl.LastUpdatedTime : TimeSpan.Zero);
                if (at > end) at = end;
                bool hasTime = end.TotalSeconds > 1;
                progress.Visibility = times.Visibility = hasTime ? Visibility.Visible : Visibility.Collapsed;
                progress.IsEnabled = pb.Controls.IsPlaybackPositionEnabled;
                if (hasTime && !progress.IsMouseCaptureWithin)
                {
                    progress.Maximum = end.TotalSeconds; progress.Value = Math.Max(0, at.TotalSeconds);
                    pos.Text = Fmt(at < TimeSpan.Zero ? TimeSpan.Zero : at); len.Text = Fmt(end);
                }
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
    /// <summary>Upcoming week: Jarvis reminders + calendar events (ICS, e.g. Google's secret iCal link). ✓ marks a reminder done.</summary>
    static (UIElement, Action<Sample>?, Action?) EventsWidget()
    {
        var list = new StackPanel { Width = 280 };
        var ru = new System.Globalization.CultureInfo("ru-RU");
        string Day(DateTime d) => d.Date == DateTime.Today ? "Сегодня" : d.Date == DateTime.Today.AddDays(1) ? "Завтра" : d.ToString("ddd d MMM", ru);
        var root = Header("\uE787", "Ближайшие дела", list);
        var add = new Button { Content = "+ Добавить", Style = (Style)Application.Current.Resources["GhostButton"], HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0),
            ToolTip = "Напиши как есть: «в пятницу сдать отчёт», «завтра в 18:00 тренировка»" };
#if !LITE
        add.Click += (_, _) => JarvisInput.Toggle();
#else
        add.Visibility = Visibility.Collapsed;
#endif
        root.Children.Add(add);

        async void Load()
        {
            var items = Reminders.Upcoming(7).Select(r => (At: Reminders.RingAt(r), Time: r.HasTime, Text: r.Text, R: (Reminder?)r)).ToList();
#if !LITE
            if (Core.Calendar.Configured)
                for (int d = 0; d < 7; d++)
                    try { foreach (var e in await Core.Calendar.On(DateTime.Today.AddDays(d))) if (e.AllDay || e.Start >= DateTime.Now) items.Add((e.Start, !e.AllDay, e.Title + (e.Place.Length > 0 ? $" · {e.Place}" : ""), null)); }
                    catch { }
#endif
            list.Children.Clear();
            if (items.Count == 0) { list.Children.Add(UiKit.Text("На неделю ничего. Скажи или напиши Джарвису: «в пятницу сдать отчёт».", 12, true)); return; }
            string? lastDay = null;
            foreach (var it in items.OrderBy(i => i.At).Take(10))
            {
                var day = Day(it.At);
                if (day != lastDay)
                {
                    var h = UiKit.Text(day, 11, true); h.Margin = new Thickness(0, lastDay == null ? 0 : 8, 0, 2); h.FontWeight = FontWeights.SemiBold;
                    list.Children.Add(h);
                    lastDay = day;
                }
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                if (it.R is { } r)
                {
                    var done = new Button { Content = "\uE73E", FontFamily = Icon, Style = (Style)Application.Current.Resources["GhostButton"], ToolTip = "Сделано — убрать", Padding = new Thickness(4, 0, 4, 0) };
                    done.Click += (_, _) => { Reminders.Remove(r); Load(); };
                    DockPanel.SetDock(done, Dock.Right); row.Children.Add(done);
                }
                var time = UiKit.Text(it.Time ? it.At.ToString("H:mm") : "весь день", 11, true); time.Width = 62; DockPanel.SetDock(time, Dock.Left);
                row.Children.Add(time);
                var txt = new TextBlock { Text = it.Text, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = it.Text, VerticalAlignment = VerticalAlignment.Center,
                    Foreground = it.R != null ? B("TextBrush") : B("AccentBrush") };   // accent = from your calendar
                row.Children.Add(txt);
                list.Children.Add(row);
            }
        }
        return (root, null, Load);
    }
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
