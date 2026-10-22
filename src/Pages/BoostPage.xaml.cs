using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

public partial class BoostPage : UserControl
{
    CancellationTokenSource? _cts;

    public BoostPage()
    {
        InitializeComponent();
        Loaded += (_, _) => TabClean.IsChecked = true;
        Unloaded += (_, _) => _cts?.Cancel();
    }

    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        Body.Content = (string)((FrameworkElement)sender).Tag switch
        {
            "clean" => CleanTab(_cts.Token),
            "startup" => StartupTab(),
            "large" => LargeTab(_cts.Token),
            "dups" => DupsTab(_cts.Token),
            "apps" => AppsTab(),
            _ => null,
        };
    }

    static Style S(string key) => (Style)Application.Current.Resources[key];

    // ---------------- Cleanup ----------------
    UIElement CleanTab(CancellationToken ct)
    {
        var root = new StackPanel();
        var targets = Cleaner.Targets();
        var sel = Settings.Current.CleanTargets;
        var list = new StackPanel();
        var sizes = new Dictionary<string, TextBlock>();
        foreach (var t in targets)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
            var cb = new CheckBox { IsChecked = sel.Contains(t.Key) };
            var txt = new StackPanel();
            txt.Children.Add(UiKit.Text(t.Name, 13, weight: FontWeights.SemiBold));
            txt.Children.Add(UiKit.Text(t.Hint, 11, true));
            cb.Content = txt;
            cb.Click += (_, _) =>
            {
                if (cb.IsChecked == true) { if (!sel.Contains(t.Key)) sel.Add(t.Key); } else sel.Remove(t.Key);
                Settings.Save();
            };
            var size = UiKit.Text(t.Special && t.Key != "recycle" ? "—" : "…", 13, true);
            size.HorizontalAlignment = HorizontalAlignment.Right;
            size.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(size, 1);
            sizes[t.Key] = size;
            g.Children.Add(cb); g.Children.Add(size);
            list.Children.Add(g);
        }
        var total = UiKit.Text("", 15, weight: FontWeights.SemiBold);
        var status = UiKit.Text("", 12, true);
        var analyze = new Button { Content = "Анализ" };
        var clean = new Button { Content = "Очистить выбранное", Style = S("AccentButton") };

        async Task Measure()
        {
            analyze.IsEnabled = false;
            long sum = 0;
            foreach (var t in targets.Where(t => !t.Special || t.Key == "recycle"))
            {
                sizes[t.Key].Text = "считаю…";
                try { await Task.Run(() => Cleaner.Measure(t, ct), ct); } catch (OperationCanceledException) { return; }
                sizes[t.Key].Text = Cleaner.Size(t.Size);
                if (sel.Contains(t.Key)) sum += t.Size;
            }
            total.Text = $"Можно освободить: {Cleaner.Size(sum)}";
            analyze.IsEnabled = true;
        }

        analyze.Click += async (_, _) => await Measure();
        clean.Click += async (_, _) =>
        {
            var chosen = targets.Where(t => sel.Contains(t.Key)).ToList();
            if (chosen.Count == 0) return;
            if (!UiKit.Confirm("Удалить выбранное?\n\n" + string.Join("\n", chosen.Select(c => "• " + c.Name)) + "\n\nФайлы, занятые программами, будут пропущены.")) return;
            clean.IsEnabled = analyze.IsEnabled = false;
            long freed = 0;
            foreach (var t in chosen)
            {
                status.Text = "Чищу: " + t.Name;
                try { freed += await Task.Run(() => Cleaner.Clean(t, ct, p => Dispatcher.BeginInvoke(() => status.Text = p)), ct); }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { Log.Error(ex); }
            }
            Cleaner.FlushDns();
            Settings.Current.LastClean = DateTime.Now; Settings.Save();
            status.Text = $"Готово! Освобождено {Cleaner.Size(freed)}";
            Osd.Show("\uE945", $"Освобождено {Cleaner.Size(freed)}");
            clean.IsEnabled = true;
            await Measure();
        };

        var btns = new WrapPanel { Margin = new Thickness(0, 12, 0, 0) };
        btns.Children.Add(analyze); btns.Children.Add(clean);
        var card = new StackPanel();
        card.Children.Add(list); card.Children.Add(total); card.Children.Add(btns); card.Children.Add(status);
        root.Children.Add(UiKit.Card(card));

        // Schedule
        var sched = new StackPanel();
        sched.Children.Add(new TextBlock { Text = "Автоочистка по расписанию", Style = S("H2") });
        var cbx = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left };
        var opts = new[] { (0, "Выключена"), (1, "Каждый день"), (3, "Раз в 3 дня"), (7, "Раз в неделю"), (30, "Раз в месяц") };
        foreach (var (_, n) in opts) cbx.Items.Add(n);
        cbx.SelectedIndex = Math.Max(0, Array.FindIndex(opts, o => o.Item1 == Settings.Current.CleanIntervalDays));
        cbx.SelectionChanged += (_, _) => { Settings.Current.CleanIntervalDays = opts[cbx.SelectedIndex].Item1; Settings.Save(); };
        sched.Children.Add(cbx);
        sched.Children.Add(UiKit.Text("Чистит отмеченные пункты (кроме DISM и драйверов) в фоне, пока приложение работает.", 12, true));
        root.Children.Add(UiKit.Card(sched));
        _ = Measure();
        return root;
    }

    // ---------------- Startup ----------------
    UIElement StartupTab()
    {
        var list = new StackPanel();
        foreach (var it in StartupApps.List())
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var info = new StackPanel();
            info.Children.Add(UiKit.Text(it.Name, 13, weight: FontWeights.SemiBold));
            info.Children.Add(new TextBlock { Text = $"{it.Source} · {it.Command}", FontSize = 11, Foreground = (Brush)FindResource("DimBrush"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = it.Command });
            g.Children.Add(info);
            if (it.FilePath != null && File.Exists(it.FilePath))
            {
                var open = new Button { Content = "\uE838", FontFamily = (FontFamily)FindResource("IconFont"), Style = S("GhostButton"), ToolTip = "Показать файл", VerticalAlignment = VerticalAlignment.Center };
                open.Click += (_, _) => Shell.Open("explorer.exe", $"/select,\"{it.FilePath}\"");
                Grid.SetColumn(open, 1); g.Children.Add(open);
            }
            var sw = new CheckBox { Style = S("Switch"), IsChecked = it.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            sw.Click += (_, _) => StartupApps.SetEnabled(it, sw.IsChecked == true);
            Grid.SetColumn(sw, 2); g.Children.Add(sw);
            list.Children.Add(g);
        }
        var sp = new StackPanel();
        sp.Children.Add(UiKit.Text("Меньше программ при старте — быстрее загрузка. Отключение не удаляет программу, его можно вернуть в любой момент.", 12, true));
        sp.Children.Add(new Border { Height = 10 });
        sp.Children.Add(list);
        return UiKit.Card(sp);
    }

    // ---------------- Large files ----------------
    UIElement LargeTab(CancellationToken ct)
    {
        var sp = new StackPanel();
        var folder = new TextBox { Text = KnownFolder("Downloads"), Width = 420, Margin = new Thickness(0, 0, 8, 8) };
        var minSize = new ComboBox { Width = 120, Margin = new Thickness(0, 0, 8, 8) };
        foreach (var s in new[] { "50 МБ", "100 МБ", "500 МБ", "1 ГБ" }) minSize.Items.Add(s);
        minSize.SelectedIndex = 1;
        var go = new Button { Content = "Искать", Style = S("AccentButton") };
        var results = new StackPanel();
        var status = UiKit.Text("", 12, true);

        var quick = new WrapPanel();
        foreach (var (n, p) in new[] { ("Загрузки", KnownFolder("Downloads")), ("Рабочий стол", Environment.GetFolderPath(Environment.SpecialFolder.Desktop)),
                     ("Документы", Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)), ("Видео", Environment.GetFolderPath(Environment.SpecialFolder.MyVideos)),
                     ("Весь профиль", Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)), ("Диск C:", @"C:\") })
        {
            var b = new Button { Content = n };
            b.Click += (_, _) => folder.Text = p;
            quick.Children.Add(b);
        }
        var pick = new Button { Content = "Обзор…" };
        pick.Click += (_, _) => { var d = new Microsoft.Win32.OpenFolderDialog(); if (d.ShowDialog() == true) folder.Text = d.FolderName; };

        go.Click += async (_, _) =>
        {
            long min = minSize.SelectedIndex switch { 0 => 50L << 20, 1 => 100L << 20, 2 => 500L << 20, _ => 1L << 30 };
            go.IsEnabled = false; status.Text = "Ищу…"; results.Children.Clear();
            try
            {
                var root = folder.Text;
                var files = await Task.Run(() => Cleaner.LargeFiles(root, min, ct), ct);
                status.Text = files.Count == 0 ? "Ничего не найдено" : $"Найдено {files.Count} файлов, всего {Cleaner.Size(files.Sum(f => f.Size))}";
                foreach (var f in files) results.Children.Add(FileRow(f, () => { }));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { status.Text = ex.Message; }
            go.IsEnabled = true;
        };

        var row = new WrapPanel();
        row.Children.Add(folder); row.Children.Add(pick); row.Children.Add(minSize); row.Children.Add(go);
        sp.Children.Add(quick); sp.Children.Add(row); sp.Children.Add(status);
        sp.Children.Add(new Border { Height = 8 });
        sp.Children.Add(results);
        return UiKit.Card(sp);
    }

    UIElement FileRow(Cleaner.FoundFile f, Action onDeleted)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var info = new StackPanel();
        info.Children.Add(new TextBlock { Text = Path.GetFileName(f.Path), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        info.Children.Add(new TextBlock { Text = $"{Path.GetDirectoryName(f.Path)} · {f.Modified:dd.MM.yyyy}", FontSize = 11, Foreground = (Brush)FindResource("DimBrush"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = f.Path });
        g.Children.Add(info);
        var size = UiKit.Text(Cleaner.Size(f.Size), 13); size.VerticalAlignment = VerticalAlignment.Center; size.HorizontalAlignment = HorizontalAlignment.Right; size.Margin = new Thickness(0, 0, 10, 0);
        Grid.SetColumn(size, 1); g.Children.Add(size);
        var btns = new StackPanel { Orientation = Orientation.Horizontal };
        var show = new Button { Content = "\uE838", FontFamily = (FontFamily)FindResource("IconFont"), Style = S("GhostButton"), ToolTip = "Показать в папке" };
        show.Click += (_, _) => Shell.Open("explorer.exe", $"/select,\"{f.Path}\"");
        var del = new Button { Content = "\uE74D", FontFamily = (FontFamily)FindResource("IconFont"), Style = S("GhostButton"), ToolTip = "В корзину", Foreground = (Brush)FindResource("BadBrush") };
        del.Click += (_, _) =>
        {
            try { Cleaner.ToRecycleBin(f.Path); g.Opacity = 0.3; g.IsEnabled = false; onDeleted(); }
            catch (Exception ex) { Osd.Show("\uE7BA", ex.Message); }
        };
        btns.Children.Add(show); btns.Children.Add(del);
        Grid.SetColumn(btns, 2); g.Children.Add(btns);
        return g;
    }

    static string KnownFolder(string name) => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), name);

    // ---------------- Duplicates ----------------
    UIElement DupsTab(CancellationToken ct)
    {
        var sp = new StackPanel();
        var folder = new TextBox { Text = KnownFolder("Downloads"), Width = 420, Margin = new Thickness(0, 0, 8, 8) };
        var pick = new Button { Content = "Обзор…" };
        pick.Click += (_, _) => { var d = new Microsoft.Win32.OpenFolderDialog(); if (d.ShowDialog() == true) folder.Text = d.FolderName; };
        var go = new Button { Content = "Искать дубликаты", Style = S("AccentButton") };
        var status = UiKit.Text("Ищет одинаковые файлы больше 1 МБ (сравнение по содержимому).", 12, true);
        var results = new StackPanel();
        go.Click += async (_, _) =>
        {
            go.IsEnabled = false; status.Text = "Сравниваю файлы…"; results.Children.Clear();
            try
            {
                var root = folder.Text;
                var groups = await Task.Run(() => Cleaner.Duplicates(root, ct), ct);
                status.Text = groups.Count == 0 ? "Дубликатов нет 👍" : $"Групп: {groups.Count}, лишнего: {Cleaner.Size(groups.Sum(g => g[0].Size * (g.Count - 1)))}";
                foreach (var grp in groups.Take(100))
                {
                    var box = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
                    box.Children.Add(UiKit.Text($"{grp.Count} копии по {Cleaner.Size(grp[0].Size)}", 12, true));
                    foreach (var f in grp) box.Children.Add(FileRow(f, () => { }));
                    results.Children.Add(box);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { status.Text = ex.Message; }
            go.IsEnabled = true;
        };
        var row = new WrapPanel();
        row.Children.Add(folder); row.Children.Add(pick); row.Children.Add(go);
        sp.Children.Add(row); sp.Children.Add(status); sp.Children.Add(results);
        return UiKit.Card(sp);
    }

    // ---------------- Installed programs ----------------
    UIElement AppsTab()
    {
        var sp = new StackPanel();
        var search = new TextBox { Width = 320, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
        var list = new StackPanel();
        var all = StartupApps.Installed();
        var sort = new ComboBox { Width = 160, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
        sort.Items.Add("По имени"); sort.Items.Add("По размеру"); sort.SelectedIndex = 0;

        void Render()
        {
            list.Children.Clear();
            var q = search.Text.Trim();
            var items = all.Where(a => q == "" || a.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || a.Publisher.Contains(q, StringComparison.OrdinalIgnoreCase));
            items = sort.SelectedIndex == 1 ? items.OrderByDescending(a => a.SizeKb) : items.OrderBy(a => a.Name);
            foreach (var a in items)
            {
                var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var info = new StackPanel();
                info.Children.Add(UiKit.Text(a.Name, 13, weight: FontWeights.SemiBold));
                info.Children.Add(UiKit.Text($"{a.Publisher} {a.Version}", 11, true));
                g.Children.Add(info);
                var size = UiKit.Text(a.SizeKb > 0 ? Cleaner.Size(a.SizeKb * 1024L) : "", 12, true); size.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(size, 1); g.Children.Add(size);
                var un = new Button { Content = "Удалить", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0) };
                un.Click += (_, _) => { if (UiKit.Confirm($"Запустить удаление «{a.Name}»?")) try { StartupApps.Uninstall(a); } catch (Exception ex) { Osd.Show("\uE7BA", ex.Message); } };
                Grid.SetColumn(un, 2); g.Children.Add(un);
                list.Children.Add(g);
            }
        }
        search.TextChanged += (_, _) => Render();
        sort.SelectionChanged += (_, _) => Render();
        Render();
        var top = new WrapPanel();
        top.Children.Add(new TextBlock { Text = "Поиск:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 10) });
        top.Children.Add(search);
        top.Children.Add(new Border { Width = 16 });
        top.Children.Add(sort);
        sp.Children.Add(top);
        sp.Children.Add(UiKit.Text($"Установлено программ: {all.Count}", 12, true));
        sp.Children.Add(list);
        return UiKit.Card(sp);
    }
}
