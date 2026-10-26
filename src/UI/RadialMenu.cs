using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using NAudio.CoreAudioApi;

namespace MishkaHelper.Core;

/// <summary>
/// BeamNG-style pie menu around the cursor: point in a direction, click to pick.
/// Right click / centre = back, Esc = close.
/// </summary>
public static class RadialMenu
{
    record Item(string Icon, string Label, Action? Run = null, Func<List<Item>>? Sub = null, ImageSource? Img = null);

    const int PerPage = 11;   // more segments than this get unreadable → "Ещё" pages
    public const int RootMax = 16;   // the root ring has bigger segments; past this it pages too

    static List<Item> WidgetItems() =>
        new List<Item> { new("", "Все: показать / скрыть", () => Actions.Run("widgets")) }
            .Concat(Widgets.Names.Select(kv => new Item(Widgets.IsOpen(kv.Key) ? "" : Widgets.Icons.GetValueOrDefault(kv.Key, ""),
                (Widgets.IsOpen(kv.Key) ? "✓ " : "") + kv.Value.Split(" (")[0],
                () => Widgets.Set(kv.Key, !Widgets.IsOpen(kv.Key)))))
            .ToList();

    static Window? _w;
    const double Size = 480, Inner = 78, Outer = 210;

    public static void Toggle()
    {
        if (_w != null) { _w.Close(); return; }
        Open();
    }

    /// <summary>Dev/screenshots: open the wheel straight into a submenu by its label.</summary>
    public static void OpenAt(string label) { _w?.Close(); Open(label); }

    /// <summary>Every built-in root item: key (stored in settings), and the item. Order = default wheel.</summary>
    static List<(string Key, Item Item)> Builtins()
    {
        var l = new List<(string, Item)>
        {
            ("profiles", new("\uE77B", "Профили", Sub: () => Settings.Current.Profiles.Select(p => new Item(p.Icon, p.Name, () => Profiles.Apply(p))).ToList())),
#if !LITE
            // Labels show the state the click switches to, so the wheel doubles as an indicator.
            ("jarvis", new("\uE720", "Джарвис", Sub: () => new List<Item>
            {
                new("\uE720", "Слушать", () => Actions.Run("jarvis")),
                new("\uE7E8", Settings.Current.Jarvis.Enabled ? "Выключить" : "Включить", () => Actions.Run("jarvisToggle")),
                new("\uEA3A", Settings.Current.Jarvis.ShowOrb ? "Скрыть шар" : "Показать шар", () => Actions.Run("jarvisOrb")),
                new("\uE767", Settings.Current.Jarvis.Speak ? "Без голоса" : "С голосом", () => Actions.Run("jarvisVoice")),
                new("\uE8BD", "Чат", () => Actions.Run("jarvisChat")),
                new("\uE765", "Написать", () => Actions.Run("jarvisType")),
            })),
#endif
            ("sound", new("\uE7F5", "Звук", Sub: () => Audio.Devices(DataFlow.Render).Select(d => new Item(d.IsDefault ? "\uE73E" : "\uE7F5", Short(d.Name), () => Actions.Run("audio:" + d.Id))).ToList())),
            ("music", new("\uE8D6", "Музыка", Sub: () => new List<Item>
            {
                new("\uE768", "Играть / пауза", () => InputSim.Key("play")),
                new("\uE893", "Следующий", () => InputSim.Key("next")),
                new("\uE892", "Предыдущий", () => InputSim.Key("prev")),
                new("\uE93C", "Моя волна", YandexMusic.MyWave),
                new("\uE8D6", "Яндекс Музыка", () => YandexMusic.Open("/")),
                new("\uE7F4", "Виджет музыки", () => Widgets.Set("media", !Widgets.IsOpen("media"))),
            })),
            ("programs", new("\uE8A9", "Программы", Sub: () => Settings.Current.Launchers.Select((x, i) => new Item("\uE7AC", x.Name, () => Actions.Run("launch:" + i), Img: AppIcon.For(x))).ToList())),
            ("boost", new("\uE945", "Ускорить", () => Actions.Run("boost"))),
            ("mic", new("\uE720", "Микрофон", () => Actions.Run("micMute"))),
            ("dnd", new("\uE708", "Не беспокоить", () => Actions.Run("dnd"))),
            ("mode", new("\uE7E8", "Режим", Sub: () => Acer.Available
                ? Acer.SupportedModes().Select(m => new Item("\uE945", m.Name, () => { Acer.Mode = m.Id; Osd.Show("\uE945", "Режим: " + m.Name); })).ToList()
                : Power.Modes.Select(m => new Item("\uE945", m.Name, () => { Power.Mode = m.Key; Osd.Show("\uE945", "Питание: " + m.Name); })).ToList())),
            ("screenshot", new("\uE722", "Скриншот", () => Actions.Run("screenshot"))),
            ("tools", new("\uE90F", "Инструменты", Sub: () => new List<Item>
            {
                new("\uE718", "Окно поверх", () => Actions.Run("topmost")),
                new("\uEF3C", "Пипетка", () => Actions.Run("colorPick")),
                new("\uE708", "Не засыпать", () => Actions.Run("keepAwake")),
                new("\uE7E8", "Выключить экран", () => Actions.Run("screenOff")),
                new("\uE72E", "Заблокировать", () => Actions.Run("lock")),
                new("\uE74D", "Очистить корзину", () => Actions.Run("recycle")),
                new("\uE968", "Пинг", () => Actions.Run("ping")),
                new("\uE8D7", "Пароль в буфер", () => Actions.Run("password")),
                new("\uE721", "Палитра команд", () => Actions.Run("palette")),
            })),
            ("widgets", new("\uECA5", "Виджеты", Sub: WidgetItems)),
            ("wifi", new("\uE701", "Wi-Fi", () => Actions.Run("wifi"))),
            ("taskmgr", new("\uE9D9", "Диспетчер", () => Actions.Run("taskmgr"))),
        };
        if (Acer.Available) l.Insert(l.FindIndex(x => x.Item1 == "screenshot"), ("fans", new("\uE9CA", "Вентиляторы", () => Actions.Run("fanMax"))));
        return l;
    }

