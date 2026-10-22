using System.Windows;
using System.Windows.Controls;
using MishkaHelper.Core;
using NAudio.CoreAudioApi;

namespace MishkaHelper.Pages;

public partial class ControlsPage : UserControl
{
    bool _loading = true;
    static readonly (int Min, string Name)[] Timeouts = { (1, "1 мин"), (2, "2 мин"), (5, "5 мин"), (10, "10 мин"), (15, "15 мин"), (30, "30 мин"), (60, "1 час"), (0, "Никогда") };

    public ControlsPage()
    {
        InitializeComponent();
        foreach (var t in UiKit.StandardTiles()) Tiles.Children.Add(t);

        // Sound
        Vol.Value = Audio.Volume;
        Mic.Value = Audio.MicVolume;
        UpdateMuteIcons();
        UiKit.FillOutputs(Output);
        FillInputs();
        AutoSwitch.IsChecked = Settings.Current.AutoSwitchAudio;
        RefreshMixer(null, null);

        // Display
        if (Display.HasBrightness) Bright.Value = Display.Brightness; else Bright.IsEnabled = false;
        var cur = Display.RefreshRate;
        foreach (var hz in Display.RefreshRates())
        {
            var rb = new RadioButton { Style = (Style)FindResource("Segment"), Content = $"{hz} Гц", GroupName = "hz", IsChecked = hz == cur };
            rb.Checked += (_, _) => { if (!_loading) { Display.SetMode(hz: hz); Osd.Show("", $"{hz} Гц"); } };
            Rates.Children.Add(rb);
        }
        var (w, h) = Display.Resolution;
        foreach (var r in Display.Resolutions()) Res.Items.Add($"{r.W} × {r.H}");
        Res.SelectedItem = $"{w} × {h}";

        // Power
        WinModes.Children.Add(UiKit.PowerModes());
        if (Acer.Available)
        {
            // Acer modes already shown above; still offer the Windows overlay underneath.
            var wp = new WrapPanel();
            var curMode = Power.Mode;
            foreach (var (key, name, _) in Power.Modes)
            {
                var rb = new RadioButton { Style = (Style)FindResource("Segment"), Content = "Windows: " + name, GroupName = "winpm", IsChecked = key == curMode };
                rb.Checked += (_, _) => { if (!_loading) Power.Mode = key; };
                wp.Children.Add(rb);
            }
            WinModes.Children.Add(wp);
        }
        var plans = Power.Plans();
        Plans.ItemsSource = plans;
        Plans.SelectedItem = plans.FirstOrDefault(p => p.Active);
        foreach (var (_, name) in Timeouts) { ScreenOff.Items.Add(name); SleepOff.Items.Add(name); }
        bool ac = Power.OnAc;
        var so = Power.ScreenTimeout; var sl = Power.SleepTimeout;
        ScreenOff.SelectedIndex = Closest(ac ? so.Ac : so.Dc);
        SleepOff.SelectedIndex = Closest(ac ? sl.Ac : sl.Dc);
        ShowBattery();

        // Tweaks
        AddSwitch("Тёмная тема Windows", () => Tweaks.DarkTheme, v => Tweaks.DarkTheme = v);
        AddSwitch("Эффекты прозрачности", () => Tweaks.Transparency, v => Tweaks.Transparency = v);
        AddSwitch("Игровой режим Windows", () => Tweaks.GameMode, v => Tweaks.GameMode = v);
        AddSwitch("Анимации интерфейса", () => Tweaks.Animations, v => Tweaks.Animations = v);
        AddSwitch("Показывать расширения файлов", () => Tweaks.ShowExtensions, v => Tweaks.ShowExtensions = v);
        AddSwitch("Показывать скрытые файлы", () => Tweaks.ShowHidden, v => Tweaks.ShowHidden = v);
        AddSwitch("Залипание клавиш по 5×Shift", () => Tweaks.StickyKeysShortcut, v => Tweaks.StickyKeysShortcut = v);
        AddSwitch("Повышенная точность указателя (ускорение мыши)", () => Tweaks.MouseAcceleration, v => Tweaks.MouseAcceleration = v);
        AddTouchpad();
        MouseSpeed.Value = Tweaks.MouseSpeed;

        _loading = false;
        Audio.Changed += OnAudioChanged;
        Unloaded += (_, _) => Audio.Changed -= OnAudioChanged;
    }

    static int Closest(int min) => Array.FindIndex(Timeouts, t => t.Min == min) is var i and >= 0 ? i : Timeouts.Length - 1;

    void OnAudioChanged() => Dispatcher.BeginInvoke(() =>
    {
        _loading = true;
        UiKit.FillOutputs(Output);
        FillInputs();
        Vol.Value = Audio.Volume;
        UpdateMuteIcons();
        _loading = false;
    });

    void FillInputs()
    {
        var list = Audio.Devices(DataFlow.Capture);
        Input.ItemsSource = list;
        Input.SelectedItem = list.FirstOrDefault(d => d.IsDefault);
    }

    void UpdateMuteIcons()
    {
        MuteBtn.Content = Audio.Muted ? "" : "";
        MicBtn.Content = Audio.MicMuted ? "" : "";
    }

