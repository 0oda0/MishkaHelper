using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

public partial class ToolsPage : UserControl
{
    readonly DispatcherTimer _tick = new() { Interval = TimeSpan.FromSeconds(1) };

    public ToolsPage()
    {
        InitializeComponent();
        Cards.Children.Add(WindowsCard());
        Cards.Children.Add(PowerCard());
        Cards.Children.Add(NetworkCard());
        Cards.Children.Add(RepairCard());
        Cards.Children.Add(PrivacyCard());
        Cards.Children.Add(ShellCard());
        Cards.Children.Add(PasswordCard());
        Cards.Children.Add(HashCard());
        Cards.Children.Add(CrashCard());
        Cards.Children.Add(EmptyFoldersCard());
        _tick.Start();
        Unloaded += (_, _) => _tick.Stop();
    }

    // ---------- helpers ----------
    static Style S(string k) => (Style)Application.Current.Resources[k];

    static (Border Card, StackPanel Body) Card(string icon, string title, string? hint = null)
    {
        var sp = new StackPanel();
        var head = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        var ic = UiKit.Icon(icon, 18); ic.Foreground = (Brush)Application.Current.Resources["AccentBrush"]; ic.Margin = new Thickness(0, 0, 10, 0);
        head.Children.Add(ic);
        head.Children.Add(new TextBlock { Text = title, Style = S("H2"), Margin = new Thickness(0) });
        sp.Children.Add(head);
        if (hint != null) { var h = UiKit.Text(hint, 12, true); h.Margin = new Thickness(0, 0, 0, 8); sp.Children.Add(h); }
        return (UiKit.Card(sp), sp);
    }

    static Button Btn(Panel p, string text, Action click, bool accent = false)
    {
        var b = new Button { Content = text };
        if (accent) b.Style = S("AccentButton");
        b.Click += (_, _) => { try { click(); } catch (Exception e) { Osd.Show("", e.Message); } };
        p.Children.Add(b);
        return b;
    }

    static Button BtnAsync(Panel p, string text, Func<Button, Task> click)
    {
        var b = new Button { Content = text };
        b.Click += async (_, _) =>
        {
            var old = b.Content; b.IsEnabled = false;
            try { await click(b); } catch (Exception e) { Osd.Show("", e.Message); }
            b.IsEnabled = true; if (b.Content is string s && s.EndsWith("…")) b.Content = old;
        };
        p.Children.Add(b);
        return b;
    }

    static CheckBox Switch(Panel p, string text, bool value, Action<bool> set)
    {
        var cb = new CheckBox { Style = S("Switch"), Content = text, IsChecked = value };
        cb.Click += (_, _) => { try { set(cb.IsChecked == true); } catch (Exception e) { Osd.Show("", e.Message); } };
        p.Children.Add(cb);
        return cb;
    }

    static string Key(string action) => Settings.Current.Hotkeys.GetValueOrDefault(action, "") is { Length: > 0 } k ? Hotkeys.Pretty(k) : "не задано";

