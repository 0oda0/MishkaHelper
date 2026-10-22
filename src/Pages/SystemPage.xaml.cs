using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

public partial class SystemPage : UserControl
{
    IDisposable? _live;
    readonly DispatcherTimer _procTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    readonly List<double> _net = new(), _fan = new();

    public SystemPage()
    {
        InitializeComponent();
        TempChart.Stroke = (Brush)FindResource("Accent2Brush");
        HistTemp.Stroke = (Brush)FindResource("Accent2Brush");
        if (!Acer.Available) FanCard.Visibility = Visibility.Collapsed;

        Loaded += async (_, _) =>
        {
            _live = SysMon.Live();
            SysMon.Updated += OnSample;
            OnSample(SysMon.Last);
            H24.IsChecked = true;
            _procTimer.Tick += (_, _) => LoadProcs();
            _procTimer.Start();
            SystemInfo.TopProcesses();
            Summary.ItemsSource = await Task.Run(SystemInfo.Summary);
            LoadDrives();
            var disks = await Task.Run(SystemInfo.Disks);
            foreach (var d in disks)
            {
                var bad = d.Health != "Исправен";
                Health.Children.Add(UiKit.Text($"{(bad ? "⚠" : "✅")} {d.Name} ({d.Media}, {d.SizeGb:0} ГБ) — {d.Health}" +
                    (d.Temp is int t ? $" · {t}°C" : "") + (d.Wear is int w ? $" · износ {w}%" : "") + (d.Hours is int h ? $" · {h} ч работы" : ""), 13));
            }
            if (disks.Count == 0) Health.Children.Add(UiKit.Text("Нет данных", 12, true));
        };
        Unloaded += (_, _) => { SysMon.Updated -= OnSample; _live?.Dispose(); _procTimer.Stop(); };
    }

    void OnSample(Sample s) => Dispatcher.BeginInvoke(() =>
    {
        Sample[] r;
        lock (SysMon.Recent) r = SysMon.Recent.ToArray();
        CpuVal.Text = $"{s.Cpu:0}%"; CpuSub.Text = s.CpuMhz > 0 ? $"{s.CpuMhz / 1000:0.00} ГГц" : "";
        GpuVal.Text = $"{s.Gpu:0}%"; GpuSub.Text = s.VramUsedGb > 0 ? $"VRAM {s.VramUsedGb:0.0} ГБ" : "";
        RamVal.Text = $"{s.Ram:0}%"; RamSub.Text = $"{s.RamUsedGb:0.0} из {s.RamTotalGb:0.0} ГБ";
        TempVal.Text = s.CpuTemp > 0 ? $"{s.CpuTemp}°" : "—";
        TempSub.Text = s.CpuTemp > 0 ? $"CPU {s.CpuTemp}°" + (s.GpuTemp > 0 ? $" · GPU {s.GpuTemp}°" : "") + (s.SysTemp > 0 ? $" · корпус {s.SysTemp}°" : "") : "Нет датчика";
        DiskVal.Text = $"{s.DiskPct:0}%"; DiskSub.Text = $"Чтение {SysMon.Speed(s.DiskRead)} · запись {SysMon.Speed(s.DiskWrite)}";
        NetVal.Text = $"↓ {SysMon.Speed(s.NetDown)}"; NetSub.Text = $"↑ {SysMon.Speed(s.NetUp)}";
        FanVal.Text = $"{s.CpuFan} / {s.GpuFan}"; FanSub.Text = "об/мин · CPU / GPU";
        var b = Power.Battery();
        BatVal.Text = b.Present ? $"{b.Percent}%" : "—";
        BatSub.Text = b.Present ? (b.OnAc ? "От сети" : "От батареи") + (b.DesignMwh > 0 ? $" · износ {b.Wear:0}%" : "") : "";

        Set(CpuChart, r.Select(x => x.Cpu)); Set(GpuChart, r.Select(x => x.Gpu)); Set(RamChart, r.Select(x => x.Ram));
        Set(TempChart, r.Select(x => (double)x.CpuTemp)); Set(DiskChart, r.Select(x => x.DiskPct));
        Set(NetChart, r.Select(x => x.NetDown)); Set(FanChart, r.Select(x => (double)x.CpuFan));
    });

    static void Set(Sparkline sp, IEnumerable<double> v) { sp.Values = v.ToArray(); sp.InvalidateVisual(); }

    void Hist_Checked(object sender, RoutedEventArgs e)
    {
        var hours = int.Parse((string)((FrameworkElement)sender).Tag);
        var pts = History.Read(TimeSpan.FromHours(hours));
        // Downsample to ~300 points so a week still draws fast.
        int step = Math.Max(1, pts.Count / 300);
        var ds = pts.Where((_, i) => i % step == 0).ToList();
        Set(HistCpu, ds.Select(p => p.Cpu));
        Set(HistTemp, ds.Select(p => (double)p.CpuTemp));
        HistInfo.Text = pts.Count == 0 ? "История копится, пока приложение работает (запись раз в минуту)." :
            $"Средняя загрузка {pts.Average(p => p.Cpu):0}% · пик температуры {pts.Max(p => p.CpuTemp)}°C · {pts.Count} мин данных";
    }

    void LoadDrives()
    {
        Drives.Children.Clear();
        foreach (var d in SystemInfo.Drives())
        {
            double used = 1 - (double)d.AvailableFreeSpace / d.TotalSize;
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            sp.Children.Add(UiKit.Text($"{d.Name.TrimEnd('\\')}  {d.VolumeLabel}  —  свободно {Cleaner.Size(d.AvailableFreeSpace)} из {Cleaner.Size(d.TotalSize)}", 13));
            var pb = new ProgressBar { Value = used * 100, Margin = new Thickness(0, 5, 0, 0) };
            if (used > 0.9) pb.Foreground = (Brush)FindResource("BadBrush");
            sp.Children.Add(pb);
            Drives.Children.Add(sp);
        }
    }

    void LoadProcs()
    {
        var list = SystemInfo.TopProcesses(10);
        Procs.Children.Clear();
        foreach (var p in list)
        {
            var g = new Grid { Margin = new Thickness(0, 1, 0, 1) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new TextBlock { Text = p.Name, VerticalAlignment = VerticalAlignment.Center });
            var cpu = UiKit.Text($"{p.Cpu:0.0}% CPU"); Grid.SetColumn(cpu, 1); cpu.VerticalAlignment = VerticalAlignment.Center;
            var ram = UiKit.Text($"{p.RamMb:0} МБ", dim: true); Grid.SetColumn(ram, 2); ram.VerticalAlignment = VerticalAlignment.Center;
            var kill = new Button { Content = "Завершить", Style = (Style)FindResource("GhostButton"), Foreground = (Brush)FindResource("BadBrush") };
            int pid = p.Pid; string name = p.Name;
            kill.Click += (_, _) =>
            {
                if (!UiKit.Confirm($"Завершить «{name}»? Несохранённые данные в нём пропадут.")) return;
                try { Process.GetProcessById(pid).Kill(true); } catch (Exception ex) { Osd.Show("", ex.Message); }
            };
            Grid.SetColumn(kill, 3);
            g.Children.Add(cpu); g.Children.Add(ram); g.Children.Add(kill);
            Procs.Children.Add(g);
        }
    }
}
