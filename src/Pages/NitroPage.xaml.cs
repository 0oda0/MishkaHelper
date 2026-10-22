using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

public partial class NitroPage : UserControl
{
    bool _loading = true;
    IDisposable? _live;
    int _effect;
    int _zone = 0xF;   // selected zones mask
    readonly Border[] _zoneBoxes = new Border[4];
    readonly Color[] _colors = new Color[4];

    static readonly string[] Palette = { "#FF2E97", "#A259FF", "#00C8FF", "#39FF9C", "#FFC53D", "#FF3355", "#FF8A3D", "#FFFFFF", "#4D6BFF", "#00FFD5" };

    public NitroPage()
    {
        InitializeComponent();
        Modes.Children.Add(UiKit.PowerModes());

        var fm = Acer.Fans;
        (fm == Acer.FanMode.Max ? FanMax : fm == Acer.FanMode.Custom ? FanCustom : FanAuto).IsChecked = true;
        CpuFan.Value = Acer.GetFanPercent(1);
        GpuFan.Value = Acer.GetFanPercent(4);
        CustomPanel.IsEnabled = fm == Acer.FanMode.Custom;

        for (int i = 0; i < Acer.Effects.Length; i++)
        {
            int e = i;
            var rb = new RadioButton { Style = (Style)FindResource("Segment"), Content = Acer.Effects[i], GroupName = "fx", IsChecked = i == 0 };
            rb.Checked += (_, _) => { _effect = e; if (!_loading) ApplyKeyboard(); };
            Effects.Children.Add(rb);
        }
        for (int z = 0; z < 4; z++)
        {
            var (r, g, b) = Acer.GetZone(1 << z);
            _colors[z] = Color.FromRgb(r, g, b);
            int zz = z;
            var box = new Border { CornerRadius = new CornerRadius(10), Margin = new Thickness(0, 0, 8, 0), Cursor = Cursors.Hand, BorderThickness = new Thickness(2) };
            box.MouseLeftButtonUp += (_, _) => { _zone = 1 << zz; HighlightZones(); };
            _zoneBoxes[z] = box;
            Zones.Children.Add(box);
        }
        HighlightZones();
        foreach (var hex in Palette)
        {
            var c = (Color)ColorConverter.ConvertFromString(hex);
            var sw = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(17), Margin = new Thickness(0, 0, 8, 8), Background = new SolidColorBrush(c), Cursor = Cursors.Hand, ToolTip = hex };
            sw.MouseLeftButtonUp += (_, _) => SetColor(c);
            Swatches.Children.Add(sw);
        }
        _loading = false;

        Loaded += (_, _) => { _live = SysMon.Live(); SysMon.Updated += OnSample; OnSample(SysMon.Last); };
        Unloaded += (_, _) => { SysMon.Updated -= OnSample; _live?.Dispose(); };
    }

    void OnSample(Sample s) => Dispatcher.BeginInvoke(() =>
        Sensors.Text = $"CPU: {s.CpuTemp}°C   ·   вентилятор {s.CpuFan} об/мин\nGPU: {s.GpuTemp}°C   ·   вентилятор {s.GpuFan} об/мин\nКорпус: {s.SysTemp}°C");

    void HighlightZones()
    {
        for (int z = 0; z < 4; z++)
        {
            _zoneBoxes[z].Background = new SolidColorBrush(_colors[z]);
            bool sel = (_zone & (1 << z)) != 0;
            _zoneBoxes[z].BorderBrush = sel ? (Brush)FindResource("TextBrush") : Brushes.Transparent;
            _zoneBoxes[z].Effect = sel ? new System.Windows.Media.Effects.DropShadowEffect { Color = _colors[z], BlurRadius = 16, ShadowDepth = 0 } : null;
        }
    }

    void SetColor(Color c)
    {
        Hex.Text = $"#{c.R:X2}{c.G:X2}{c.B:X2}";
        for (int z = 0; z < 4; z++) if ((_zone & (1 << z)) != 0) _colors[z] = c;
        HighlightZones();
        if (_effect == 0) Acer.SetZone(_zone, c.R, c.G, c.B);
        ApplyKeyboard();
    }

    void ApplyKeyboard()
    {
        // Static mode keeps per-zone colours; animated effects use one colour.
        var c = _colors[Enumerable.Range(0, 4).First(z => (_zone & (1 << z)) != 0)];
        Acer.SetKeyboard(_effect, _effect == 0 ? 0 : (int)KbSpeed.Value, (int)KbBright.Value, KbReverse.IsChecked == true ? 2 : 1, c.R, c.G, c.B);
        if (_effect == 0)
            for (int z = 0; z < 4; z++) Acer.SetZone(1 << z, _colors[z].R, _colors[z].G, _colors[z].B);
    }

    void Fan_Checked(object sender, RoutedEventArgs e)
    {
        CustomPanel.IsEnabled = FanCustom.IsChecked == true;
        if (_loading) return;
        Acer.Fans = FanMax.IsChecked == true ? Acer.FanMode.Max : FanCustom.IsChecked == true ? Acer.FanMode.Custom : Acer.FanMode.Auto;
        if (FanCustom.IsChecked == true) FanSpeed_Changed(null, null);
    }

    void FanSpeed_Changed(object? s, RoutedPropertyChangedEventArgs<double>? e)
    {
        if (_loading || FanCustom.IsChecked != true) return;
        Acer.SetFanPercent(1, (int)CpuFan.Value);
        Acer.SetFanPercent(4, (int)GpuFan.Value);
    }

    void Kb_Changed(object s, RoutedEventArgs e) { if (!_loading) ApplyKeyboard(); }
    void KbSlider_Changed(object s, RoutedPropertyChangedEventArgs<double> e) { if (!_loading) ApplyKeyboard(); }
    void AllZones_Click(object s, RoutedEventArgs e) { _zone = 0xF; HighlightZones(); }
    void Accent_Click(object s, RoutedEventArgs e) { _zone = 0xF; SetColor(ThemeManager.Accent); }

    void Hex_KeyDown(object s, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        try { SetColor((Color)ColorConverter.ConvertFromString(Hex.Text.Trim())); }
        catch { Osd.Show("", "Цвет в формате #RRGGBB"); }
    }
}
