using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

public partial class BoostPage
{
    // ---------------- Обслуживание ----------------
    UIElement CareTab()
    {
        var root = new StackPanel();

        // Windows and drivers
        var upd = new StackPanel();
        upd.Children.Add(UiKit.Text("Обновления Windows и драйверы", 15, weight: FontWeights.SemiBold));
        var updList = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        var updBtn = new Button { Content = "Проверить", HorizontalAlignment = HorizontalAlignment.Left, Style = S("AccentButton") };
        var openWu = new Button { Content = "Открыть Центр обновлений" };
        openWu.Click += (_, _) => Shell.Open("ms-settings:windowsupdate");
        updBtn.Click += async (_, _) =>
        {
            updBtn.IsEnabled = false; updList.Children.Clear(); updList.Children.Add(UiKit.Text("Спрашиваю Центр обновлений… это до минуты.", 12, true));
            try
            {
                var l = await Care.WindowsUpdates();
                updList.Children.Clear();
                if (l.Count == 0) updList.Children.Add(UiKit.Text("Всё установлено: обновлений и новых драйверов нет.", 13));
                foreach (var u in l.OrderByDescending(u => u.Driver))
                    updList.Children.Add(UiKit.Text((u.Driver ? "Драйвер: " : "") + u.Title, 12));
                if (l.Count > 0) updList.Children.Add(UiKit.Text("Поставить их можно в Центре обновлений: там видно ход установки и перезагрузку.", 11, true));
            }
            catch (Exception ex) { updList.Children.Clear(); updList.Children.Add(UiKit.Text("Не получилось: " + ex.Message, 12, true)); }
            updBtn.IsEnabled = true;
        };
        upd.Children.Add(updList);
        upd.Children.Add(new WrapPanel { Children = { updBtn, openWu } });
        root.Children.Add(UiKit.Card(upd));

        // Programs
        var apps = new StackPanel();
        apps.Children.Add(UiKit.Text("Обновления программ", 15, weight: FontWeights.SemiBold));
        apps.Children.Add(UiKit.Text("Через winget — встроенный в Windows установщик программ.", 11, true));
        var appList = new StackPanel { Margin = new Thickness(0, 6, 0, 6) };
        var appBtn = new Button { Content = "Проверить", HorizontalAlignment = HorizontalAlignment.Left, Style = S("AccentButton") };
        var allBtn = new Button { Content = "Обновить все", Visibility = Visibility.Collapsed };
        allBtn.Click += (_, _) => { try { Care.UpgradeAll(); } catch (Exception ex) { Osd.Show("", ex.Message); } };
        appBtn.Click += async (_, _) =>
        {
            appBtn.IsEnabled = false; appList.Children.Clear(); appList.Children.Add(UiKit.Text("Смотрю…", 12, true));
            try
            {
                var l = await Care.Upgrades();
                appList.Children.Clear();
                if (l.Count == 0) appList.Children.Add(UiKit.Text("Все программы свежие.", 13));
                foreach (var u in l)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
                    var one = new Button { Content = "Обновить", Margin = new Thickness(0) };
                    var id = u.Id;
                    one.Click += (_, _) => { try { Care.RunVisible(Care.IsSafeId(id) ? new[] { $"upgrade --id {id} -e --silent --accept-package-agreements --accept-source-agreements" } : Array.Empty<string>()); } catch (Exception ex) { Osd.Show("", ex.Message); } };
                    DockPanel.SetDock(one, Dock.Right); row.Children.Add(one);
                    row.Children.Add(UiKit.Text($"{u.Name}   {u.Version} → {u.Available}", 12));
                    appList.Children.Add(row);
                }
                allBtn.Visibility = l.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex) { appList.Children.Clear(); appList.Children.Add(UiKit.Text("Не получилось: " + ex.Message, 12, true)); }
            appBtn.IsEnabled = true;
        };
        apps.Children.Add(appList);
        apps.Children.Add(new WrapPanel { Children = { appBtn, allBtn } });
        root.Children.Add(UiKit.Card(apps));

        // Dust
        var dust = new StackPanel();
        dust.Children.Add(UiKit.Text("Пыль и перегрев", 15, weight: FontWeights.SemiBold));
        var data = Care.Load();
        dust.Children.Add(UiKit.Text(Care.Dust(data.LoadTemp, DateTime.Today) is { } t
            ? $"Под нагрузкой процессор сейчас в среднем {t.Now:0}°, в первый месяц наблюдений было {t.Then:0}°. " +
              (t.Now - t.Then >= 10 ? "Заметно горячее — похоже, пора чистить от пыли и менять термопасту." : "Разница небольшая, чистка пока не нужна.")
            : $"Собираю температуру под нагрузкой, дней с данными: {data.LoadTemp.Count}. Сравнение появится через два месяца: если ноутбук начнёт греться заметно сильнее, Mishka напомнит про чистку.", 12));
        root.Children.Add(UiKit.Card(dust));

