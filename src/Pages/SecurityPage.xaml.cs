using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

public partial class SecurityPage : UserControl
{
    static readonly (int Days, string Name)[] Intervals = { (0, "Выключена"), (1, "Каждый день"), (3, "Раз в 3 дня"), (7, "Раз в неделю") };
    static Task? _scan;          // survives page navigation
    static string _scanText = "";
    static DateTime _scanStart;
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    public SecurityPage()
    {
        InitializeComponent();
        foreach (var (_, n) in Intervals) Schedule.Items.Add(n);
        Schedule.SelectedIndex = Math.Max(0, Array.FindIndex(Intervals, i => i.Days == Settings.Current.ScanIntervalDays));
        _tick.Tick += (_, _) => ShowScanState();
        Loaded += async (_, _) =>
        {
            _tick.Start();
            ShowScanState();
            await LoadStatus();
            LoadThreats();
            await LoadQuarantine();
        };
        Unloaded += (_, _) => _tick.Stop();
    }

    async Task LoadStatus()
    {
        var s = await Task.Run(Security.Status);
        Status.Text = (s.Passive ? $"ℹ Основной антивирус — {s.OtherAv}. Defender в пассивном режиме: проверки по запросу работают, постоянную защиту ведёт {s.OtherAv}" : s.Enabled ? "✅ Антивирус Defender работает" : "⚠ Defender выключен" + (s.OtherAv.Length > 0 ? $" (используется {s.OtherAv})" : "")) +
                      $"\n{(s.RealTime ? "✅" : "⚠")} Защита в реальном времени: {(s.RealTime ? "включена" : "выключена")}" +
                      $"\nБазы: {s.SigVersion} · {(s.SigUpdated == default ? "?" : s.SigUpdated.ToString("dd.MM.yyyy HH:mm"))}" +
                      (s.SigUpdated != default && (DateTime.Now - s.SigUpdated).TotalDays > 3 ? "  ⚠ устарели" : "") +
                      $"\nБыстрая проверка: {(s.LastQuick == default ? "не было" : s.LastQuick.ToString("dd.MM HH:mm"))} · полная: {(s.LastFull == default ? "не было" : s.LastFull.ToString("dd.MM HH:mm"))}";
    }

    void LoadThreats()
    {
        Threats.Children.Clear();
        var list = Security.Threats();
        foreach (var t in list.Take(30))
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            var head = UiKit.Text($"{(t.Resolved ? "✅" : "⚠")} {t.Name}", 13, weight: FontWeights.SemiBold);
            if (!t.Resolved) head.Foreground = (Brush)FindResource("BadBrush");
            sp.Children.Add(head);
            sp.Children.Add(UiKit.Text($"Опасность: {t.Severity} · {t.When:dd.MM.yyyy HH:mm} · {(t.Resolved ? "обезврежена" : "требует действия")}", 11, true));
            sp.Children.Add(new TextBlock { Text = t.Resources, FontSize = 11, Foreground = (Brush)FindResource("DimBrush"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = t.Resources });
            if (!t.Resolved)
            {
                var b = new Button { Content = "Удалить угрозу", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
                b.Click += async (_, _) => { b.IsEnabled = false; await Security.RemoveThreat(t.Id); LoadThreats(); };
                sp.Children.Add(b);
            }
            Threats.Children.Add(sp);
        }
        if (list.Count == 0) Threats.Children.Add(UiKit.Text("Угроз не обнаружено 👍", 13));
    }

    async Task LoadQuarantine()
    {
        Quarantine.Children.Clear();
        Quarantine.Children.Add(UiKit.Text("Загружаю…", 12, true));
        var q = await Security.Quarantine();
        Quarantine.Children.Clear();
        foreach (var name in q)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var b = new Button { Content = "Восстановить", Margin = new Thickness(0) };
            b.Click += async (_, _) =>
            {
                if (!UiKit.Confirm($"Восстановить «{name}» из карантина? Делай это, только если уверен, что файл безопасен.")) return;
                await Security.Restore(name);
                await LoadQuarantine();
            };
            DockPanel.SetDock(b, Dock.Right);
            row.Children.Add(b);
            row.Children.Add(new TextBlock { Text = name, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
            Quarantine.Children.Add(row);
        }
        if (q.Count == 0) Quarantine.Children.Add(UiKit.Text("Карантин пуст", 13));
    }

    void ShowScanState()
    {
        bool running = _scan is { IsCompleted: false };
        ScanBar.Visibility = CancelBtn.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        QuickBtn.IsEnabled = FullBtn.IsEnabled = CustomBtn.IsEnabled = FileBtn.IsEnabled = !running;
        ScanStatus.Text = running ? $"{_scanText} · {DateTime.Now - _scanStart:mm\\:ss}" : _scanText;
    }

    void StartScan(Security.ScanKind kind, string? path, string label)
    {
        _scanStart = DateTime.Now;
        _scanText = label;
        _scan = Run();
        ShowScanState();

        async Task Run()
        {
            var (threats, output) = await Security.Scan(kind, path);
            _scanText = threats ? "⚠ Найдены угрозы! Смотри список ниже." : output.Contains("cancel", StringComparison.OrdinalIgnoreCase) ? "Проверка остановлена" : "✅ Угроз не найдено";
            Toast.Show(threats ? "" : "", threats ? "Найдены угрозы" : "Проверка завершена", _scanText, () => App.Current.Navigate("Security"));
            if (IsLoaded) { LoadThreats(); await LoadStatus(); }
        }
    }

    void Quick_Click(object s, RoutedEventArgs e) => StartScan(Security.ScanKind.Quick, null, "Быстрая проверка (память, автозагрузка, системные папки)…");

    void Full_Click(object s, RoutedEventArgs e)
    {
        if (UiKit.Confirm("Полная проверка смотрит каждый файл на всех дисках и может идти час и больше. Запустить?"))
            StartScan(Security.ScanKind.Full, null, "Полная проверка всех файлов…");
    }

    void Custom_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFolderDialog();
        if (d.ShowDialog() == true) StartScan(Security.ScanKind.Custom, d.FolderName, $"Проверка {d.FolderName}…");
    }

    void File_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog();
        if (d.ShowDialog() == true) StartScan(Security.ScanKind.Custom, d.FileName, $"Проверка {Path.GetFileName(d.FileName)}…");
    }

