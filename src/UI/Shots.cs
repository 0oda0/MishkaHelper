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
        // Dev copies aren't elevated, so the LAN remote can't bind; localhost is enough to show its QR on the Tools page.
        if (!Remote.Running) try { Remote.StartLocal(); } catch { }
        await Task.Delay(1500);
        foreach (var p in new[] { "Jarvis", "Scenarios", "Home", "Controls", "System", "Devices", "Phone", "Nitro", "Boost", "Security", "Tools", "Profiles", "Hotkeys", "Radial", "Widgets", "Settings" })
        {
            if (p is "Jarvis" or "Scenarios" && !Edition.HasJarvis) continue;
            main.Navigate(p);
            await Task.Delay(p is "Devices" or "Security" or "System" ? 6000 : 2500);
            Save(main, Path.Combine(dir, p + ".png"));
        }
#if !LITE
        JarvisChat.Open();
        foreach (var q in new[] { "привет", "что ты умеешь", "сколько времени" })
            await Jarvis.Handle(q);
        await Task.Delay(800);
        foreach (Window w in Application.Current.Windows)
            if (w.Title == "Джарвис") { Save(w, Path.Combine(dir, "JarvisChat.png")); w.Close(); }
#endif

        QuickPanel.Toggle();
        await Task.Delay(1500);
        foreach (Window w in Application.Current.Windows)
            if (w is QuickPanel) { Save(w, Path.Combine(dir, "QuickPanel.png")); w.Close(); }

        RadialMenu.Toggle();
        await Task.Delay(800);
        foreach (Window w in Application.Current.Windows)
            if (w.Content is System.Windows.Controls.Canvas) Save(w, Path.Combine(dir, "Wheel.png"));
        RadialMenu.Toggle();

        // A custom wheel: built-ins + any palette actions, then the user's own wheel comes back.
        var keep = Settings.Current.Radial.ToList();
        Settings.Current.Radial = new()
        {
            new() { Key = "music" }, new() { Key = "profiles" }, new() { Key = "tools" },
            new() { Key = "act:page:Security", Label = "Защита", Icon = "" },
            new() { Key = "act:widget:media", Label = "Виджет музыки", Icon = "" },
        };
        RadialMenu.Toggle();
        await Task.Delay(800);
        foreach (Window w in Application.Current.Windows)
            if (w.Content is System.Windows.Controls.Canvas) Save(w, Path.Combine(dir, "RadialCustom.png"));
        RadialMenu.Toggle();
        Settings.Current.Radial = keep;
        await Task.Delay(200);

        foreach (var (sub, name) in new[] { ("Программы", "RadialPrograms"), ("Виджеты", "RadialWidgets"), ("Джарвис", "RadialJarvis") }.Where(x => Edition.HasJarvis || x.Item1 != "Джарвис"))
        {
            RadialMenu.OpenAt(sub);
            await Task.Delay(800);
            foreach (Window w in Application.Current.Windows)
                if (w.Content is System.Windows.Controls.Canvas) Save(w, Path.Combine(dir, name + ".png"));
            RadialMenu.Toggle();
            await Task.Delay(200);
        }

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

        // Column layout check: open a few, hide the second, log where everything ended up.
        foreach (var k in kinds) Widgets.Set(k, false);
        var few = new[] { "system", "clock", "battery", "audio", "ping" };
        foreach (var k in few) { Settings.Current.Widgets.First(c => c.Kind == k).Manual = false; Widgets.Set(k, true); }
        await Task.Delay(1500);
        var log = new System.Text.StringBuilder($"work area {SystemParameters.WorkArea}\n");
        void Dump(string title)
        {
            log.AppendLine(title);
            foreach (Window w in Application.Current.Windows)
                if (w.Content is System.Windows.Controls.Border && w != main && w.IsVisible)
                    log.AppendLine($"  {w.Left:0},{w.Top:0}  {w.ActualWidth:0}x{w.ActualHeight:0}");
        }
        Dump("five open:");
        Widgets.Set("clock", false);
        await Task.Delay(800);
        Dump("clock hidden:");
        File.WriteAllText(Path.Combine(dir, "widget-layout.txt"), log.ToString());
        foreach (var k in few) Widgets.Set(k, false);
        foreach (var k in was) Widgets.Set(k, true);

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