        // Battery
        var bat = new StackPanel();
        bat.Children.Add(UiKit.Text("Здоровье батареи", 15, weight: FontWeights.SemiBold));
        var batText = UiKit.Text("Читаю отчёт Windows…", 12, true);
        bat.Children.Add(batText);
        var bars = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        bat.Children.Add(bars);
        root.Children.Add(UiKit.Card(bat));
        _ = Task.Run(async () =>
        {
            (int Now, List<Care.BatteryMonth> Months)? r = null;
            try { r = await Care.Battery(); } catch (Exception e) { Log.Error(e); }
            await Dispatcher.InvokeAsync(() =>
            {
                if (r is not { } b) { batText.Text = "Батареи нет или Windows не отдаёт о ней данные."; return; }
                batText.Text = $"Сейчас батарея держит {b.Now}% от ёмкости новой." + (b.Now < 70 ? " Это уже заметный износ: время работы сократилось примерно на треть." : "");
                foreach (var m in b.Months.TakeLast(12))
                {
                    var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                    var label = UiKit.Text(m.Month.ToString("MMM yyyy"), 12, true); label.Width = 90;
                    var bar = new Border { Height = 10, Width = Math.Max(4, m.Percent * 3), Background = (Brush)FindResource("AccentBrush"), CornerRadius = new CornerRadius(3), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
                    var pct = UiKit.Text($"  {m.Percent}%", 12);
                    row.Children.Add(label); row.Children.Add(bar); row.Children.Add(pct);
                    bars.Children.Add(row);
                }
            });
        });
        return root;
    }

    // ---------------- Новый ПК: программы одной кнопкой ----------------
    UIElement KitTab()
    {
        var root = new StackPanel();
        var sel = Settings.Current.AppKit;
        root.Children.Add(UiKit.Text("Отметь программы и поставь их разом: Mishka скачает официальные установщики через winget и поставит без вопросов. Подходит для только что установленной Windows.", 12, true));
        var boxes = new List<(CheckBox Box, string Id)>();
        var grid = new WrapPanel { Margin = new Thickness(0, 8, 0, 8) };
        foreach (var g in Care.Kit.GroupBy(k => k.Group))
        {
            var col = new StackPanel { Width = 230, Margin = new Thickness(0, 0, 12, 10) };
            col.Children.Add(UiKit.Text(g.Key, 13, weight: FontWeights.SemiBold));
            foreach (var (name, id, _) in g)
            {
                var cb = new CheckBox { Content = name, IsChecked = sel.Contains(id), Margin = new Thickness(0, 3, 0, 0) };
                cb.Click += (_, _) => { if (cb.IsChecked == true) { if (!sel.Contains(id)) sel.Add(id); } else sel.Remove(id); Settings.Save(); };
                boxes.Add((cb, id)); col.Children.Add(cb);
            }
            grid.Children.Add(col);
        }
        root.Children.Add(grid);
        var extra = UiKit.Text("", 12, true);
        void ShowExtra()
        {
            var own = sel.Where(id => !Care.Kit.Any(k => k.Id.Equals(id, StringComparison.OrdinalIgnoreCase))).ToList();
            extra.Text = own.Count > 0 ? "Ещё из набора: " + string.Join(", ", own) : "";
        }
        ShowExtra();
        root.Children.Add(extra);

        var install = new Button { Content = "Установить отмеченные", Style = S("AccentButton") };
        install.Click += (_, _) =>
        {
            if (sel.Count == 0) { Osd.Show("", "Ничего не отмечено"); return; }
            if (!UiKit.Confirm($"Поставить программы ({sel.Count})? Откроется окно, где видно ход установки.")) return;
            try { Care.Install(sel); } catch (Exception ex) { Osd.Show("", ex.Message); }
        };
        var copy = new Button { Content = "Скопировать набор для друга" };
        copy.Click += (_, _) => { Clipboard.SetText(string.Join(", ", sel)); Osd.Show("", "Набор скопирован — отправь его другу"); };
        var paste = new Button { Content = "Вставить набор друга" };
        paste.Click += (_, _) =>
        {
            var got = Care.ParseSet(Clipboard.ContainsText() ? Clipboard.GetText() : "");
            if (got.Count == 0) { Osd.Show("", "В буфере обмена нет набора"); return; }
            foreach (var id in got) if (!sel.Contains(id, StringComparer.OrdinalIgnoreCase)) sel.Add(id);
            Settings.Save();
            foreach (var (box, id) in boxes) box.IsChecked = sel.Contains(id, StringComparer.OrdinalIgnoreCase);
            ShowExtra();
            Osd.Show("", $"Добавлено из набора: {got.Count}");
        };
        root.Children.Add(new WrapPanel { Children = { install, copy, paste } });
        return root;
    }
}