    async void Cancel_Click(object s, RoutedEventArgs e) => await Security.CancelScan();

    async void Update_Click(object s, RoutedEventArgs e)
    {
        UpdBtn.IsEnabled = false; UpdBtn.Content = "Обновляю…";
        await Security.UpdateSignatures();
        UpdBtn.IsEnabled = true; UpdBtn.Content = "Обновить базы";
        await LoadStatus();
    }

    void OpenWinSec_Click(object s, RoutedEventArgs e) => Shell.Open("windowsdefender:");
    async void Quarantine_Click(object s, RoutedEventArgs e) => await LoadQuarantine();

    void Schedule_Changed(object s, SelectionChangedEventArgs e)
    {
        Settings.Current.ScanIntervalDays = Intervals[Schedule.SelectedIndex].Days;
        Settings.Save();
    }

    async void Sweep_Click(object s, RoutedEventArgs e)
    {
        SweepBtn.IsEnabled = false;
        Suspects.Children.Clear();
        var list = await Security.Sweep(p => Dispatcher.BeginInvoke(() => SweepStatus.Text = p));
        SweepStatus.Text = list.Count == 0 ? "✅ Подозрительного не найдено" : $"Найдено пунктов: {list.Count} (сверху — самые подозрительные)";
        foreach (var x in list) Suspects.Children.Add(SuspectRow(x));
        SweepBtn.IsEnabled = true;
    }

    UIElement SuspectRow(Suspect x)
    {
        var sp = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        var head = UiKit.Text($"{(x.Score >= 5 ? "🔴" : x.Score >= 3 ? "🟠" : "🟡")} {x.Name}", 13, weight: FontWeights.SemiBold);
        sp.Children.Add(head);
        sp.Children.Add(UiKit.Text($"{x.Where} · {x.ReasonText}", 11, true));
        sp.Children.Add(new TextBlock { Text = x.Path, FontSize = 11, Foreground = (Brush)FindResource("DimBrush"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = x.Path });
        var result = UiKit.Text("", 12);
        var btns = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        Button B(string t, Action a) { var b = new Button { Content = t, Padding = new Thickness(10, 4, 10, 4) }; b.Click += (_, _) => a(); btns.Children.Add(b); return b; }
        B("VirusTotal", async () => { result.Text = "Проверяю…"; result.Text = VtText(await Security.VirusTotal(x.Path)); });
        B("Проверить Defender", () => StartScan(Security.ScanKind.Custom, x.Path, $"Проверка {Path.GetFileName(x.Path)}…"));
        B("Показать", () => Shell.Open("explorer.exe", $"/select,\"{x.Path}\""));
        if (x.Startup != null && x.Startup.Enabled) B("Убрать из автозагрузки", () => { StartupApps.SetEnabled(x.Startup, false); result.Text = "Отключено в автозагрузке"; });
        if (x.Pid is int pid)
            B("Завершить", () =>
            {
                if (!UiKit.Confirm($"Завершить процесс «{x.Name}»?")) return;
                try { Process.GetProcessById(pid).Kill(true); result.Text = "Процесс завершён"; } catch (Exception ex) { result.Text = ex.Message; }
            });
        sp.Children.Add(btns);
        sp.Children.Add(result);
        return sp;
    }

    static string VtText(VtResult r)
    {
        if (r.Error != null) return "⚠ " + r.Error;
        if (!r.Found) return "Файл ещё не проверяли на VirusTotal. Можно загрузить его вручную на сайте: " + r.Link;
        return r.Malicious > 0
            ? $"🔴 Вредоносный по мнению {r.Malicious} антивирусов (подозрительный — {r.Suspicious}, чисто — {r.Clean}). {r.Link}"
            : r.Suspicious > 0 ? $"🟠 Подозрительный: {r.Suspicious}, чисто: {r.Clean}. {r.Link}" : $"✅ Чисто ({r.Clean} антивирусов). {r.Link}";
    }

    async void Vt_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog();
        if (d.ShowDialog() != true) return;
        VtBtn.IsEnabled = false;
        VtResult.Text = "Считаю отпечаток и спрашиваю VirusTotal…";
        VtResult.Text = VtText(await Security.VirusTotal(d.FileName));
        VtBtn.IsEnabled = true;
    }
}
