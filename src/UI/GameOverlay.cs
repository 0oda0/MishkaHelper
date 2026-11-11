using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace MishkaHelper.Core;

/// <summary>
/// A small line in the corner over a game: FPS, CPU and GPU load and temperature. Click-through, never takes focus.
/// Shows itself with game mode (if allowed in settings) or by its action; not over exclusive full-screen, which nothing can draw over.
/// </summary>
public static class GameOverlay
{
    static Window? _w;
    static TextBlock? _text;
    static DispatcherTimer? _timer;
    static IDisposable? _live;
    static bool _fps;

    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);

    public static bool Shown => _w != null;
    public static void Toggle() { if (Shown) Hide(); else Show(); }

    /// <summary>Follows game mode: on when a game starts, off when it ends.</summary>
    public static void Follow()
    {
        if (Focus.On && Settings.Current.GameOverlay) Show();
        else if (!Focus.On && Shown) Hide();
    }

    public static void Show()
    {
        if (Shown) return;
        _fps = Fps.Start();
        _live = SysMon.Live();
        _text = new TextBlock { FontFamily = new FontFamily("Consolas"), FontSize = 13, Foreground = Brushes.White, Text = "…" };
        _w = Floating.Create(clickThrough: true);
        _w.Content = new Border { Child = _text, Background = new SolidColorBrush(Color.FromArgb(0x99, 0, 0, 0)), CornerRadius = new CornerRadius(6), Padding = new Thickness(8, 3, 8, 3) };
        _w.Left = SystemParameters.WorkArea.Left + 8; _w.Top = SystemParameters.WorkArea.Top + 8;
        _w.Show();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    public static void Hide()
    {
        _timer?.Stop(); _timer = null;
        _w?.Close(); _w = null;
        _live?.Dispose(); _live = null;
        if (_fps) { Fps.Stop(); _fps = false; }
    }

    static void Tick()
    {
        GetWindowThreadProcessId(GetForegroundWindow(), out var pid);
        bool same = Fps.Pid == (int)pid;
        Fps.Pid = (int)pid;
        int frames = Fps.Take();
        var s = SysMon.Last;
        var fps = !_fps ? "" : same && frames > 0 ? $"{frames,3} FPS  " : "  — FPS  ";
        _text!.Text = $"{fps}CPU {s.Cpu,3:0}% {s.CpuTemp}°  GPU {s.Gpu,3:0}% {s.GpuTemp}°";
        _w!.Topmost = false; _w.Topmost = true;   // a game that went full-screen after us would cover it
    }
}
