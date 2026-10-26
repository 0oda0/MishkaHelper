using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace MishkaHelper.Core;

/// <summary>Floating neon orb: pulses while listening/thinking/speaking, shows a speech bubble. Click = talk.</summary>
public static class JarvisOrb
{
    static Window? _w;
    static Ellipse? _core, _ring;
    static Border? _bubble;
    static TextBlock? _text;
    static readonly DispatcherTimer HideBubble = new() { Interval = TimeSpan.FromSeconds(7) };
    static bool _hooked;

    static Brush Accent => (Brush)Application.Current.Resources["AccentBrush"];
    static Color AccentColor => (Color)Application.Current.Resources["AccentColor"];

    public static void Hide() { _w?.Close(); _w = null; }

    public static void Show()
    {
        if (_w != null) return;
        var w = Floating.Create(clickThrough: false);
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _text = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 300, FontSize = 13, Foreground = (Brush)Application.Current.Resources["TextBrush"] };
        _bubble = new Border
        {
            Child = _text, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(0, 0, 10, 0),
            Background = new SolidColorBrush(Color.FromArgb(0xEE, 16, 16, 28)), BorderBrush = Accent, BorderThickness = new Thickness(1),
            VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed,
        };
        grid.Children.Add(_bubble);

        var orb = new Grid { Width = 74, Height = 74, Margin = new Thickness(16), Cursor = Cursors.Hand, ToolTip = "Джарвис: клик — говорить, правый клик — меню" };
        _ring = new Ellipse { Stroke = Accent, StrokeThickness = 2, Opacity = 0.5 };
        _core = new Ellipse
        {
            Margin = new Thickness(10),
            Fill = new RadialGradientBrush(Color.FromArgb(0xFF, 255, 255, 255), AccentColor) { GradientOrigin = new Point(0.35, 0.3) },
            Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = AccentColor, BlurRadius = 30, ShadowDepth = 0, Opacity = 0.9 },
        };
        orb.Children.Add(_ring);
        orb.Children.Add(_core);
        Grid.SetColumn(orb, 1);
        grid.Children.Add(orb);
        w.Content = grid;

        // Drag to move; a click without moving talks; double click opens the chat.
        orb.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2) { JarvisChat.Open(); return; }
            double x = w.Left, y = w.Top;
            try { w.DragMove(); } catch { }
            if (Math.Abs(w.Left - x) < 3 && Math.Abs(w.Top - y) < 3) Jarvis.Listen();
        };
        var menu = new ContextMenu();
        void Item(string h, Action a) { var mi = new MenuItem { Header = h }; mi.Click += (_, _) => a(); menu.Items.Add(mi); }
        Item("Чат с Джарвисом", JarvisChat.Open);
        Item("Настройки Джарвиса", () => App.Current.Navigate("Jarvis"));
        Item("Скрыть шар", () => { Settings.Current.Jarvis.ShowOrb = false; Settings.Save(); Hide(); });
        orb.ContextMenu = menu;

        var s = Settings.Current.Jarvis;
        var wa = SystemParameters.WorkArea;
        w.Left = s.OrbLeft >= 0 ? s.OrbLeft : wa.Right - 420;
        w.Top = s.OrbTop >= 0 ? s.OrbTop : wa.Bottom - 130;
        w.LocationChanged += (_, _) => { s.OrbLeft = w.Left; s.OrbTop = w.Top; };
        w.Closed += (_, _) => Settings.Save();
        _w = w;
        w.Show();
        // Keep the orb's right edge anchored when the bubble appears on its left.
        w.SizeChanged += (_, e) => { if (e.WidthChanged && e.PreviousSize.Width > 0) w.Left -= e.NewSize.Width - e.PreviousSize.Width; };

        if (!_hooked)
        {
            _hooked = true;
            Ears.StateChanged += st => Application.Current.Dispatcher.BeginInvoke(() => Animate(st == EarState.Listening ? "listen" : st == EarState.Thinking ? "think" : st == EarState.Off ? "off" : "idle"));
            Voice.Speaking += on => Application.Current.Dispatcher.BeginInvoke(() => Animate(on ? "speak" : Ears.State == EarState.Off ? "off" : "idle"));
            Jarvis.Status += t => Application.Current.Dispatcher.BeginInvoke(() => Bubble(t));
            HideBubble.Tick += (_, _) => { HideBubble.Stop(); if (_bubble != null) _bubble.Visibility = Visibility.Collapsed; };
        }
        Animate(Ears.State == EarState.Off ? "off" : "idle");
    }

    public static void Bubble(string text)
    {
        if (_bubble == null || _text == null || string.IsNullOrWhiteSpace(text)) return;
        _text.Text = text;
        _bubble.Visibility = Visibility.Visible;
        HideBubble.Stop(); HideBubble.Start();
    }

    static void Animate(string mode)
    {
        if (_core == null || _ring == null) return;
        var scale = new ScaleTransform(1, 1, 37, 37);
        _ring.RenderTransform = scale;
        _ring.RenderTransformOrigin = new Point(0.5, 0.5);
        _core.BeginAnimation(UIElement.OpacityProperty, null);
        switch (mode)
        {
            case "listen":
                var pulse = new DoubleAnimation(1, 1.35, TimeSpan.FromMilliseconds(600)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, pulse);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, pulse);
                _ring.Opacity = 1; _core.Opacity = 1;
                break;
            case "think":
                _core.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, 0.45, TimeSpan.FromMilliseconds(450)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever });
                _ring.Opacity = 0.8;
                break;
            case "speak":
                var talk = new DoubleAnimation(1, 1.15, TimeSpan.FromMilliseconds(180)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever };
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, talk);
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, talk);
                _ring.Opacity = 1; _core.Opacity = 1;
                break;
            case "off":
                _ring.Opacity = 0.2; _core.Opacity = 0.35;
                break;
            default:
                _ring.Opacity = 0.45; _core.Opacity = 0.85;
                break;
        }
    }
}

