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
        BuildLook();
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

        About.Text = $"{Edition.Display} {Edition.Version} · {(Updater.IsInstalled ? "установлена" : "портативная")}\n" +
                     $"\nДанные: {Settings.Dir}";
        if (Updater.Available is { } u) ShowUpdate(u);
        BuildEditions();
        Loaded += async (_, _) => { Autostart.IsChecked = await Task.Run(() => Core.Autostart.Enabled); _loading = false; };
    }

    void Setup_Click(object s, RoutedEventArgs e) => SetupWizard.Open();

    static string? PickColor(string current)
    {
        using var d = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
        if (ThemeManager.Parse(current) is { } c) d.Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B);
        return d.ShowDialog() == System.Windows.Forms.DialogResult.OK ? $"#{d.Color.R:X2}{d.Color.G:X2}{d.Color.B:X2}" : null;
    }

    /// <summary>Own accent, background (colour or picture), cards, interface size. Every change shows at once.</summary>
    void BuildLook()
    {
        var s = Settings.Current;
        void Redo() { Settings.Save(); ThemeManager.Apply(); App.Current.Navigate("Settings"); }
        Button Btn(Panel p, string text, Action click)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 6) };
            b.Click += (_, _) => click();
            p.Children.Add(b);
            return b;
        }
        Border Swatch(Color c, bool on, string tip, Action click)
        {
            var sw = new Border
            {
                Width = 34, Height = 34, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 8, 6), Cursor = Cursors.Hand, ToolTip = tip,
                Background = new SolidColorBrush(c), BorderThickness = new Thickness(on ? 3 : 1), BorderBrush = on ? (Brush)FindResource("AccentBrush") : Brushes.Gray,
            };
            sw.MouseLeftButtonUp += (_, _) => click();
            return sw;
        }

        var acc = new WrapPanel();
        Btn(acc, "Свой цвет неона…", () => { if (PickColor(s.CustomAccent) is { } h) { s.CustomAccent = h; Redo(); } });
        if (s.CustomAccent.Length > 0) Btn(acc, "Вернуть цвет из набора", () => { s.CustomAccent = ""; Redo(); });
        Look.Children.Add(acc);

        Look.Children.Add(new TextBlock { Text = "Фон", Style = (Style)FindResource("Dim"), Margin = new Thickness(0, 8, 0, 6) });
        var bgs = new WrapPanel();
        var cur = s.BgColor.Length > 0 ? s.BgColor : ThemeManager.DefaultBg;
        foreach (var (name, hex) in ThemeManager.Backgrounds)
            bgs.Children.Add(Swatch(ThemeManager.Parse(hex)!.Value, cur.Equals(hex, StringComparison.OrdinalIgnoreCase), name, () => { s.BgColor = hex == ThemeManager.DefaultBg ? "" : hex; s.CardColor = ""; Redo(); }));
        Btn(bgs, "Свой…", () => { if (PickColor(cur) is { } h) { s.BgColor = h; s.CardColor = ""; Redo(); } });
        Look.Children.Add(bgs);

        var cards = new WrapPanel();
        Btn(cards, s.CardColor.Length > 0 ? "Цвет карточек: свой…" : "Цвет карточек…", () => { if (PickColor(s.CardColor) is { } h) { s.CardColor = h; Redo(); } });
        if (s.CardColor.Length > 0) Btn(cards, "Карточки по фону", () => { s.CardColor = ""; Redo(); });
        Btn(cards, s.BgImage.Length > 0 ? "Другая картинка…" : "Картинка на фон…", () =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Картинки|*.jpg;*.jpeg;*.png;*.bmp;*.webp" };
            if (d.ShowDialog() != true) return;
            try { s.BgImage = ThemeManager.KeepImage(d.FileName); Redo(); } catch (Exception ex) { Osd.Show("\uE7BA", ex.Message); }
        });
        if (s.BgImage.Length > 0) Btn(cards, "Убрать картинку", () => { s.BgImage = ""; Redo(); });
        Look.Children.Add(cards);

        if (s.BgImage.Length > 0) Look.Children.Add(SliderRow("Затемнение картинки", 0, 95, s.BgImageDim, "%", v => { s.BgImageDim = (int)v; Settings.Save(); ThemeManager.Apply(); }));
        Look.Children.Add(SliderRow("Размер интерфейса", 80, 150, Math.Round(s.UiScale * 100), "%", v => { s.UiScale = Math.Round(v) / 100; Settings.Save(); ThemeManager.ApplyScale(Application.Current.MainWindow); }));

        var reset = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        Btn(reset, "Сбросить оформление", () =>
        {
            s.CustomAccent = s.BgColor = s.CardColor = s.BgImage = ""; s.BgImageDim = 70; s.UiScale = 1; s.Glass = false; s.Accent = "Purple";
            Redo();
        });
        Look.Children.Add(reset);
    }

    UIElement SliderRow(string label, double min, double max, double value, string unit, Action<double> set)
    {
        var g = new Grid { Margin = new Thickness(0, 6, 0, 4) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(56) });
        var t = UiKit.Text(label, 13); t.VerticalAlignment = VerticalAlignment.Center;
        var sl = new Slider { Minimum = min, Maximum = max, Value = value, IsSnapToTickEnabled = true, TickFrequency = 5, VerticalAlignment = VerticalAlignment.Center };
        var v = UiKit.Text($"{value:0} {unit}", 12, true); v.VerticalAlignment = VerticalAlignment.Center; v.HorizontalAlignment = HorizontalAlignment.Right;
        // Applied when the slider is let go: rescaling the window under a dragging mouse makes the slider jump.
        sl.ValueChanged += (_, _) => v.Text = $"{sl.Value:0} {unit}";
        sl.PreviewMouseLeftButtonUp += (_, _) => set(sl.Value);
        sl.KeyUp += (_, _) => set(sl.Value);
        Grid.SetColumn(sl, 1); Grid.SetColumn(v, 2);
        g.Children.Add(t); g.Children.Add(sl); g.Children.Add(v);
        return g;
    }

    /// <summary>The parts of the program: switched on and off in place (modules.ini), a restart applies them. Data of a switched-off part stays.</summary>
    void BuildEditions()
    {
        EditionBox.Children.Add(new TextBlock { Text = "Состав программы", Style = (Style)FindResource("Dim"), Margin = new Thickness(0, 0, 0, 4) });
        CheckBox Part(string text, bool on) { var c = new CheckBox { Style = (Style)FindResource("Switch"), Content = text, IsChecked = on }; EditionBox.Children.Add(c); return c; }
        var j = Part("Джарвис — голосовой помощник", Modules.Jarvis);
        var s = Part("Секретарша — заметки, дела, сводки, почта", Modules.Secretary);
        var g = Part("Игровой помощник — база знаний по играм", Modules.Games);
        var note = UiKit.Text("", 12, true); note.TextWrapping = TextWrapping.Wrap;
        var restart = new Button { Content = "Перезапустить сейчас", Style = (Style)FindResource("AccentButton"), HorizontalAlignment = HorizontalAlignment.Left, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
        restart.Click += (_, _) => App.Current.Restart();
        void Changed()
        {
            try { Modules.Save(j.IsChecked == true, s.IsChecked == true, g.IsChecked == true); }
            catch (Exception ex) { note.Text = "Не получилось сохранить: " + ex.Message; return; }
            note.Text = "Применится после перезапуска. Настройки и данные выключенных частей остаются на диске.";
            restart.Visibility = Visibility.Visible;
        }
        foreach (var c in new[] { j, s, g }) c.Click += (_, _) => Changed();
        EditionBox.Children.Add(note); EditionBox.Children.Add(restart);
    }

    void ShowUpdate(UpdateInfo u)
    {
        UpdateStatus.Text = $"Доступна версия {u.Version}." + (u.Notes.Length > 0 ? "\n" + Updater.PlainNotes(u.Notes) : "");
        UpdateBtn.Content = $"Обновить до {u.Version}";
        UpdateBtn.Visibility = Visibility.Visible;
    }

    async void Check_Click(object s, RoutedEventArgs e)
    {
        CheckBtn.IsEnabled = false;
        UpdateStatus.Text = "Проверяю…";
        try
        {
            var u = await Updater.Check();
            if (u == null) UpdateStatus.Text = $"У тебя последняя версия ({Edition.Version}).";
            else ShowUpdate(u);
        }
        catch (Exception ex) { UpdateStatus.Text = "Не удалось проверить: " + ex.Message; }
        CheckBtn.IsEnabled = true;
    }

    async void Update_Click(object s, RoutedEventArgs e)
    {
        if (Updater.Available is not { } u) return;
        UpdateBtn.IsEnabled = CheckBtn.IsEnabled = false;
        UpdateBar.Visibility = Visibility.Visible;
        UpdateStatus.Text = $"Скачиваю {u.Version}… Приложение перезапустится само.";
        try { await Updater.Install(u, f => Dispatcher.BeginInvoke(() => UpdateBar.Value = f)); }
        catch (Exception ex) { UpdateStatus.Text = "Ошибка: " + ex.Message; UpdateBtn.IsEnabled = CheckBtn.IsEnabled = true; }
    }

    void Features_Click(object s, RoutedEventArgs e) => Features.Open();

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
        var d = new Microsoft.Win32.SaveFileDialog { FileName = $"mishka-backup-{DateTime.Now:yyyy-MM-dd}.zip", Filter = "Резервная копия|*.zip" };
        if (d.ShowDialog() != true) return;
        try
        {
            Settings.Save();
#if SECRETARY
            SecStore.Save();
#endif
            Backup.Make(Settings.Dir, d.FileName);
            Osd.Show("", "Копия сохранена: настройки, заметки, дела");
        }
        catch (Exception ex) { Osd.Show("", "Не получилось: " + ex.Message); }
    }

    void Import_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = "Резервная копия|*.zip;*.json" };
        if (d.ShowDialog() != true) return;
        if (!UiKit.Confirm("Заменить текущие настройки и заметки данными из копии?")) return;
        try
        {
            Backup.Restore(d.FileName, Settings.Dir);
            Settings.Load();
#if SECRETARY
            SecStore.Reload();
#endif
#if JARVIS || SECRETARY
            Notes.Reload();
#endif
            ThemeManager.Apply();
            Hotkeys.Reload();
            Osd.Show("", "Копия загружена");
            App.Current.Navigate("Settings");
        }
        catch (Exception ex) { Osd.Show("", "Файл не подошёл: " + ex.Message); }
    }
}
