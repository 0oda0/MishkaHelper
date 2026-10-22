using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MishkaHelper.Core;

/// <summary>Dev aid: "--shots dir" renders every page (and popups) to PNG, then exits.</summary>
public static class Shots
{
    public static async void Run(string dir, MainWindow main)
    {
        Directory.CreateDirectory(dir);
        await Task.Delay(1500);
        foreach (var p in new[] { "Home", "Controls", "System", "Devices", "Nitro", "Boost", "Security", "Tools", "Profiles", "Hotkeys", "Widgets", "Settings" })
        {
            main.Navigate(p);
            await Task.Delay(p is "Devices" or "Security" or "System" ? 6000 : 2500);
            Save(main, Path.Combine(dir, p + ".png"));
        }
        QuickPanel.Toggle();
        await Task.Delay(1500);
        foreach (Window w in Application.Current.Windows)
            if (w is QuickPanel) { Save(w, Path.Combine(dir, "QuickPanel.png")); w.Close(); }

        RadialMenu.Toggle();
        await Task.Delay(800);
        foreach (Window w in Application.Current.Windows)
            if (w.Content is System.Windows.Controls.Canvas) Save(w, Path.Combine(dir, "Radial.png"));
        RadialMenu.Toggle();

        var kinds = Widgets.Names.Keys.ToArray();
        var was = kinds.Where(Widgets.IsOpen).ToList();
        foreach (var k in kinds) Widgets.Set(k, true);
        await Task.Delay(5000);
        int i = 0;
        foreach (Window w in Application.Current.Windows)
            if (w.Content is System.Windows.Controls.Border && w != main) Save(w, Path.Combine(dir, $"Widget{i++:00}.png"));
        CommandPalette.Toggle();
        await Task.Delay(600);
        foreach (Window w in Application.Current.Windows)
            if (w.IsVisible && w.Content is System.Windows.Controls.Border { Child: System.Windows.Controls.StackPanel { Children.Count: 2 } } && w != main)
                Save(w, Path.Combine(dir, "Palette.png"));
        CommandPalette.Toggle();
        foreach (var k in kinds.Except(was)) Widgets.Set(k, false);

        Osd.Show("", "Громкость", 55);
        await Task.Delay(300);
        foreach (Window w in Application.Current.Windows)
            if (w.IsVisible && w.Content is System.Windows.Controls.Border b && b.Child is System.Windows.Controls.StackPanel { Orientation: System.Windows.Controls.Orientation.Horizontal } && w != main)
                Save(w, Path.Combine(dir, "Osd.png"));
        App.Current.Quit();
    }

    static void Save(Window w, string file)
    {
        var el = (FrameworkElement)w.Content;
        var dpi = VisualTreeHelper.GetDpi(w);
        var bmp = new RenderTargetBitmap((int)(el.ActualWidth * dpi.DpiScaleX), (int)(el.ActualHeight * dpi.DpiScaleY), dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var bg = new DrawingVisual();
        using (var dc = bg.RenderOpen()) dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(10, 10, 18)), null, new Rect(0, 0, el.ActualWidth, el.ActualHeight));
        bmp.Render(bg);
        bmp.Render(el);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(file);
        enc.Save(fs);
    }
}