    /// <summary>For the editor page: built-in items as (key, icon, name).</summary>
    public static List<(string Key, string Icon, string Name)> Catalog() => Builtins().Select(b => (b.Key, b.Item.Icon, b.Item.Label)).ToList();

    /// <summary>The user's wheel (Settings.Radial) or the default one. Custom entries run any action from the palette.</summary>
    static List<Item> Root()
    {
        var builtins = Builtins();
        var cfg = Settings.Current.Radial;
        if (cfg.Count == 0) return Paged(builtins.Select(b => b.Item).ToList(), per: RootMax);
        var byKey = builtins.ToDictionary(b => b.Key, b => b.Item);
        return Paged(cfg.Select(e => e.Key.StartsWith("act:")
                ? new Item(e.Icon.Length > 0 ? e.Icon : "\uE768", e.Label, () => CommandPalette.RunAction(e.Key[4..]))
                : byKey.GetValueOrDefault(e.Key))
            .OfType<Item>().ToList(), per: RootMax);
    }

    /// <summary>Long submenus (20 widgets, many programs) show 11 items + "Ещё →"; Back walks the pages.</summary>
    static List<Item> Paged(List<Item> all, int page = 0, int per = PerPage)
    {
        if (all.Count <= per + 1) return all;
        var chunk = all.Skip(page * per).Take(per).ToList();
        if ((page + 1) * per < all.Count) chunk.Add(new Item("", "Ещё →", Sub: () => Paged(all, page + 1, per)));
        return chunk;
    }

    static string Short(string s) => s.Length > 18 ? s[..17] + "…" : s;