    // ---------- cards ----------
    UIElement WindowsCard()
    {
        var (c, b) = Card("", "Окна", "Работают с активным окном по горячим клавишам — даже поверх игр в оконном режиме.");
        foreach (var (a, t) in new[] { ("topmost", "Поверх всех окон"), ("opacity", "Прозрачность окна"), ("killFg", "Убить зависшее окно"), ("colorPick", "Пипетка (цвет под курсором → буфер)"), ("palette", "Палитра команд") })
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var k = UiKit.Text(Key(a), 12); k.FontFamily = new FontFamily("Consolas"); k.Foreground = (Brush)FindResource("AccentBrush"); DockPanel.SetDock(k, Dock.Right);
            row.Children.Add(k);
            row.Children.Add(UiKit.Text(t, 13));
            b.Children.Add(row);
        }
        var wp = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        Btn(wp, "Изменить сочетания", () => App.Current.Navigate("Hotkeys"));
        b.Children.Add(wp);
        return c;
    }

    UIElement PowerCard()
    {
        var (c, b) = Card("", "Питание");
        Switch(b, "Не засыпать (для загрузок, рендера, стримов)", Toolbox.KeepAwake, v => Toolbox.KeepAwake = v);
        var hib = Switch(b, $"Гибернация{(Toolbox.Hibernation ? $" — файл {Cleaner.Size(Toolbox.HiberSize)}" : "")}", Toolbox.Hibernation, async v => await Toolbox.SetHibernation(v));
        hib.ToolTip = "Выключение гибернации освобождает на диске C: столько, сколько у тебя RAM. Но пропадёт «Быстрый запуск».";

        b.Children.Add(UiKit.Text("Таймер", 12, true));
        var kind = new ComboBox { Width = 170, Margin = new Thickness(0, 4, 8, 8) };
        foreach (var k in new[] { "Выключение", "Перезагрузка", "Сон", "Гибернация" }) kind.Items.Add(k);
        kind.SelectedIndex = 0;
        var row = new WrapPanel();
        row.Children.Add(kind);
        var status = UiKit.Text("", 13);
        foreach (var m in new[] { 15, 30, 60, 120 })
            Btn(row, $"{m} мин", () =>
            {
                var k = new[] { "shutdown", "restart", "sleep", "hibernate" }[kind.SelectedIndex];
                Toolbox.SchedulePower(k, m);
            });
        Btn(row, "Отмена", () => Toolbox.SchedulePower("", 0));
        b.Children.Add(row);
        b.Children.Add(status);
        _tick.Tick += (_, _) => status.Text = Toolbox.TimerAt is { } at ? $"⏱ {Toolbox.PowerName(Toolbox.TimerKind)} через {at - DateTime.Now:hh\\:mm\\:ss}" : "";

        var wp = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        Btn(wp, "Выключить экран", () => Actions.Run("screenOff"));
        Btn(wp, "Заблокировать", Toolbox.Lock);
        BtnAsync(wp, "Отчёт о батарее", async _ => Shell.Open(await Toolbox.BatteryReport()));
        b.Children.Add(wp);
        return c;
    }

    UIElement NetworkCard()
    {
        var (c, b) = Card("", "Сеть");
        var res = UiKit.Text("", 13);
        var host = new TextBox { Text = Settings.Current.PingHost, Width = 160, Margin = new Thickness(0, 0, 8, 8) };
        var wp = new WrapPanel();
        wp.Children.Add(host);
        BtnAsync(wp, "Пинг ×5", async _ =>
        {
            Settings.Current.PingHost = host.Text.Trim(); Settings.Save();
            var list = new List<long>();
            for (int i = 0; i < 5; i++) { list.Add(await Toolbox.Ping(host.Text.Trim())); res.Text = string.Join("  ", list.Select(x => x < 0 ? "✕" : $"{x}")) + " мс"; }
            var ok = list.Where(x => x >= 0).ToList();
            res.Text = ok.Count == 0 ? "Нет ответа" : $"Средний {ok.Average():0} мс · мин {ok.Min()} · макс {ok.Max()} · потери {(5 - ok.Count) * 20}%";
        });
        BtnAsync(wp, "Тест скорости", async btn =>
        {
            btn.Content = "Качаю 25 МБ…";
            var mbps = await Toolbox.SpeedTestMbps(p => Dispatcher.BeginInvoke(() => btn.Content = $"Качаю… {p}"));
            res.Text = $"Скорость загрузки: {mbps:0.0} Мбит/с";
        });
        BtnAsync(wp, "Внешний IP", async _ => res.Text = "Внешний IP: " + await Toolbox.PublicIp());
        b.Children.Add(wp);
        b.Children.Add(res);

        var wifi = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        var wp2 = new WrapPanel();
        BtnAsync(wp2, "Пароли сохранённых Wi-Fi", async _ =>
        {
            wifi.Children.Clear();
            foreach (var w in await Task.Run(Toolbox.WifiPasswords))
            {
                var row = new DockPanel();
                var copy = new Button { Content = "", FontFamily = (FontFamily)FindResource("IconFont"), Style = S("GhostButton"), ToolTip = "Копировать пароль" };
                copy.Click += (_, _) => { Clipboard.SetText(w.Password); Osd.Show("", "Пароль скопирован"); };
                DockPanel.SetDock(copy, Dock.Right);
                row.Children.Add(copy);
                row.Children.Add(UiKit.Text($"{w.Name}:  {w.Password}", 13));
                wifi.Children.Add(row);
            }
            if (wifi.Children.Count == 0) wifi.Children.Add(UiKit.Text("Сохранённых сетей нет", 12, true));
        });
        BtnAsync(wp2, "Сбросить сеть", async _ =>
        {
            if (!UiKit.Confirm("Сбросить сетевые настройки Windows (Winsock, TCP/IP, DNS-кэш)?\nПомогает, когда интернет «есть, но не работает». Потребуется перезагрузка.")) return;
            await Toolbox.ResetNetwork();
            res.Text = "Готово. Перезагрузи компьютер.";
        });
        b.Children.Add(wp2);
        b.Children.Add(wifi);
        return c;
    }

    UIElement RepairCard()
    {
        var (c, b) = Card("", "Ремонт и обслуживание", "Команды открываются в отдельном окне, чтобы был виден ход работы.");
        var res = UiKit.Text("", 13);
        var wp = new WrapPanel();
        BtnAsync(wp, "Точка восстановления", async btn => { btn.Content = "Создаю…"; var r = await Toolbox.CreateRestorePoint(); res.Text = r == "OK" ? "✅ Точка восстановления создана" : "⚠ " + r; });
        Btn(wp, "Проверить системные файлы (SFC)", () => Toolbox.RunVisible("sfc /scannow"));
        Btn(wp, "Восстановить образ (DISM)", () => Toolbox.RunVisible("DISM /Online /Cleanup-Image /RestoreHealth"));
        Btn(wp, "Оптимизировать диски", () => Toolbox.RunVisible("defrag /C /O /U /V"));
        Btn(wp, "Перезапустить Проводник", Toolbox.RestartExplorer);
        Btn(wp, "Сведения о ПК → буфер", () => Actions.Run("sysinfo"));
        BtnAsync(wp, "Проверить обновления Mishka", _ => Updater.CheckInBackground(silent: false));
        b.Children.Add(wp);
        b.Children.Add(res);
        var boot = Toolbox.BootTimes();
        if (boot.Count > 0)
            b.Children.Add(UiKit.Text($"Последняя загрузка Windows: {boot[0].Seconds:0} с (средняя за {boot.Count}: {boot.Average(x => x.Seconds):0} с)", 12, true));
        return c;
    }

    UIElement PrivacyCard()
    {
        var (c, b) = Card("", "Камера и микрофон", "Кто сейчас или недавно использовал камеру и микрофон.");
        var list = new StackPanel();
        void Load()
        {
            list.Children.Clear();
            foreach (var u in Toolbox.SensorUsage().Take(10))
            {
                var t = UiKit.Text($"{(u.Active ? "🔴 СЕЙЧАС" : "  ")} {u.Device}: {u.App}" + (u.LastUsed is { } d && !u.Active ? $" · {d:dd.MM HH:mm}" : ""), 13);
                if (u.Active) t.Foreground = (Brush)FindResource("BadBrush");
                list.Children.Add(t);
            }
            if (list.Children.Count == 0) list.Children.Add(UiKit.Text("Нет данных", 12, true));
        }
        Load();
        var wp = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
        Btn(wp, "Обновить", Load);
        Btn(wp, "Разрешения Windows…", () => Shell.Open("ms-settings:privacy-webcam"));
        b.Children.Add(list);
        b.Children.Add(wp);
        return c;
    }

    UIElement ShellCard()
    {
        var (c, b) = Card("", "Windows под себя");
        var s = Settings.Current;
        Switch(b, "Автоскрытие панели задач", Toolbox.TaskbarAutoHide, v => Toolbox.TaskbarAutoHide = v);
        Switch(b, "Подсказка при Caps Lock / Num Lock", s.LockKeysOsd, v => { s.LockKeysOsd = v; Settings.Save(); });
        Switch(b, "Проверять обновления Mishka раз в день", s.CheckUpdates, v => { s.CheckUpdates = v; Settings.Save(); });
        var wp = new WrapPanel();
        Btn(wp, "Скрыть/показать значки рабочего стола", Toolbox.ToggleDesktopIcons);
        b.Children.Add(wp);

        var auto = Switch(b, "Авто-тема: светлая днём, тёмная вечером", s.AutoTheme, v => { s.AutoTheme = v; Settings.Save(); });
        var times = new WrapPanel { Margin = new Thickness(0, 2, 0, 6) };
        var light = new TextBox { Text = s.LightAt, Width = 70, Margin = new Thickness(0, 0, 8, 0) };
        var dark = new TextBox { Text = s.DarkAt, Width = 70, Margin = new Thickness(0, 0, 8, 0) };
        light.LostFocus += (_, _) => { if (TimeSpan.TryParse(light.Text, out _)) { s.LightAt = light.Text; Settings.Save(); } };
        dark.LostFocus += (_, _) => { if (TimeSpan.TryParse(dark.Text, out _)) { s.DarkAt = dark.Text; Settings.Save(); } };
        times.Children.Add(UiKit.Text("светлая с ", 12, true)); times.Children.Add(light);
        times.Children.Add(UiKit.Text("тёмная с ", 12, true)); times.Children.Add(dark);
        b.Children.Add(times);

        var capLabel = UiKit.Text("", 13);
        var cap = new Slider { Minimum = 0, Maximum = 100, Value = s.VolumeCap, IsSnapToTickEnabled = true, TickFrequency = 5 };
        void Lbl() => capLabel.Text = s.VolumeCap == 0 ? "Лимит громкости: выкл (защита ушей в наушниках)" : $"Лимит громкости: не выше {s.VolumeCap}%";
        cap.ValueChanged += (_, e) => { s.VolumeCap = (int)e.NewValue; Lbl(); };
        cap.LostMouseCapture += (_, _) => Settings.Save();
        Lbl();
        b.Children.Add(capLabel);
        b.Children.Add(cap);
        return c;
    }

    UIElement PasswordCard()
    {
        var (c, b) = Card("", "Генератор паролей", "Криптостойкий генератор, без похожих символов (0/O, 1/l).");
        var len = new Slider { Minimum = 8, Maximum = 48, Value = 16, IsSnapToTickEnabled = true, TickFrequency = 1 };
        var lenTxt = UiKit.Text("Длина: 16", 12, true);
        len.ValueChanged += (_, e) => lenTxt.Text = $"Длина: {e.NewValue:0}";
        var sym = new CheckBox { Content = "Спецсимволы", IsChecked = true };
        var output = new TextBox { FontFamily = new FontFamily("Consolas"), FontSize = 15, IsReadOnly = true, Margin = new Thickness(0, 8, 0, 8) };
        var wp = new WrapPanel();
        Btn(wp, "Сгенерировать и скопировать", () =>
        {
            output.Text = Toolbox.Password((int)len.Value, sym.IsChecked == true);
            Clipboard.SetText(output.Text);
            Osd.Show("", "Пароль скопирован");
        }, accent: true);
        b.Children.Add(lenTxt); b.Children.Add(len); b.Children.Add(sym); b.Children.Add(output); b.Children.Add(wp);
        return c;
    }

    UIElement HashCard()
    {
        var (c, b) = Card("", "Контрольная сумма файла", "Сравни с суммой на сайте, чтобы убедиться, что скачанный файл не подменён.");
        var res = new TextBox { IsReadOnly = true, FontFamily = new FontFamily("Consolas"), FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6) };
        var cmp = new TextBox { Margin = new Thickness(0, 0, 0, 6) };
        var verdict = UiKit.Text("", 13);
        (string, string, string) h = default;
        void Compare()
        {
            var x = cmp.Text.Trim().Replace(" ", "");
            if (x.Length == 0 || h == default) { verdict.Text = ""; return; }
            bool ok = new[] { h.Item1, h.Item2, h.Item3 }.Any(v => string.Equals(v, x, StringComparison.OrdinalIgnoreCase));
            verdict.Text = ok ? "✅ Совпадает" : "❌ Не совпадает";
            verdict.Foreground = (Brush)FindResource(ok ? "GoodBrush" : "BadBrush");
        }
        cmp.TextChanged += (_, _) => Compare();
        var wp = new WrapPanel();
        BtnAsync(wp, "Выбрать файл…", async _ =>
        {
            var d = new Microsoft.Win32.OpenFileDialog();
            if (d.ShowDialog() != true) return;
            res.Text = "Считаю…";
            h = await Toolbox.Hashes(d.FileName);
            res.Text = $"MD5     {h.Item1}\nSHA-1   {h.Item2}\nSHA-256 {h.Item3}";
            Compare();
        });
        b.Children.Add(wp); b.Children.Add(res);
        b.Children.Add(UiKit.Text("Вставь сумму для сравнения:", 12, true));
        b.Children.Add(cmp); b.Children.Add(verdict);
        return c;
    }

    UIElement CrashCard()
    {
        var (c, b) = Card("", "История сбоев (30 дней)", "Синие экраны, внезапные выключения и вылеты программ из журнала Windows.");
        var list = new StackPanel();
        var wp = new WrapPanel();
        BtnAsync(wp, "Показать", async _ =>
        {
            list.Children.Clear();
            var items = await Task.Run(() => Toolbox.Crashes());
            foreach (var g in items.GroupBy(i => i.Kind))
                list.Children.Add(UiKit.Text($"{g.Key}: {g.Count()}", 13, weight: FontWeights.SemiBold));
            foreach (var i in items.Take(15))
                list.Children.Add(new TextBlock { Text = $"{i.When:dd.MM HH:mm}  {i.Kind} — {i.Details}", FontSize = 11, Foreground = (Brush)FindResource("DimBrush"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = i.Details });
            if (items.Count == 0) list.Children.Add(UiKit.Text("Сбоев не было 👍", 13));
        });
        b.Children.Add(wp); b.Children.Add(list);
        return c;
    }

    UIElement EmptyFoldersCard()
    {
        var (c, b) = Card("", "Пустые папки", "Находит пустые папки (часто остаются после удаления программ).");
        // Not the whole profile: some apps expect their empty AppData folders to exist.
        var folder = new TextBox { Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads"), Margin = new Thickness(0, 0, 0, 8) };
        var res = UiKit.Text("", 13);
        List<string> found = new();
        var wp = new WrapPanel();
        Btn(wp, "Обзор…", () => { var d = new Microsoft.Win32.OpenFolderDialog(); if (d.ShowDialog() == true) folder.Text = d.FolderName; });
        BtnAsync(wp, "Найти", async _ =>
        {
            res.Text = "Ищу…";
            var root = folder.Text;
            found = await Task.Run(() => Toolbox.EmptyFolders(root, CancellationToken.None));
            res.Text = found.Count == 0 ? "Пустых папок нет" : $"Найдено: {found.Count}\n" + string.Join("\n", found.Take(8)) + (found.Count > 8 ? "\n…" : "");
        });
        Btn(wp, "Удалить найденные", () =>
        {
            if (found.Count == 0 || !UiKit.Confirm($"Удалить {found.Count} пустых папок?")) return;
            int n = 0;
            foreach (var f in found.OrderByDescending(x => x.Length)) { try { if (!Directory.EnumerateFileSystemEntries(f).Any()) { Directory.Delete(f); n++; } } catch { } }
            res.Text = $"Удалено папок: {n}";
            found.Clear();
        });
        b.Children.Add(folder); b.Children.Add(wp); b.Children.Add(res);
        return c;
    }
}
