using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace MishkaHelper.Core;

/// <summary>Shared look for small floating windows (OSD, toasts, radial menu, widgets).</summary>
public static class Floating
{
    public static Window Create(bool clickThrough, bool noActivate = true)
    {
        var w = new Window
        {
            WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
            Topmost = true, ShowInTaskbar = false, ShowActivated = !noActivate, ResizeMode = ResizeMode.NoResize,
            SizeToContent = SizeToContent.WidthAndHeight, Foreground = (Brush)Application.Current.Resources["TextBrush"],
            FontFamily = (FontFamily)Application.Current.Resources["MainFont"],
        };
        w.SourceInitialized += (_, _) =>
        {
            var h = new WindowInteropHelper(w).Handle;
            int ex = Native.GetWindowLong(h, Native.GWL_EXSTYLE) | Native.WS_EX_TOOLWINDOW;
            if (noActivate) ex |= Native.WS_EX_NOACTIVATE;
            if (clickThrough) ex |= Native.WS_EX_TRANSPARENT;
            Native.SetWindowLong(h, Native.GWL_EXSTYLE, ex);
        };
        return w;
    }

    public static Border Panel(UIElement child, double radius = 18) => new()
    {
        Child = child,
        CornerRadius = new CornerRadius(radius),
        Background = new SolidColorBrush(Color.FromArgb(0xEE, 16, 16, 28)),
        BorderBrush = (Brush)Application.Current.Resources["AccentBrush"],
        BorderThickness = new Thickness(1),
        Padding = new Thickness(18, 12, 18, 12),
        Margin = new Thickness(20),   // room for the glow
        Effect = (System.Windows.Media.Effects.Effect)Application.Current.Resources["Glow"],
    };

    public static TextBlock Icon(string glyph, double size = 22) => new()
    {
        Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center,
        FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
        Foreground = (Brush)Application.Current.Resources["AccentBrush"],
    };
}

/// <summary>On-screen display for hotkey actions: icon + text (+ level bar).</summary>
public static class Osd
{
    static Window? _w;
    static TextBlock? _icon, _text;
    static ProgressBar? _bar;
    static readonly DispatcherTimer Hide = new() { Interval = TimeSpan.FromSeconds(1.6) };

    public static void Show(string glyph, string text, int? level = null)
    {
        if (!Settings.Current.Osd) return;
        if (_w == null) Build();
        _icon!.Text = glyph;
        _text!.Text = text;
        _bar!.Visibility = level is null ? Visibility.Collapsed : Visibility.Visible;
        if (level is int l) _bar.Value = l;

        _w!.BeginAnimation(UIElement.OpacityProperty, null);
        _w.Opacity = 1;
        if (!_w.IsVisible) _w.Show();
        _w.UpdateLayout();
        var wa = SystemParameters.WorkArea;
        _w.Left = wa.Left + (wa.Width - _w.ActualWidth) / 2;
        _w.Top = wa.Bottom - _w.ActualHeight - 60;
        Hide.Stop(); Hide.Start();
    }

    static void Build()
    {
        _w = Floating.Create(clickThrough: true);
        _icon = Floating.Icon("", 24);
        _text = new TextBlock { FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        _bar = new ProgressBar { Width = 160, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Maximum = 100 };
        var sp = new StackPanel { Orientation = Orientation.Horizontal, MinWidth = 180 };
        sp.Children.Add(_icon); sp.Children.Add(_text); sp.Children.Add(_bar);
        _w.Content = Floating.Panel(sp, 22);
        Hide.Tick += (_, _) =>
        {
            Hide.Stop();
            var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(250));
            a.Completed += (_, _) => { if (_w.Opacity == 0) _w.Hide(); };
            _w.BeginAnimation(UIElement.OpacityProperty, a);
        };
    }
}

/// <summary>Neon notification cards, bottom-right, stacked.</summary>
public static class Toast
{
    static readonly List<Window> Open = new();

    public static void Show(string glyph, string title, string text, Action? onClick = null)
    {
        var w = Floating.Create(clickThrough: false);
        var head = new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold };
        var body = new TextBlock { Text = text, FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 280, Foreground = (Brush)Application.Current.Resources["DimBrush"], Margin = new Thickness(0, 2, 0, 0) };
        var col = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
        col.Children.Add(head); col.Children.Add(body);
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        row.Children.Add(Floating.Icon(glyph, 26)); row.Children.Add(col);
        w.Content = Floating.Panel(row, 14);
        w.Cursor = System.Windows.Input.Cursors.Hand;
        w.MouseLeftButtonUp += (_, _) => { onClick?.Invoke(); Close(w); };

        w.Opacity = 0;
        w.Show();
        w.UpdateLayout();
        Open.Add(w);
        Layout();
        w.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
        t.Tick += (_, _) => { t.Stop(); Close(w); };
        t.Start();
    }

    static void Close(Window w)
    {
        if (!Open.Remove(w)) return;
        var a = new DoubleAnimation(0, TimeSpan.FromMilliseconds(200));
        a.Completed += (_, _) => w.Close();
        w.BeginAnimation(UIElement.OpacityProperty, a);
        Layout();
    }

    static void Layout()
    {
        var wa = SystemParameters.WorkArea;
        // Windows carry a 20px transparent margin for the glow, so neighbours overlap by it.
        double y = wa.Bottom + 8;
        for (int i = Open.Count - 1; i >= 0; i--)
        {
            var w = Open[i];
            y -= w.ActualHeight;
            w.Left = wa.Right - w.ActualWidth + 8;
            w.Top = y;
            y += 26;
        }
    }
}
