using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MishkaHelper.Core;
using NAudio.CoreAudioApi;

namespace MishkaHelper.Pages;

public partial class ProfilesPage : UserControl
{
    static readonly string[] Icons = { "", "", "", "", "", "", "", "", "", "" };
    bool _loading = true;

    public ProfilesPage()
    {
        InitializeComponent();
        Render();
        var s = Settings.Current;
        Auto.IsChecked = s.AutoProfiles;
        AutoPanel.IsEnabled = s.AutoProfiles;
        FillProfileCombo(OnBattery, s.OnBatteryProfile);
        FillProfileCombo(OnAc, s.OnAcProfile);
        FillProfileCombo(RuleProfile, null, false);
        RenderRules();
        _loading = false;
    }

    static void FillProfileCombo(ComboBox cb, string? selected, bool allowNone = true)
    {
        cb.Items.Clear();
        if (allowNone) cb.Items.Add("— ничего —");
        foreach (var p in Settings.Current.Profiles) cb.Items.Add(p.Name);
        cb.SelectedItem = selected ?? (allowNone ? "— ничего —" : Settings.Current.Profiles.FirstOrDefault()?.Name);
    }

    void Render()
    {
        List.Children.Clear();
        foreach (var p in Settings.Current.Profiles) List.Children.Add(Editor(p));
    }

    UIElement Editor(Profile p)
    {
        var sp = new StackPanel { Width = 330 };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var icon = new Button { Content = p.Icon, FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 20, Style = (Style)FindResource("GhostButton"), ToolTip = "Сменить значок", Foreground = (Brush)FindResource("AccentBrush") };
        icon.Click += (_, _) => { p.Icon = Icons[(Array.IndexOf(Icons, p.Icon) + 1) % Icons.Length]; icon.Content = p.Icon; Save(); };
        head.Children.Add(icon);
        var name = new TextBox { Text = p.Name, FontSize = 15, FontWeight = FontWeights.SemiBold, Margin = new Thickness(8, 0, 0, 0) };
        name.LostFocus += (_, _) =>
        {
            var old = p.Name;
            p.Name = name.Text.Trim() is { Length: > 0 } n ? n : old;
            foreach (var r in Settings.Current.AppRules.Where(r => r.Profile == old)) r.Profile = p.Name;
            Save();
        };
        head.Children.Add(name);
        sp.Children.Add(head);

        sp.Children.Add(SliderRow("Громкость", p.Volume, v => p.Volume = v));
        if (Display.HasBrightness) sp.Children.Add(SliderRow("Яркость", p.Brightness, v => p.Brightness = v));

        sp.Children.Add(ComboRow("Питание Windows", Power.Modes.Select(m => (m.Key, m.Name)), p.PowerMode, v => p.PowerMode = v));
        if (Acer.Available)
            sp.Children.Add(ComboRow("Режим Nitro", Acer.SupportedModes().Select(m => (m.Id.ToString(), m.Name)), p.AcerMode?.ToString(), v => p.AcerMode = v == null ? null : int.Parse(v)));
        sp.Children.Add(ComboRow("Частота экрана", Display.RefreshRates().Select(r => (r.ToString(), $"{r} Гц")), p.RefreshRate?.ToString(), v => p.RefreshRate = v == null ? null : int.Parse(v)));
        sp.Children.Add(ComboRow("Не беспокоить", new[] { ("1", "Вкл"), ("0", "Выкл") }, p.DoNotDisturb is bool d ? (d ? "1" : "0") : null, v => p.DoNotDisturb = v == null ? null : v == "1"));
        sp.Children.Add(ComboRow("Ночной свет", new[] { ("1", "Вкл"), ("0", "Выкл") }, p.NightLight is bool nl ? (nl ? "1" : "0") : null, v => p.NightLight = v == null ? null : v == "1"));
        sp.Children.Add(ComboRow("Звук через", Audio.Devices(DataFlow.Render).Select(a => (a.Id, a.Name)), p.AudioDevice, v => p.AudioDevice = v));

        var clean = new CheckBox { Content = "Чистить память и DNS при включении", IsChecked = p.CleanOnApply, Margin = new Thickness(0, 6, 0, 0) };
        clean.Click += (_, _) => { p.CleanOnApply = clean.IsChecked == true; Save(); };
        sp.Children.Add(clean);

        var btns = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        var apply = new Button { Content = "Применить", Style = (Style)FindResource("AccentButton") };
        apply.Click += (_, _) => Profiles.Apply(p);
        var del = new Button { Content = "Удалить" };
        del.Click += (_, _) =>
        {
            if (!UiKit.Confirm($"Удалить профиль «{p.Name}»?")) return;
            Settings.Current.Profiles.Remove(p);
            Save(); Render();
        };
        btns.Children.Add(apply); btns.Children.Add(del);
        sp.Children.Add(btns);
        var card = UiKit.Card(sp);
        if (Profiles.Active == p.Name) card.BorderBrush = (Brush)FindResource("AccentBrush");
        return card;
    }

