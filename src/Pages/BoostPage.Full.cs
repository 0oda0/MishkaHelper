using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

/// <summary>"Полная очистка": analyse everything → one checklist with safe defaults → do it → report "было/стало".</summary>
public partial class BoostPage
{
    UIElement FullTab(CancellationToken ct)
    {
        var root = new StackPanel();
        var intro = new StackPanel();
        intro.Children.Add(new TextBlock { Text = "Полная очистка", Style = S("H2") });
        intro.Children.Add(UiKit.Text("Для первого раза: проверю мусор, автозагрузку, программы в фоне и настройки, отмечу то, что безопасно убрать, " +
            "и всё сделаю одной кнопкой. Драйверы, звук, антивирус, облака и устройства не трогаю. Всё отключённое можно вернуть.", 12, true));
        var start = new Button { Content = "Начать анализ", Style = S("AccentButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        intro.Children.Add(start);
        root.Children.Add(UiKit.Card(intro));
        var body = new StackPanel();
        root.Children.Add(body);

        start.Click += async (_, _) =>
        {
            start.IsEnabled = false; start.Content = "Анализирую…";
            body.Children.Clear();
            try { await Analyze(body, ct); } catch (OperationCanceledException) { return; }
            start.Content = "Анализ заново"; start.IsEnabled = true;
        };
        return root;
    }

    static (Border Card, StackPanel Body) Step(int n, string title, string hint)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = $"{n}. {title}", Style = S("H2") });
        var h = UiKit.Text(hint, 12, true); h.Margin = new Thickness(0, 0, 0, 6); sp.Children.Add(h);
        return (UiKit.Card(sp), sp);
    }

    static CheckBox Row(Panel p, string name, string right, string? why, bool on, FullClean.Verdict? v = null)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var txt = new StackPanel();
        txt.Children.Add(UiKit.Text(name, 13, weight: FontWeights.SemiBold));
        if (why != null)
        {
            var w = UiKit.Text((v switch { FullClean.Verdict.Junk => "🧹 ", FullClean.Verdict.Keep => "🛡 ", _ => "❔ " }) + why, 11, true);
            txt.Children.Add(w);
        }
        var cb = new CheckBox { IsChecked = on, Content = txt, IsEnabled = v != FullClean.Verdict.Keep };
        var r = UiKit.Text(right, 12, true); r.VerticalAlignment = VerticalAlignment.Center; r.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(r, 1);
        g.Children.Add(cb); g.Children.Add(r);
        p.Children.Add(g);
        return cb;
    }

    async Task Analyze(StackPanel body, CancellationToken ct)
    {
        double ramBefore = FullClean.RamUsedPercent();

        // 1. Junk files — safe targets only (no DISM/drivers/shader cache).
        var (c1, b1) = Step(1, "Мусор", "Временные файлы, кэш браузеров (пароли и вкладки не трогаются), корзина, дампы, кэш обновлений.");
        var targets = Cleaner.Targets().Where(t => t.DefaultOn && (!t.Special || t.Key == "recycle")).ToList();
        var junk = new List<(CleanTarget T, CheckBox Cb)>();
        long junkSum = 0;
        foreach (var t in targets)
        {
            await Task.Run(() => Cleaner.Measure(t, ct), ct);
            junkSum += t.Size;
            junk.Add((t, Row(b1, t.Name, Cleaner.Size(t.Size), null, t.Size > 0)));
        }
        b1.Children.Insert(2, UiKit.Text($"Найдено: {Cleaner.Size(junkSum)}", 14, weight: FontWeights.SemiBold));
        body.Children.Add(c1);

        // 2. Autostart.
        var startup = FullClean.Startup();
        var (c2, b2) = Step(2, "Автозагрузка", $"Программ при старте Windows: {startup.Count}. Чем меньше — тем быстрее включается компьютер. Программы не удаляются, просто не стартуют сами.");
        var st = startup.Select(s => (s.Item, Cb: Row(b2, s.Item.Name, "", s.Why, s.Verdict == FullClean.Verdict.Junk, s.Verdict))).ToList();
        if (startup.Count == 0) b2.Children.Add(UiKit.Text("Пусто — отлично.", 13));
        body.Children.Add(c2);

        // 3. Background programs.
        var bg = await Task.Run(FullClean.Background, ct);
        var (c3, b3) = Step(3, "Программы в фоне", "Работают без окна и едят память. Закрытие — до следующего запуска; чтобы не возвращались, отключи их в шаге 2.");
        var bgs = bg.Select(x => (x, Cb: Row(b3, x.Name + (x.Count > 1 ? $" ×{x.Count}" : ""), Cleaner.Size(x.Bytes), x.Why, x.Verdict == FullClean.Verdict.Junk, x.Verdict))).ToList();
        if (bg.Count == 0) b3.Children.Add(UiKit.Text("Ничего лишнего не нашёл.", 13));
        body.Children.Add(c3);

        // 4. Settings & protection.
        var (c4, b4) = Step(4, "Настройки и защита", $"Память занята на {ramBefore:0}%.");
        var ram = Row(b4, "Освободить оперативную память", "", null, true);
        var dns = Row(b4, "Сбросить DNS-кэш (помогает, если сайты не открываются)", "", null, true);
        CheckBox? power = Power.OnAc && Power.Mode == "Saver" ? Row(b4, "Режим питания «Баланс» (сейчас «Экономия», а ноутбук на зарядке)", "", null, true) : null;
        CheckBox? game = !Tweaks.GameMode ? Row(b4, "Включить игровой режим Windows", "", null, true) : null;
        var scan = Row(b4, "Быстрая проверка на вирусы (Defender, 3–10 мин)", "", null, true);
        body.Children.Add(c4);

        // 5. Go.
        var (c5, b5) = Step(5, "Готово к очистке", "Проверь галочки выше и жми. Можно ничего не менять — отмечено только безопасное.");
        var go = new Button { Content = "Сделать отмеченное", Style = S("AccentButton"), HorizontalAlignment = HorizontalAlignment.Left };
        var status = UiKit.Text("", 12, true);
        b5.Children.Add(go); b5.Children.Add(status);
        body.Children.Add(c5);

        go.Click += async (_, _) =>
        {
            go.IsEnabled = false;
            long freed = 0; int disabled = 0, closed = 0; string virus = "не проверялось";
            var undo = new List<StartupItem>();
            Cleaner.LastSkipped = 0;
            try
            {
                foreach (var (t, cb) in junk.Where(j => j.Cb.IsChecked == true))
                {
                    status.Text = "Чищу: " + t.Name;
                    freed += await Task.Run(() => Cleaner.Clean(t, ct), ct);
                }
                foreach (var (it, cb) in st.Where(s => s.Cb.IsChecked == true))
                {
                    try { StartupApps.SetEnabled(it, false); undo.Add(it); disabled++; } catch (Exception e) { Log.Error(e); }
                }
                foreach (var (x, cb) in bgs.Where(b => b.Cb.IsChecked == true))
                {
                    status.Text = "Закрываю: " + x.Name;
                    closed += await Task.Run(() => FullClean.Close(x.Name), ct);
                }
                status.Text = "Настройки…";
                if (dns.IsChecked == true) await Task.Run(Cleaner.FlushDns, ct);
                if (power?.IsChecked == true) Power.Mode = "Balanced";
                if (game?.IsChecked == true) Tweaks.GameMode = true;
                if (ram.IsChecked == true) await Task.Run(Cleaner.TrimRam, ct);
                if (scan.IsChecked == true)
                {
                    status.Text = "Проверка на вирусы… (можно пользоваться компьютером)";
                    var (threats, _) = await Security.Scan(Security.ScanKind.Quick);
                    virus = threats ? "найдены угрозы — открой «Защиту»" : "угроз нет";
                }
            }
            catch (OperationCanceledException) { return; }
            catch (Exception e) { Log.Error(e); status.Text = "Ошибка: " + e.Message; }

            Settings.Current.LastClean = DateTime.Now; Settings.Save();
            await Task.Delay(1500);   // let memory numbers settle after closing programs
            double ramAfter = FullClean.RamUsedPercent();
            status.Text = "";
            var (c6, b6) = Step(6, "Отчёт", "Готово! Вот что изменилось:");
            foreach (var line in new[]
                     {
                         $"🧹 Освобождено места: {Cleaner.Size(freed)}" + (Cleaner.LastSkipped > 0 ? $" (занятых файлов пропущено: {Cleaner.LastSkipped})" : ""),
                         $"🚀 Убрано из автозагрузки: {disabled}",
                         $"🛑 Закрыто фоновых процессов: {closed}",
                         $"🧠 Память: было {ramBefore:0}% → стало {ramAfter:0}%",
                         $"🛡 Вирусы: {virus}",
                     })
                b6.Children.Add(UiKit.Text(line, 14));
            b6.Children.Add(UiKit.Text("Совет: перезагрузи компьютер — тогда автозагрузка применится полностью.", 12, true));
            if (undo.Count > 0)
            {
                var back = new Button { Content = "Вернуть автозагрузку как было", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
                back.Click += (_, _) => { foreach (var it in undo) StartupApps.SetEnabled(it, true); back.IsEnabled = false; back.Content = "Вернул"; };
                b6.Children.Add(back);
            }
            body.Children.Add(c6);
            c6.BringIntoView();
            Osd.Show("", $"Очистка: {Cleaner.Size(freed)}");
        };
    }
}
