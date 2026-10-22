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
    record Item(string Icon, string Label, Action? Run = null, Func<List<Item>>? Sub = null);

    static Window? _w;
    const double Size = 480, Inner = 78, Outer = 210;

    public static void Toggle()
    {
        if (_w != null) { _w.Close(); return; }
        Open();
    }

    static List<Item> Root()
    {
        var l = new List<Item>
        {
            new("", "Профили", Sub: () => Settings.Current.Profiles.Select(p => new Item(p.Icon, p.Name, () => Profiles.Apply(p))).ToList()),
            new("", "Звук", Sub: () => Audio.Devices(DataFlow.Render).Select(d => new Item(d.IsDefault ? "" : "", Short(d.Name), () => Actions.Run("audio:" + d.Id))).ToList()),
            new("", "Программы", Sub: () => Settings.Current.Launchers.Select((x, i) => new Item("", x.Name, () => Actions.Run("launch:" + i))).ToList()),
            new("", "Ускорить", () => Actions.Run("boost")),
            new("", "Микрофон", () => Actions.Run("micMute")),
            new("", "Не беспокоить", () => Actions.Run("dnd")),
            new("", "Режим", Sub: () => Acer.Available
                ? Acer.SupportedModes().Select(m => new Item("", m.Name, () => { Acer.Mode = m.Id; Osd.Show("", "Режим: " + m.Name); })).ToList()
                : Power.Modes.Select(m => new Item("", m.Name, () => { Power.Mode = m.Key; Osd.Show("", "Питание: " + m.Name); })).ToList()),
            new("", "Скриншот", () => Actions.Run("screenshot")),
            new("", "Виджеты", () => Actions.Run("widgets")),
            new("", "Wi-Fi", () => Actions.Run("wifi")),
            new("", "Диспетчер", () => Actions.Run("taskmgr")),
        };
        if (Acer.Available) l.Insert(7, new("", "Вентиляторы", () => Actions.Run("fanMax")));
        return l;
    }

    static string Short(string s) => s.Length > 18 ? s[..17] + "…" : s;

    static void Open()
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
            if (it.Sub != null) { stack.Push(items); items = it.Sub(); Render(); return; }
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