/// <summary>Chat window: type or talk; history loads only on request.</summary>
public static class JarvisChat
{
    static Window? _w;
    static StackPanel? _list;
    static ScrollViewer? _scroll;
    static bool _hooked;

    public static void Open()
    {
        if (_w != null) { _w.Activate(); return; }
        var res = Application.Current.Resources;
        _list = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
        _scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var input = new TextBox { FontSize = 14, Padding = new Thickness(10, 8, 10, 8), VerticalAlignment = VerticalAlignment.Center };
        var send = new Button { Content = "", FontFamily = (FontFamily)res["IconFont"], Style = (Style)res["AccentButton"], Margin = new Thickness(8, 0, 0, 0), ToolTip = "Отправить (Enter)" };
        var mic = new Button { Content = "", FontFamily = (FontFamily)res["IconFont"], Margin = new Thickness(8, 0, 0, 0), ToolTip = "Сказать голосом" };
        var bottom = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
        DockPanel.SetDock(mic, Dock.Right); DockPanel.SetDock(send, Dock.Right);
        bottom.Children.Add(mic); bottom.Children.Add(send); bottom.Children.Add(input);

        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var hist = new Button { Content = "История", Margin = new Thickness(0) };
        var clear = new Button { Content = "Очистить", Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(clear, Dock.Right); DockPanel.SetDock(hist, Dock.Right);
        top.Children.Add(clear); top.Children.Add(hist);
        top.Children.Add(new TextBlock { Text = "ДЖАРВИС", FontWeight = FontWeights.Black, FontSize = 18, Foreground = (Brush)res["AccentBrush"], Effect = (System.Windows.Media.Effects.Effect)res["Glow"], VerticalAlignment = VerticalAlignment.Center });

        var root = new DockPanel { Margin = new Thickness(16) };
        DockPanel.SetDock(top, Dock.Top); DockPanel.SetDock(bottom, Dock.Bottom);
        root.Children.Add(top); root.Children.Add(bottom); root.Children.Add(_scroll);

        var w = new Window
        {
            Title = "Джарвис", Width = 460, Height = 640, Content = root, Background = (Brush)res["BgBrush"], Foreground = (Brush)res["TextBrush"],
            FontFamily = (FontFamily)res["MainFont"], WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/app.ico")),
        };
        w.SourceInitialized += (_, _) => ThemeManager.ApplyBackdrop(w);

        async void Send()
        {
            var t = input.Text.Trim();
            if (t.Length == 0) return;
            input.Text = "";
            await Jarvis.Handle(t);
        }
        send.Click += (_, _) => Send();
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) Send(); };
        mic.Click += (_, _) => Jarvis.Listen();
        hist.Click += (_, _) =>
        {
            _list.Children.Clear();
            foreach (var l in ChatHistory.Load()) AddBubble(l.Text, l.FromUser, l.When);
        };
        clear.Click += (_, _) => { if (UiKit.Confirm("Удалить всю историю разговоров?")) { ChatHistory.Clear(); _list.Children.Clear(); } };

