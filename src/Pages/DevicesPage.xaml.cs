using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MishkaHelper.Core;
using NAudio.CoreAudioApi;

namespace MishkaHelper.Pages;

public partial class DevicesPage : UserControl
{
    public DevicesPage()
    {
        InitializeComponent();
        Loaded += async (_, _) => { LoadAudio(); await LoadDevices(); };
        Audio.Changed += OnAudio;
        Unloaded += (_, _) => Audio.Changed -= OnAudio;
    }

    void OnAudio() => Dispatcher.BeginInvoke(LoadAudio);

    void LoadAudio()
    {
        Fill(Outputs, DataFlow.Render);
        Fill(Inputs, DataFlow.Capture);
    }

    void Fill(StackPanel panel, DataFlow flow)
    {
        panel.Children.Clear();
        foreach (var d in Audio.Devices(flow))
        {
            var g = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var icon = UiKit.Icon(flow == DataFlow.Render ? (d.Name.Contains("наушн", StringComparison.OrdinalIgnoreCase) || d.Name.Contains("head", StringComparison.OrdinalIgnoreCase) ? "" : "") : "", 16);
            icon.Width = 28;
            if (d.IsDefault) icon.Foreground = (Brush)FindResource("AccentBrush");
            g.Children.Add(icon);
            if (d.IsDefault)
            {
                var tag = UiKit.Text("по умолчанию", 11);
                tag.Foreground = (Brush)FindResource("AccentBrush");
                tag.VerticalAlignment = VerticalAlignment.Center;
                DockPanel.SetDock(tag, Dock.Right);
                g.Children.Add(tag);
            }
            else
            {
                var b = new Button { Content = "Сделать основным", Margin = new Thickness(0), Padding = new Thickness(10, 4, 10, 4) };
                b.Click += (_, _) => { Audio.SetDefault(d.Id); LoadAudio(); };
                DockPanel.SetDock(b, Dock.Right);
                g.Children.Add(b);
            }
            g.Children.Add(new TextBlock { Text = d.Name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = d.IsDefault ? FontWeights.SemiBold : FontWeights.Normal });
            panel.Children.Add(g);
        }
    }

    async Task LoadDevices()
    {
        Loading.Visibility = Visibility.Visible;
        List<DeviceItem> items;
        try { items = await Devices.List(); }
        catch (Exception e) { Loading.Text = "Ошибка: " + e.Message; return; }
        Loading.Visibility = Visibility.Collapsed;
        List.Children.Clear();
        foreach (var d in items) List.Children.Add(Card(d));
        if (items.Count == 0) { Loading.Text = "Внешних устройств не найдено"; Loading.Visibility = Visibility.Visible; }
    }

    Border Card(DeviceItem d)
    {
        var sp = new StackPanel { Width = 250 };
        var head = new DockPanel();
        var ic = UiKit.Icon(d.Icon, 26);
        ic.Foreground = (Brush)FindResource(d.Connected && !d.Disabled ? "AccentBrush" : "DimBrush");
        ic.Margin = new Thickness(0, 0, 12, 0);
        if (d.Connected && !d.Disabled) ic.Effect = (System.Windows.Media.Effects.Effect)FindResource("SoftGlow");
        head.Children.Add(ic);
        var title = new StackPanel();
        title.Children.Add(new TextBlock { Text = d.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = d.Name });
        title.Children.Add(UiKit.Text($"{d.Kind}{(d.Bluetooth ? " · Bluetooth" : "")} · {d.Status}", 11, true));
        head.Children.Add(title);
        sp.Children.Add(head);

        if (d.Battery is int bat)
        {
            var row = new DockPanel { Margin = new Thickness(0, 10, 0, 0) };
            var t = UiKit.Text($"{bat}%", 13, weight: FontWeights.SemiBold);
            DockPanel.SetDock(t, Dock.Right);
            row.Children.Add(t);
            var pb = new ProgressBar { Value = bat, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            if (bat <= 20) pb.Foreground = (Brush)FindResource("BadBrush");
            row.Children.Add(pb);
            sp.Children.Add(row);
        }

        var btns = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        Button B(string text, Func<Task> act)
        {
            var b = new Button { Content = text, Padding = new Thickness(10, 5, 10, 5) };
            b.Click += async (_, _) =>
            {
                b.IsEnabled = false;
                try { await act(); } catch (Exception e) { Osd.Show("", e.Message); }
                await LoadDevices();
            };
            btns.Children.Add(b);
            return b;
        }
        if (d.Nodes.Count > 0)
            B(d.Disabled ? "Включить" : "Отключить", async () =>
            {
                if (!d.Disabled && (d.Kind is "Клавиатура" or "Мышь") && !UiKit.Confirm($"Отключить «{d.Name}»? Если это единственная мышь или клавиатура, включать обратно придётся другим устройством."))
                    return;
                await Devices.SetEnabled(d, d.Disabled);
            });
        if (d.Drive != null) B("Извлечь", () => { Devices.Eject(d.Drive); Osd.Show("", "Можно доставать"); return Task.CompletedTask; });
        if (d.Bluetooth && d.Paired) B("Забыть", async () => { if (UiKit.Confirm($"Удалить сопряжение с «{d.Name}»?")) await Devices.Unpair(d); });
        sp.Children.Add(btns);
        var card = UiKit.Card(sp);
        if (d.Connected && !d.Disabled) card.BorderBrush = (Brush)FindResource("AccentSoftBrush");
        return card;
    }

    async void Refresh_Click(object s, RoutedEventArgs e) { LoadAudio(); await LoadDevices(); }

    async void Scan_Click(object s, RoutedEventArgs e)
    {
        ScanBtn.IsEnabled = false;
        ScanBtn.Content = "Ищу…";
        Found.Children.Clear();
        try
        {
            var list = await Devices.DiscoverBluetooth();
            foreach (var d in list)
            {
                var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                var b = new Button { Content = "Подключить", Margin = new Thickness(0) };
                b.Click += async (_, _) =>
                {
                    b.IsEnabled = false; b.Content = "Подключаю…";
                    var r = await Devices.Pair(d);
                    b.Content = r is "Paired" or "AlreadyPaired" ? "Готово ✓" : "Ошибка: " + r;
                    await LoadDevices();
                };
                DockPanel.SetDock(b, Dock.Right);
                row.Children.Add(b);
                row.Children.Add(new TextBlock { Text = d.Name, VerticalAlignment = VerticalAlignment.Center });
                Found.Children.Add(row);
            }
            if (list.Count == 0) Found.Children.Add(UiKit.Text("Ничего не найдено. Проверь, что Bluetooth включён и устройство в режиме сопряжения.", 12, true));
        }
        catch (Exception ex) { Found.Children.Add(UiKit.Text("Ошибка: " + ex.Message, 12, true)); }
        ScanBtn.IsEnabled = true;
        ScanBtn.Content = "Найти новые устройства";
    }
}