    UIElement SliderRow(string label, int? value, Action<int?> set)
    {
        var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        var cb = new CheckBox { Content = label, IsChecked = value != null };
        var sl = new Slider { Maximum = 100, Value = value ?? 50, IsEnabled = value != null, VerticalAlignment = VerticalAlignment.Center };
        var t = UiKit.Text(value?.ToString() ?? "", 12); t.HorizontalAlignment = HorizontalAlignment.Right; t.VerticalAlignment = VerticalAlignment.Center;
        cb.Click += (_, _) => { sl.IsEnabled = cb.IsChecked == true; set(cb.IsChecked == true ? (int)sl.Value : null); t.Text = cb.IsChecked == true ? $"{sl.Value:0}" : ""; Save(); };
        sl.ValueChanged += (_, e) => { if (cb.IsChecked == true) { set((int)e.NewValue); t.Text = $"{e.NewValue:0}"; } };
        sl.LostMouseCapture += (_, _) => Save();
        Grid.SetColumn(sl, 1); Grid.SetColumn(t, 2);
        g.Children.Add(cb); g.Children.Add(sl); g.Children.Add(t);
        return g;
    }

    UIElement ComboRow(string label, IEnumerable<(string Key, string Name)> options, string? current, Action<string?> set)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var opts = new List<(string? Key, string Name)> { (null, "— не менять —") };
        opts.AddRange(options.Select(o => ((string?)o.Key, o.Name)));
        var cb = new ComboBox { MinHeight = 30 };
        foreach (var o in opts) cb.Items.Add(o.Name);
        cb.SelectedIndex = Math.Max(0, opts.FindIndex(o => o.Key == current));
        cb.SelectionChanged += (_, _) => { set(opts[cb.SelectedIndex].Key); Save(); };
        Grid.SetColumn(cb, 1);
        g.Children.Add(cb);
        return g;
    }

    static void Save() => Settings.Save();

    void New_Click(object s, RoutedEventArgs e)
    {
        var name = UiKit.Ask("Новый профиль", "Как назвать?", "Мой профиль");
        if (name.Length == 0) return;
        Settings.Current.Profiles.Add(new Profile { Name = name });
        Save(); Render();
    }

    void Snapshot_Click(object s, RoutedEventArgs e)
    {
        var name = UiKit.Ask("Сохранить текущее", "Запомню громкость, яркость, режимы и частоту экрана как они сейчас.", "Как сейчас");
        if (name.Length == 0) return;
        Settings.Current.Profiles.Add(Profiles.Snapshot(name));
        Save(); Render();
    }

    void Auto_Click(object s, RoutedEventArgs e)
    {
        Settings.Current.AutoProfiles = Auto.IsChecked == true;
        AutoPanel.IsEnabled = Settings.Current.AutoProfiles;
        Save();
    }

    void Power_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        string? Val(ComboBox cb) => cb.SelectedIndex <= 0 ? null : cb.SelectedItem as string;
        Settings.Current.OnBatteryProfile = Val(OnBattery);
        Settings.Current.OnAcProfile = Val(OnAc);
        Save();
    }

    void RenderRules()
    {
        Rules.Children.Clear();
        foreach (var r in Settings.Current.AppRules)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var del = new Button { Content = "", FontFamily = (FontFamily)FindResource("IconFont"), Style = (Style)FindResource("GhostButton") };
            del.Click += (_, _) => { Settings.Current.AppRules.Remove(r); Save(); RenderRules(); };
            DockPanel.SetDock(del, Dock.Right);
            row.Children.Add(del);
            row.Children.Add(UiKit.Text($"{r.Process}  →  {r.Profile}", 13));
            Rules.Children.Add(row);
        }
    }

    void AddRule_Click(object s, RoutedEventArgs e)
    {
        var proc = RuleProc.Text.Trim().Replace(".exe", "", StringComparison.OrdinalIgnoreCase);
        if (proc.Length == 0 || RuleProfile.SelectedItem is not string prof) return;
        Settings.Current.AppRules.Add(new AppRule { Process = proc, Profile = prof });
        RuleProc.Text = "";
        Save(); RenderRules();
    }

    void PickProc_Click(object s, RoutedEventArgs e)
    {
        var names = Process.GetProcesses().Where(p => { try { return p.MainWindowHandle != IntPtr.Zero; } catch { return false; } })
            .Select(p => p.ProcessName).Distinct().OrderBy(n => n).ToList();
        var menu = new ContextMenu();
        foreach (var n in names)
        {
            var mi = new MenuItem { Header = n };
            mi.Click += (_, _) => RuleProc.Text = n;
            menu.Items.Add(mi);
        }
        menu.PlacementTarget = (UIElement)s;
        menu.IsOpen = true;
    }
}