        foreach (var l in ChatHistory.Session) AddBubble(l.Text, l.FromUser, l.When);
        if (ChatHistory.Session.Count == 0)
            AddBubble((Settings.Current.Jarvis.Enabled ? "Привет! Напиши или скажи, что сделать." : "Привет! Голос выключен, но писать мне можно. Включить голос — на странице «Джарвис».") +
                      "\nНапример: «громкость 30», «открой дискорд», «запиши в заметку купить молоко», «почему тормозит».", false, DateTime.Now);

        if (!_hooked)
        {
            _hooked = true;
            Jarvis.Message += (t, user) => Application.Current.Dispatcher.BeginInvoke(() => { if (_list != null) AddBubble(t, user, DateTime.Now); });
        }
        w.Closed += (_, _) => { _w = null; _list = null; };
        _w = w;
        w.Show();
        input.Focus();
    }

    static void AddBubble(string text, bool user, DateTime when)
    {
        if (_list == null) return;
        var res = Application.Current.Resources;
        var tb = new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14,
            Foreground = user ? new SolidColorBrush(Color.FromRgb(10, 10, 18)) : (Brush)res["TextBrush"],
        };
        var b = new Border
        {
            Child = tb, CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 8, 12, 8), Margin = new Thickness(user ? 50 : 0, 3, user ? 0 : 50, 3),
            Background = user ? (Brush)res["AccentBrush"] : (Brush)res["CardBrush"], HorizontalAlignment = user ? HorizontalAlignment.Right : HorizontalAlignment.Left,
            ToolTip = when.ToString("dd.MM HH:mm"),
        };
        _list.Children.Add(b);
        _scroll?.ScrollToEnd();
    }
}

/// <summary>
/// Ctrl+Alt+Enter anywhere: one line to Jarvis in plain words — "через 5 минут запусти фотошоп",
/// "в воскресенье сделать лабу". The answer comes back as a quiet toast, nothing is spoken.
/// </summary>
public static class JarvisInput
{
    static Window? _w;

    public static void Toggle()
    {
        if (_w != null) { _w.Close(); return; }
        var w = Floating.Create(clickThrough: false, noActivate: false);
        var box = new TextBox { FontSize = 17, MinWidth = 600, Padding = new Thickness(12, 10, 12, 10), BorderThickness = new Thickness(0) };
        var sp = new StackPanel();
        sp.Children.Add(UiKit.WithHint(box, "Например: через 5 минут запусти фотошоп и включи режим работы"));
        sp.Children.Add(UiKit.Text("Enter — отправить · Esc — закрыть · команды, напоминания, вопросы", 11, true));
        var panel = Floating.Panel(sp, 16);
        panel.Padding = new Thickness(12);
        w.Content = panel;

        box.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape) { w.Close(); return; }
            if (e.Key != Key.Enter || box.Text.Trim().Length == 0) return;
            e.Handled = true;
            var text = box.Text.Trim();
            w.Close();
            var answer = await Jarvis.HandleTyped(text);
            Toast.Show("\uE720", "Джарвис", answer.Length > 0 ? answer : "Готово.", JarvisChat.Open);
        };
        bool closing = false;
        w.Closing += (_, _) => closing = true;
        w.Deactivated += (_, _) => { if (!closing && box.Text.Length == 0) w.Close(); };   // keep a half-typed line
        w.Closed += (_, _) => _w = null;
        _w = w;
        w.Show();
        var wa = SystemParameters.WorkArea;
        w.Left = wa.Left + (wa.Width - w.ActualWidth) / 2;
        w.Top = wa.Top + wa.Height * 0.22;
        Native.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(w).Handle);
        w.Activate();
        box.Focus();
    }
}