    static void Open(string? startIn = null)
    {
        var w = Floating.Create(clickThrough: false, noActivate: false);
        w.SizeToContent = SizeToContent.Manual;
        w.Width = w.Height = Size;
        Native.GetCursorPos(out var p);
        double scale = VisualTreeHelper.GetDpi(w).DpiScaleX;   // ponytail: system DPI; per-monitor offset if menus land wrong on a 2nd screen
        // Centre on the cursor but keep the whole ring on the screen the cursor is on.
        var wa = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(p.X, p.Y)).WorkingArea;
        w.Left = Math.Clamp(p.X / scale - Size / 2, wa.Left / scale, wa.Right / scale - Size);
        w.Top = Math.Clamp(p.Y / scale - Size / 2, wa.Top / scale, wa.Bottom / scale - Size);

        var canvas = new Canvas { Width = Size, Height = Size, Background = Brushes.Transparent };
        w.Content = canvas;
        var stack = new Stack<List<Item>>();
        List<Item> items = Root();
        var stack0 = startIn != null ? items.FirstOrDefault(x => x.Label == startIn) : null;
        int hover = -1;
        var paths = new List<Path>();
        var center = new TextBlock { FontSize = 15, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center, Width = Inner * 2 - 16, TextWrapping = TextWrapping.Wrap };

        void Render()
        {
            canvas.Children.Clear(); paths.Clear(); hover = -1;
            var hub = new Ellipse
            {
                Width = Inner * 2 - 8, Height = Inner * 2 - 8, Fill = new SolidColorBrush(Color.FromArgb(0xF0, 14, 14, 24)),
                Stroke = (Brush)Application.Current.Resources["AccentBrush"], StrokeThickness = 1.5, Effect = (System.Windows.Media.Effects.Effect)Application.Current.Resources["Glow"],
            };
            Canvas.SetLeft(hub, Size / 2 - Inner + 4); Canvas.SetTop(hub, Size / 2 - Inner + 4);
            canvas.Children.Add(hub);
            center.Text = stack.Count > 0 ? "Назад" : "MISHKA";
            canvas.Children.Add(center);
            center.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(center, Size / 2 - center.Width / 2); Canvas.SetTop(center, Size / 2 - center.DesiredSize.Height / 2);

            int n = Math.Max(items.Count, 1);
            double step = 360.0 / n;
            for (int i = 0; i < items.Count; i++)
            {
                double a0 = i * step - 90 - step / 2 + 1.2, a1 = a0 + step - 2.4;
                var path = new Path
                {
                    Data = Segment(a0, a1), Fill = new SolidColorBrush(Color.FromArgb(0xE8, 18, 18, 30)),
                    Stroke = (Brush)Application.Current.Resources["CardBorderBrush"], StrokeThickness = 1,
                };
                canvas.Children.Add(path); paths.Add(path);
                double mid = (a0 + a1) / 2 * Math.PI / 180, rr = (Inner + Outer) / 2;
                var label = new StackPanel { Width = 92, IsHitTestVisible = false };
                if (items[i].Img is { } img)
                    label.Children.Add(new Image { Source = img, Width = 30, Height = 30, HorizontalAlignment = HorizontalAlignment.Center });
                else
                    label.Children.Add(new TextBlock { Text = items[i].Icon, FontFamily = (FontFamily)Application.Current.Resources["IconFont"], FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, Foreground = (Brush)Application.Current.Resources["AccentBrush"] });
                label.Children.Add(new TextBlock { Text = items[i].Label, FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
                canvas.Children.Add(label);
                label.Measure(new System.Windows.Size(92, double.PositiveInfinity));
                Canvas.SetLeft(label, Size / 2 + rr * Math.Cos(mid) - 46);
                Canvas.SetTop(label, Size / 2 + rr * Math.Sin(mid) - label.DesiredSize.Height / 2);
            }
            if (items.Count == 0) center.Text = "Пусто\n(настрой в приложении)";
        }

        void Hover(Point m)
        {
            double dx = m.X - Size / 2, dy = m.Y - Size / 2, dist = Math.Sqrt(dx * dx + dy * dy);
            int idx = -1;
            if (dist > Inner - 8 && items.Count > 0)
            {
                double ang = (Math.Atan2(dy, dx) * 180 / Math.PI + 90 + 360.0 / items.Count / 2 + 720) % 360;
                idx = (int)(ang / (360.0 / items.Count)) % items.Count;
            }
            if (idx == hover) return;
            if (hover >= 0) { paths[hover].Fill = new SolidColorBrush(Color.FromArgb(0xE8, 18, 18, 30)); paths[hover].Effect = null; paths[hover].Stroke = (Brush)Application.Current.Resources["CardBorderBrush"]; }
            hover = idx;
            if (hover >= 0)
            {
                paths[hover].Fill = (Brush)Application.Current.Resources["AccentSoftBrush"];
                paths[hover].Stroke = (Brush)Application.Current.Resources["AccentBrush"];
                paths[hover].Effect = (System.Windows.Media.Effects.Effect)Application.Current.Resources["Glow"];
                center.Text = items[hover].Label;
            }
            else center.Text = stack.Count > 0 ? "Назад" : "MISHKA";
        }

        void Back()
        {
            if (stack.Count == 0) { w.Close(); return; }
            items = stack.Pop(); Render();
        }

        void Pick(int idx)
        {
            if (idx < 0 || idx >= items.Count) { Back(); return; }
            var it = items[idx];
            if (it.Sub != null) { stack.Push(items); items = Paged(it.Sub()); Render(); return; }
            w.Close();
            it.Run?.Invoke();
        }

        canvas.MouseMove += (_, e) => Hover(e.GetPosition(canvas));
        canvas.MouseLeftButtonUp += (_, e) => { Hover(e.GetPosition(canvas)); Pick(hover); };
        canvas.MouseRightButtonUp += (_, _) => Back();
        w.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) w.Close();
            else if (e.Key == Key.Back) Back();
            else if (e.Key is >= Key.D1 and <= Key.D9) Pick(e.Key - Key.D1);
        };
        bool closing = false;
        w.Closing += (_, _) => closing = true;
        w.Deactivated += (_, _) => { if (!closing) w.Close(); };
        w.Closed += (_, _) => _w = null;

