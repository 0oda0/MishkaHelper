using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MishkaHelper.Core;

public static class ThemeManager
{
    public static readonly Dictionary<string, (string Name, Color Main, Color Second)> Accents = new()
    {
        ["Pink"] = ("Розовый", C("#FF2E97"), C("#A259FF")),
        ["Blue"] = ("Синий", C("#00C8FF"), C("#4D6BFF")),
        ["Purple"] = ("Фиолетовый", C("#A259FF"), C("#FF2E97")),
        ["Red"] = ("Красный", C("#FF3355"), C("#FF8A3D")),
        ["Green"] = ("Кислотный", C("#39FF9C"), C("#00C8FF")),
    };

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static Color Accent => (Color)Application.Current.Resources["AccentColor"];

    public static void Apply()
    {
        var s = Settings.Current;
        var (_, main, second) = Accents.TryGetValue(s.Accent, out var a) ? a : Accents["Purple"];
        var r = Application.Current.Resources;
        r["AccentColor"] = main;
        r["AccentBrush"] = Frozen(new SolidColorBrush(main));
        r["AccentSoftBrush"] = Frozen(new SolidColorBrush(Color.FromArgb(0x33, main.R, main.G, main.B)));
        r["Accent2Brush"] = Frozen(new SolidColorBrush(second));
        r["Glow"] = Frozen(new DropShadowEffect { Color = main, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.85 });
        r["SoftGlow"] = Frozen(new DropShadowEffect { Color = main, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.5 });

        // Glass: translucent surfaces over a DWM acrylic backdrop.
        r["BgBrush"] = Frozen(new SolidColorBrush(s.Glass ? Color.FromArgb(0x60, 8, 8, 16) : C("#0A0A12")));
        r["SidebarBrush"] = Frozen(new SolidColorBrush(s.Glass ? Color.FromArgb(0x50, 14, 14, 25) : C("#0E0E19")));
        r["CardBrush"] = Frozen(new SolidColorBrush(s.Glass ? Color.FromArgb(0x48, 40, 40, 70) : C("#141422")));
        r["CardBorderBrush"] = Frozen(new SolidColorBrush(s.Glass ? Color.FromArgb(0x40, 255, 255, 255) : C("#24243A")));
        r["ControlBrush"] = Frozen(new SolidColorBrush(s.Glass ? Color.FromArgb(0x38, 255, 255, 255) : C("#1E1E31")));

        foreach (Window w in Application.Current.Windows) ApplyBackdrop(w);
    }

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>Dark title, rounded corners, and acrylic when glass is on (Windows 11).</summary>
    public static void ApplyBackdrop(Window w, bool? forceGlass = null)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        bool glass = forceGlass ?? Settings.Current.Glass;
        int on = 1, round = 2, backdrop = glass ? 3 : 1;
        Native.DwmSetWindowAttribute(hwnd, 20, ref on, 4);        // immersive dark mode
        Native.DwmSetWindowAttribute(hwnd, 33, ref round, 4);     // rounded corners
        Native.DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4);  // system backdrop
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: not null } src)
            src.CompositionTarget.BackgroundColor = glass ? Colors.Transparent : C("#0A0A12");
        var m = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Native.DwmExtendFrameIntoClientArea(hwnd, ref m);
    }
}
