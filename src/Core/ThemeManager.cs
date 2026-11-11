using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

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

    /// <summary>Background presets: name → colour. The last one is light: text turns dark by itself.</summary>
    public static readonly (string Name, string Hex)[] Backgrounds =
    {
        ("Тёмный", "#0A0A12"), ("Графит", "#18191D"), ("Ночной синий", "#0A1324"), ("Чёрный", "#000000"), ("Тёмно-зелёный", "#0B1712"), ("Светлый", "#EEF0F6"),
    };
    public const string DefaultBg = "#0A0A12";

    static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    public static Color? Parse(string hex) { try { return hex.Length > 0 ? C(hex) : null; } catch { return null; } }
    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static Color Accent => (Color)Application.Current.Resources["AccentColor"];

    /// <summary>Relative luminance 0..1: decides whether the text on it is light or dark.</summary>
    internal static double Luma(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255;
    internal static bool IsLight(Color c) => Luma(c) > 0.55;

    /// <summary>A step away from the background: lighter on dark, darker on light. Cards and borders are built from it.</summary>
    internal static Color Step(Color c, int amount)
    {
        int d = IsLight(c) ? -amount : amount;
        byte F(byte v) => (byte)Math.Clamp(v + d, 0, 255);
        return Color.FromRgb(F(c.R), F(c.G), F(c.B));
    }

    static Color A(byte a, Color c) => Color.FromArgb(a, c.R, c.G, c.B);

    public static void Apply()
    {
        var s = Settings.Current;
        var (_, main, second) = Accents.TryGetValue(s.Accent, out var a) ? a : Accents["Purple"];
        if (Parse(s.CustomAccent) is { } custom) main = custom;
        var r = Application.Current.Resources;
        r["AccentColor"] = main;
        r["AccentBrush"] = Frozen(new SolidColorBrush(main));
        r["AccentSoftBrush"] = Frozen(new SolidColorBrush(Color.FromArgb(0x33, main.R, main.G, main.B)));
        r["Accent2Brush"] = Frozen(new SolidColorBrush(second));
        r["Glow"] = Frozen(new DropShadowEffect { Color = main, BlurRadius = 18, ShadowDepth = 0, Opacity = 0.85 });
        r["SoftGlow"] = Frozen(new DropShadowEffect { Color = main, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.5 });

        var bg = Parse(s.BgColor) ?? C(DefaultBg);
        bool light = IsLight(bg);
        var card = Parse(s.CardColor) ?? Step(bg, 10);
        bool image = s.BgImage.Length > 0 && File.Exists(s.BgImage);
        // Glass and a picture both need see-through surfaces; otherwise everything is solid.
        byte Alpha(byte glass, byte pic, byte solid = 0xFF) => s.Glass ? glass : image ? pic : solid;
        r["BgBrush"] = image ? Picture(s.BgImage, A(s.Glass ? (byte)0x60 : (byte)(s.BgImageDim * 255 / 100), bg)) : Frozen(new SolidColorBrush(A(Alpha(0x60, 0xFF), bg)));
        r["SidebarBrush"] = Frozen(new SolidColorBrush(A(Alpha(0x50, 0xB0), Step(bg, 4))));
        r["CardBrush"] = Frozen(new SolidColorBrush(A(Alpha(0x48, 0xC8), card)));
        r["CardHoverBrush"] = Frozen(new SolidColorBrush(A(Alpha(0x60, 0xD8), Step(card, 7))));
        r["CardBorderBrush"] = Frozen(new SolidColorBrush(s.Glass ? Color.FromArgb(0x40, 255, 255, 255) : Step(card, 16)));
        r["ControlBrush"] = Frozen(new SolidColorBrush(s.Glass ? (light ? Color.FromArgb(0x30, 0, 0, 0) : Color.FromArgb(0x38, 255, 255, 255)) : Step(card, 10)));
        r["TextBrush"] = Frozen(new SolidColorBrush(light ? C("#16161E") : C("#EEEEF6")));
        r["DimBrush"] = Frozen(new SolidColorBrush(light ? C("#5C5C72") : C("#8C8CA6")));

        foreach (Window w in Application.Current.Windows) ApplyBackdrop(w);
        ApplyScale(Application.Current.MainWindow);
    }

    /// <summary>The picture filling the window, with the background colour over it so text stays readable.</summary>
    static Brush Picture(string file, Color veil)
    {
        try
        {
            var img = new BitmapImage();
            img.BeginInit(); img.UriSource = new Uri(file); img.CacheOption = BitmapCacheOption.OnLoad; img.DecodePixelWidth = 2560; img.EndInit(); img.Freeze();
            var rect = new Rect(0, 0, img.PixelWidth, img.PixelHeight);
            var g = new DrawingGroup();
            g.Children.Add(new ImageDrawing(img, rect));
            g.Children.Add(new GeometryDrawing(new SolidColorBrush(veil), null, new RectangleGeometry(rect)));
            return Frozen(new DrawingBrush(g) { Stretch = Stretch.UniformToFill });
        }
        catch (Exception e) { Log.Error(e); return Frozen(new SolidColorBrush(veil)); }
    }

    /// <summary>Interface size: the whole window content is scaled, so every page grows the same way.</summary>
    public static void ApplyScale(Window? w)
    {
        if (w?.FindName("Root") is FrameworkElement root)
        {
            var k = Math.Clamp(Settings.Current.UiScale, 0.8, 1.6);
            root.LayoutTransform = Math.Abs(k - 1) < 0.01 ? Transform.Identity : new ScaleTransform(k, k);
        }
    }

    /// <summary>Puts a picture into the protected data folder: the setting never points into the user's own folders.</summary>
    public static string KeepImage(string source)
    {
        var dest = Path.Combine(AppPaths.Data, "background" + Path.GetExtension(source).ToLowerInvariant());
        foreach (var old in Directory.GetFiles(AppPaths.Data, "background.*")) try { File.Delete(old); } catch { }
        File.Copy(source, dest, true);
        return dest;
    }

    static T Frozen<T>(T f) where T : Freezable { f.Freeze(); return f; }

    /// <summary>Dark (or light) title, rounded corners, and acrylic when glass is on (Windows 11).</summary>
    public static void ApplyBackdrop(Window w, bool? forceGlass = null)
    {
        var hwnd = new WindowInteropHelper(w).Handle;
        if (hwnd == IntPtr.Zero) return;
        bool glass = forceGlass ?? Settings.Current.Glass;
        var bg = Parse(Settings.Current.BgColor) ?? C(DefaultBg);
        int dark = IsLight(bg) ? 0 : 1, round = 2, backdrop = glass ? 3 : 1;
        Native.DwmSetWindowAttribute(hwnd, 20, ref dark, 4);      // immersive dark mode
        Native.DwmSetWindowAttribute(hwnd, 33, ref round, 4);     // rounded corners
        Native.DwmSetWindowAttribute(hwnd, 38, ref backdrop, 4);  // system backdrop
        if (HwndSource.FromHwnd(hwnd) is { CompositionTarget: not null } src)
            src.CompositionTarget.BackgroundColor = glass ? Colors.Transparent : bg;
        var m = new Native.MARGINS { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        Native.DwmExtendFrameIntoClientArea(hwnd, ref m);
    }
}