    void ShowBattery()
    {
        var b = Power.Battery();
        if (!b.Present) { Battery.Text = "Батареи нет"; return; }
        Battery.Text = $"Батарея: {b.Percent}% · " + (b.OnAc ? (b.Charging ? "заряжается" : "от сети") : b.Remaining is { } r ? $"осталось ~{r:h\\:mm}" : "от батареи") +
                       (b.DesignMwh > 0 ? $"\nИзнос: {b.Wear:0}% ({b.FullMwh / 1000.0:0.0} из {b.DesignMwh / 1000.0:0.0} Вт·ч)" : "") +
                       (b.Cycles > 0 ? $" · циклов: {b.Cycles}" : "") +
                       (b.RateMw > 0 ? $"\n{(b.Charging ? "Заряд" : "Расход")}: {b.RateMw / 1000.0:0.0} Вт" : "");
    }

    void AddSwitch(string text, Func<bool> get, Action<bool> set)
    {
        var cb = new CheckBox { Style = (Style)FindResource("Switch"), Content = text, Margin = new Thickness(0, 4, 24, 4) };
        try { cb.IsChecked = get(); } catch { cb.IsEnabled = false; }
        cb.Click += (_, _) => { try { set(cb.IsChecked == true); } catch (Exception e) { Log.Error(e); Osd.Show("", e.Message); } };
        Switches.Children.Add(cb);
    }

    async void AddTouchpad()
    {
        var tp = await Task.Run(Touchpad.Find);
        if (tp == null) return;
        var cb = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Тачпад", IsChecked = tp.Value.Enabled, Margin = new Thickness(0, 4, 24, 4) };
        cb.Click += async (_, _) => await Touchpad.Set(tp.Value.Id, cb.IsChecked == true);
        Switches.Children.Add(cb);
    }

    void Vol_Changed(object s, RoutedPropertyChangedEventArgs<double> e) { if (!_loading) Audio.Volume = (int)e.NewValue; }
    void Mic_Changed(object s, RoutedPropertyChangedEventArgs<double> e) { if (!_loading) Audio.MicVolume = (int)e.NewValue; }
    void Mute_Click(object s, RoutedEventArgs e) { Audio.Muted = !Audio.Muted; UpdateMuteIcons(); }
    void Mic_Click(object s, RoutedEventArgs e) { Audio.MicMuted = !Audio.MicMuted; UpdateMuteIcons(); }

    void Output_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_loading || Output.Tag != null || Output.SelectedItem is not AudioDevice d || d.IsDefault) return;
        Audio.SetDefault(d.Id);
    }

    void Input_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_loading || Input.SelectedItem is not AudioDevice d || d.IsDefault) return;
        Audio.SetDefault(d.Id);
    }

    void AutoSwitch_Click(object s, RoutedEventArgs e) { Settings.Current.AutoSwitchAudio = AutoSwitch.IsChecked == true; Settings.Save(); }

    void RefreshMixer(object? s, RoutedEventArgs? e)
    {
        Mixer.Children.Clear();
        foreach (var app in Audio.Apps())
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            g.Children.Add(new TextBlock { Text = app.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = app.Name });
            var sl = new Slider { Maximum = 100, Value = app.Volume * 100, VerticalAlignment = VerticalAlignment.Center };
            var val = new TextBlock { Text = $"{app.Volume * 100:0}", HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            sl.ValueChanged += (_, ev) => { app.Session.SimpleAudioVolume.Volume = (float)(ev.NewValue / 100); val.Text = $"{ev.NewValue:0}"; };
            Grid.SetColumn(sl, 1); Grid.SetColumn(val, 2);
            g.Children.Add(sl); g.Children.Add(val);
            Mixer.Children.Add(g);
        }
        if (Mixer.Children.Count == 0) Mixer.Children.Add(UiKit.Text("Сейчас ничего не играет", 12, true));
    }

    void Bright_Changed(object s, RoutedPropertyChangedEventArgs<double> e) { if (!_loading) Display.Brightness = (int)e.NewValue; }

    void Res_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_loading || Res.SelectedItem is not string r) return;
        var p = r.Split(" × ");
        int w = int.Parse(p[0]), h = int.Parse(p[1]);
        var old = Display.Resolution;
        if (!Display.SetMode(w, h)) { Osd.Show("", "Не удалось сменить разрешение"); return; }
        if (!UiKit.Confirm("Оставить новое разрешение?"))
        {
            Display.SetMode(old.W, old.H);
            _loading = true; Res.SelectedItem = $"{old.W} × {old.H}"; _loading = false;
        }
    }

    void Plans_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_loading || Plans.SelectedItem == null) return;
        Power.SetPlan(((PowerPlan)Plans.SelectedItem).Id);
    }

    void Timeouts_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        // Same value for mains and battery keeps it simple; Windows Settings still allows splitting them.
        if (s == ScreenOff) { var m = Timeouts[ScreenOff.SelectedIndex].Min; Power.SetScreenTimeout(m, m); }
        else { var m = Timeouts[SleepOff.SelectedIndex].Min; Power.SetSleepTimeout(m, m); }
    }

    void MouseSpeed_Changed(object s, RoutedPropertyChangedEventArgs<double> e) { if (!_loading) Tweaks.MouseSpeed = (int)e.NewValue; }
    void DisplaySettings_Click(object s, RoutedEventArgs e) => Shell.Open("ms-settings:display");
    void Personalize_Click(object s, RoutedEventArgs e) => Shell.Open("ms-settings:personalization");
}
