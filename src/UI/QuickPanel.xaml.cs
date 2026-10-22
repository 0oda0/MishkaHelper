using System.Windows;
using System.Windows.Controls;
using NAudio.CoreAudioApi;

namespace MishkaHelper.Core;

public partial class QuickPanel : Window
{
    static QuickPanel? _open;
    bool _loading = true;
    IDisposable? _live;

    public static void Toggle()
    {
        if (_open != null) { _open.Close(); return; }
        _open = new QuickPanel();
        _open.Closed += (_, _) => _open = null;
        _open.Show();
        _open.Activate();
    }

    public QuickPanel()
    {
        InitializeComponent();
        foreach (var t in UiKit.StandardTiles()) { t.Width = 116; Tiles.Children.Add(t); }
        Vol.Value = Audio.Volume;
        MuteBtn.Content = Audio.Muted ? "" : "";
        if (Display.HasBrightness) Bright.Value = Display.Brightness; else Bright.IsEnabled = false;
        UiKit.FillOutputs(Output);
        Modes.Children.Add(UiKit.PowerModes());
        ProfilesPanel.Children.Add(UiKit.ProfileButtons());
        _loading = false;

        Loaded += (_, _) =>
        {
            var wa = SystemParameters.WorkArea;
            Left = wa.Right - ActualWidth + 4;
            Top = wa.Bottom - ActualHeight + 4;
        };
        Deactivated += (_, _) => Close();
        KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) Close(); };
        _live = SysMon.Live();
        SysMon.Updated += OnSample;
        Closed += (_, _) => { SysMon.Updated -= OnSample; _live?.Dispose(); };
        OnSample(SysMon.Last);
    }

    void OnSample(Sample s) => Dispatcher.BeginInvoke(() =>
        Stats.Text = $"CPU {s.Cpu:0}%  ·  RAM {s.Ram:0}%" + (s.CpuTemp > 0 ? $"  ·  {s.CpuTemp}°C" : ""));

    void Vol_Changed(object s, RoutedPropertyChangedEventArgs<double> e) { if (!_loading) Audio.Volume = (int)e.NewValue; }
    void Bright_Changed(object s, RoutedPropertyChangedEventArgs<double> e) { if (!_loading) Display.Brightness = (int)e.NewValue; }

    void Mute_Click(object s, RoutedEventArgs e)
    {
        Audio.Muted = !Audio.Muted;
        MuteBtn.Content = Audio.Muted ? "" : "";
    }

    void Output_Changed(object s, SelectionChangedEventArgs e)
    {
        if (_loading || Output.Tag != null || Output.SelectedItem is not AudioDevice d || d.IsDefault) return;
        Audio.SetDefault(d.Id);
    }

    async void Boost_Click(object s, RoutedEventArgs e)
    {
        BoostText.Text = "Чищу…";
        var freed = await Cleaner.BoostAll(p => Dispatcher.BeginInvoke(() => BoostText.Text = p));
        BoostText.Text = $"Готово: {Cleaner.Size(freed)}";
    }

    void Open_Click(object s, RoutedEventArgs e) { Close(); App.Current.ShowMain(); }
    void Quit_Click(object s, RoutedEventArgs e) => App.Current.Quit();
}
