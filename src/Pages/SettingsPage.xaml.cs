using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

public partial class SettingsPage : UserControl
{
    static readonly (int Days, string Name)[] HistoryOpts = { (1, "1 день"), (3, "3 дня"), (7, "Неделя"), (14, "2 недели"), (30, "Месяц") };
    bool _loading = true;

    public SettingsPage()
    {
        InitializeComponent();
        var s = Settings.Current;
        foreach (var (key, (name, main, second)) in ThemeManager.Accents)
        {
            var sw = new Border
            {
                Width = 46, Height = 46, CornerRadius = new CornerRadius(23), Margin = new Thickness(0, 0, 10, 8), Cursor = Cursors.Hand, ToolTip = name,
                Background = new LinearGradientBrush(main, second, 45), BorderThickness = new Thickness(key == s.Accent ? 3 : 0), BorderBrush = Brushes.White,
                Effect = key == s.Accent ? new System.Windows.Media.Effects.DropShadowEffect { Color = main, BlurRadius = 18, ShadowDepth = 0 } : null,
            };
            sw.MouseLeftButtonUp += (_, _) =>
            {
                s.Accent = key; Settings.Save(); ThemeManager.Apply();
                App.Current.Navigate("Settings");   // rebuild swatches + glow
            };
            Accents.Children.Add(sw);
        }
        Glass.IsChecked = s.Glass;
        OsdSwitch.IsChecked = s.Osd;
        Minimized.IsChecked = s.StartMinimized;
        CloseTray.IsChecked = s.CloseToTray;
        Alerts.IsChecked = s.Alerts.Enabled;
        VtKey.Password = s.VirusTotalKey;
        foreach (var (_, n) in HistoryOpts) History.Items.Add(n);
        History.SelectedIndex = Math.Max(0, Array.FindIndex(HistoryOpts, o => o.Days == s.HistoryDays));

        var a = s.Alerts;
        Thresholds.Children.Add(Threshold("CPU горячее", a.CpuTemp, 70, 100, "°C", v => a.CpuTemp = v));
        Thresholds.Children.Add(Threshold("GPU горячее", a.GpuTemp, 60, 95, "°C", v => a.GpuTemp = v));
        Thresholds.Children.Add(Threshold("RAM занято больше", a.RamPercent, 70, 99, "%", v => a.RamPercent = v));
        Thresholds.Children.Add(Threshold("На диске меньше", a.DiskFreeGb, 2, 100, "ГБ", v => a.DiskFreeGb = v));
        Thresholds.Children.Add(Threshold("Наушники/мышь ниже", a.HeadsetBattery, 5, 50, "%", v => a.HeadsetBattery = v));

        var ver = typeof(App).Assembly.GetName().Version;
        About.Text = $"Mishka Helper {ver?.ToString(3)}\nСистемный помощник: регуляторы, мониторинг, устройства, очистка, защита.\nДанные: {Settings.Dir}";
        Loaded += async (_, _) => { Autostart.IsChecked = await Task.Run(() => Core.Autostart.Enabled); _loading = false; };
    }

    UIElement Threshold(string label, int value, int min, int max, string unit, Action<int> set)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var sl = new Slider { Minimum = min, Maximum = max, Value = value, IsSnapToTickEnabled = true, TickFrequency = 1, VerticalAlignment = VerticalAlignment.Center };
        var t = UiKit.Text($"{value} {unit}", 12); t.HorizontalAlignment = HorizontalAlignment.Right; t.VerticalAlignment = VerticalAlignment.Center;
        sl.ValueChanged += (_, e) => { set((int)e.NewValue); t.Text = $"{e.NewValue:0} {unit}"; };
        sl.LostMouseCapture += (_, _) => Settings.Save();
        Grid.SetColumn(sl, 1); Grid.SetColumn(t, 2);
        g.Children.Add(sl); g.Children.Add(t);
        return g;
    }

    void Glass_Click(object s, RoutedEventArgs e) { Settings.Current.Glass = Glass.IsChecked == true; Settings.Save(); ThemeManager.Apply(); }
    void Osd_Click(object s, RoutedEventArgs e) { Settings.Current.Osd = OsdSwitch.IsChecked == true; Settings.Save(); }

    async void Autostart_Click(object s, RoutedEventArgs e)
    {
        if (_loading) return;
        Settings.Current.StartWithWindows = Autostart.IsChecked == true;
        Settings.Save();
        var on = Settings.Current.StartWithWindows;
        await Task.Run(() => Core.Autostart.Set(on));
        Autostart.IsChecked = await Task.Run(() => Core.Autostart.Enabled);
    }

    void Minimized_Click(object s, RoutedEventArgs e) { Settings.Current.StartMinimized = Minimized.IsChecked == true; Settings.Save(); }
    void CloseTray_Click(object s, RoutedEventArgs e) { Settings.Current.CloseToTray = CloseTray.IsChecked == true; Settings.Save(); }
    void Alerts_Click(object s, RoutedEventArgs e) { Settings.Current.Alerts.Enabled = Alerts.IsChecked == true; Settings.Save(); }
    void VtKey_Changed(object s, RoutedEventArgs e) { if (_loading) return; Settings.Current.VirusTotalKey = VtKey.Password.Trim(); Settings.Save(); }
    void Vt_Click(object s, RoutedEventArgs e) => Shell.Open("https://www.virustotal.com/gui/my-apikey");

    void History_Changed(object s, SelectionChangedEventArgs e)
    {
        Settings.Current.HistoryDays = HistoryOpts[History.SelectedIndex].Days;
        Settings.Save();
    }

    void Folder_Click(object s, RoutedEventArgs e) => Shell.Open(Settings.Dir);
    void Log_Click(object s, RoutedEventArgs e) { var p = Path.Combine(Settings.Dir, "log.txt"); if (File.Exists(p)) Shell.Open(p); else Osd.Show("", "Ошибок не было"); }
    void Github_Click(object s, RoutedEventArgs e) => Shell.Open("https://github.com/0oda0/MishkaHelper");

    void Export_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.SaveFileDialog { FileName = "mishka-settings.json", Filter = "JSON|*.json" };
        if (d.ShowDialog() != true) return;
        Settings.Save();
        File.Copy(Path.Combine(Settings.Dir, "settings.json"), d.FileName, true);
    }

    void Import_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = "JSON|*.json" };
        if (d.ShowDialog() != true) return;
        try
        {
            System.Text.Json.JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(d.FileName));   // validate before replacing
            File.Copy(d.FileName, Path.Combine(Settings.Dir, "settings.json"), true);
            Settings.Load();
            ThemeManager.Apply();
            Hotkeys.Reload();
            Osd.Show("", "Настройки загружены");
            App.Current.Navigate("Settings");
        }
        catch (Exception ex) { Osd.Show("", "Файл не подошёл: " + ex.Message); }
    }
}