        if (stack0?.Sub != null) { stack.Push(items); items = Paged(stack0.Sub()); }
        Render();
        canvas.RenderTransformOrigin = new Point(0.5, 0.5);
        var st = new ScaleTransform(0.6, 0.6);
        canvas.RenderTransform = st;
        canvas.Opacity = 0;
        _w = w;
        var fgAtOpen = Native.GetForegroundWindow();   // the app the user was in
        w.Show();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(w).Handle;
        // The hotkey owner may take the foreground; forcing it (AttachThreadInput) makes it flicker and lose focus again.
        Native.SetForegroundWindow(hwnd);
        w.Activate();
        Log.Info($"radial: shown at {w.Left:0},{w.Top:0} cursor={p.X},{p.Y} fgBefore={fgAtOpen} fgNow={Native.GetForegroundWindow()} me={hwnd}");

        // Safety net: if we didn't get focus, Deactivated never fires. Close once the user switches to some
        // *other* window than the one that was in front when the menu opened (i.e. they clicked away).
        var watch = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        watch.Tick += (_, _) =>
        {
            var fg = Native.GetForegroundWindow();
            if (fg != hwnd && fg != fgAtOpen && !closing) { Log.Info($"radial: focus moved to {fg}, closing"); w.Close(); }
        };
        w.Closed += (_, _) => watch.Stop();
        watch.Start();
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
        st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(160)) { EasingFunction = ease });
        canvas.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(120)));
    }

    static Geometry Segment(double a0, double a1)
    {
        Point P(double r, double a) => new(Size / 2 + r * Math.Cos(a * Math.PI / 180), Size / 2 + r * Math.Sin(a * Math.PI / 180));
        bool large = a1 - a0 > 180;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(P(Inner, a0), true, true);
            c.LineTo(P(Outer, a0), true, true);
            c.ArcTo(P(Outer, a1), new System.Windows.Size(Outer, Outer), 0, large, SweepDirection.Clockwise, true, true);
            c.LineTo(P(Inner, a1), true, true);
            c.ArcTo(P(Inner, a0), new System.Windows.Size(Inner, Inner), 0, large, SweepDirection.Counterclockwise, true, true);
        }
        g.Freeze();
        return g;
    }
}
