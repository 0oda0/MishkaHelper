using System.Windows;
using System.Windows.Controls;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

public partial class HomePage : UserControl
{
    IDisposable? _live;

    public HomePage()
    {
        InitializeComponent();
        var h = DateTime.Now.Hour;
        Hello.Text = (h < 6 ? "Доброй ночи" : h < 12 ? "Доброе утро" : h < 18 ? "Добрый день" : "Добрый вечер") + ", Mishka";
        if (!Settings.Current.Onboarded) Guide.Content = FirstSteps();
        foreach (var t in UiKit.StandardTiles()) Tiles.Children.Add(t);
        Modes.Content = UiKit.PowerModes();
        ProfilesBox.Content = UiKit.ProfileButtons();
        var lc = Settings.Current.LastClean;
        LastClean.Text = lc == default ? "Ещё ни разу не запускалось" : $"Последний раз: {lc:dd.MM HH:mm}";

        Loaded += async (_, _) =>
        {
            _live = SysMon.Live();
            SysMon.Updated += OnSample;
            OnSample(SysMon.Last);
            var model = await Task.Run(SystemInfo.Model);
            Sub.Text = model;
            var st = await Task.Run(Security.Status);
            Sec.Text = (st.RealTime ? "✅ Защита в реальном времени включена" : "⚠ Защита в реальном времени выключена") +
                       $"\nБазы: {st.SigVersion} от {st.SigUpdated:dd.MM.yyyy}" +
                       (st.LastQuick != default ? $"\nПоследняя проверка: {st.LastQuick:dd.MM HH:mm}" : "") +
                       (st.OtherAv.Length > 0 ? $"\nДругой антивирус: {st.OtherAv}" : "");
            await LoadBatteries();
        };
        Unloaded += (_, _) => { SysMon.Updated -= OnSample; _live?.Dispose(); };
    }

    async Task LoadBatteries()
    {
        var b = Power.Battery();
        if (b.Present) Batteries.Children.Add(UiKit.Text($"Ноутбук: {b.Percent}%" + (b.OnAc ? " · от сети" : b.Remaining is { } r ? $" · осталось ~{r:h\\:mm}" : ""), 13));
        try
        {
            foreach (var d in (await Devices.List()).Where(d => d.Connected && d.Battery != null))
                Batteries.Children.Add(UiKit.Text($"{d.Name}: {d.Battery}%", 13));
        }
        catch { }
        if (Batteries.Children.Count == 0) Batteries.Children.Add(UiKit.Text("Нет устройств с батареей", 12, true));
    }

    void OnSample(Sample s) => Dispatcher.BeginInvoke(() =>
    {
        void Set(Ring r, double v) { r.Value = v; r.Label = $"{v:0}%"; r.InvalidateVisual(); }
        Set(RCpu, s.Cpu); Set(RGpu, s.Gpu); Set(RRam, s.Ram); Set(RDisk, s.DiskPct);
        Temps.Text = (s.CpuTemp > 0 ? $"CPU {s.CpuTemp}°C" + (s.GpuTemp > 0 ? $" · GPU {s.GpuTemp}°C" : "") + "   " : "") +
                     (s.CpuFan > 0 ? $"Вентиляторы {s.CpuFan} / {s.GpuFan} об/мин   " : "") +
                     $"↓ {SysMon.Speed(s.NetDown)}  ↑ {SysMon.Speed(s.NetUp)}";
    });

    async void Boost_Click(object sender, RoutedEventArgs e)
    {
        BoostBtn.IsEnabled = false;
        var freed = await Cleaner.BoostAll(p => Dispatcher.BeginInvoke(() => BoostText.Text = p));
        BoostText.Text = "Ускорить всё";
        BoostBtn.IsEnabled = true;
        LastClean.Text = $"Готово! Освобождено {Cleaner.Size(freed)}";
    }

    void Sec_Click(object sender, RoutedEventArgs e) => App.Current.Navigate("Security");

    /// <summary>First run: the four things worth knowing, each one click away; hidden for good after «Понятно».</summary>
    UIElement FirstSteps()
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "С чего начать", Style = (Style)FindResource("H2") });
        var steps = new List<(string Key, string Text, string Button, Action Go)>
        {
            ("Ctrl+Alt+R", "Колесо у курсора — профили, звук, музыка, программы. Настраивается под себя.", "Настроить колесо", () => App.Current.Navigate("Radial")),
            ("Ctrl+Alt+K", "Поиск любой функции: набери слово и нажми Enter.", "Попробовать", CommandPalette.Toggle),
            ("", "Полная очистка — мусор, автозагрузка, фоновые программы одной кнопкой.", "Открыть", () => App.Current.Navigate("Boost")),
        };
        if (Edition.HasJarvis) steps.Add(("Ctrl+Alt+J", "Джарвис — голосовой помощник. Один раз скачать модели — и «Джарвис, открой браузер».", "Настроить", () => App.Current.Navigate("Jarvis")));
        foreach (var (key, text, button, go) in steps)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var b = new Button { Content = button, Padding = new Thickness(12, 4, 12, 4) };
            b.Click += (_, _) => go();
            DockPanel.SetDock(b, Dock.Right); row.Children.Add(b);
            var k = UiKit.Text(key, 12); k.Width = 92; k.FontFamily = new System.Windows.Media.FontFamily("Consolas");
            k.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush"); k.VerticalAlignment = VerticalAlignment.Center;
            DockPanel.SetDock(k, Dock.Left); row.Children.Add(k);
            var t = UiKit.Text(text, 13); t.VerticalAlignment = VerticalAlignment.Center; t.Margin = new Thickness(0, 0, 10, 0);
            row.Children.Add(t);
            sp.Children.Add(row);
        }
        var ok = new Button { Content = "Понятно, скрыть", Style = (Style)FindResource("GhostButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        ok.Click += (_, _) => { Settings.Current.Onboarded = true; Settings.Save(); Guide.Content = null; };
        sp.Children.Add(ok);
        var card = UiKit.Card(sp);
        card.BorderBrush = (System.Windows.Media.Brush)FindResource("AccentBrush");
        card.Margin = new Thickness(0, 0, 0, 14);
        return card;
    }
}
