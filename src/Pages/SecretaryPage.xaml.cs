using System.Windows;
using System.Windows.Controls;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

/// <summary>Master switch, today's list, search, every feature's switch, and what the secretary has collected.</summary>
public partial class SecretaryPage : UserControl
{
    static SecretarySettings S => Settings.Current.Secretary;
    static Style St(string k) => (Style)Application.Current.Resources[k];

    /// <summary>Feature switches. Also shown by the first-run setup.</summary>
    public static readonly (string Name, string Hint, Func<bool> Get, Action<bool> Set)[] Features =
    {
        ("Заметки к программам и сайтам", "Свой текст для каждой программы и каждого сайта, по горячей клавише.", () => S.AppNotes, v => S.AppNotes = v),
        ("Показывать заметку при открытии", "Один раз за сеанс, когда программа выходит на передний план.", () => S.AppHints, v => S.AppHints = v),
        ("Быстрая заметка", "Текст с датой сам становится напоминанием.", () => S.Capture, v => S.Capture = v),
        ("Заметка голосом", "Без Джарвиса. Распознавание на этом ПК, модель 44 МБ скачается один раз.", () => S.Voice, v => S.Voice = v),
        ("«Запомни» со страницы", "Запоминает адрес открытой страницы: расписание, журнал, что угодно.", () => S.Remember, v => S.Remember = v),
        ("Утренняя сводка", "Дела, календарь, письма, расписание при первом включении за день.", () => S.Morning, v => S.Morning = v),
        ("Сводка на завтра", "Вечером: дела на завтра и ссылка на расписание.", () => S.Evening, v => S.Evening = v),
        ("Сроки из писем и буфера", "Видит дату рядом со словами «сдать», «встреча», «оплата» и предлагает напомнить.", () => S.Deadlines, v => S.Deadlines = v),
        ("Проверка почты", "Только чтение. Письма остаются непрочитанными.", () => S.Mail, v => S.Mail = v),
        ("Где я остановился", "Запоминает открытые программы, папки и страницы, возвращает одной кнопкой.", () => S.Workspace, v => S.Workspace = v),
        ("Разбор «Загрузок»", "Подсказывает, куда разложить файлы. Без твоей кнопки ничего не двигает.", () => S.Downloads, v => S.Downloads = v),
        ("Встречи из календаря", "За 10 минут до начала; ссылка на встречу открывается по нажатию.", () => S.Meetings, v => S.Meetings = v),
        ("Напоминать о перерыве", "После долгой работы без пауз.", () => S.Breaks, v => S.Breaks = v),
        ("Дублировать напоминания на телефон", "Через приложение Mishka.", () => S.ToPhone, v => S.ToPhone = v),
        ("Дела с галочками", "«Надо сдать отчёт» попадает в список. Просроченное остаётся в утренней сводке.", () => S.Tasks, v => S.Tasks = v),
        ("Повторы", "«Каждый вторник в 18 тренировка», «каждое 5-е оплатить интернет».", () => S.Repeats, v => S.Repeats = v),
        ("Дни рождения и даты", "«День рождения мамы 12 мая» — каждый год и за 3 дня до него; такие же события из календаря.", () => S.Dates, v => S.Dates = v),
        ("Напоминание при открытии", "«Когда открою Word, проверить титульник».", () => S.AppTriggers, v => S.AppTriggers = v),
        ("Расписание в сводке", "Читает запомненную страницу и показывает пары на день, если там есть понятный заголовок дня.", () => S.ScheduleRead, v => S.ScheduleRead = v),
        ("Итог дня", "Вечером: где провёл время, что сделано и что осталось.", () => S.DayLog, v => S.DayLog = v),
        ("Заметка из выделенного", "Выдели текст и нажми клавишу: сохранится вместе с адресом страницы.", () => S.Selection, v => S.Selection = v),
        ("Правила для «Загрузок»", "Твои правила: файлы со словом «лаба» сами уходят в выбранную папку.", () => S.DownloadRules, v => S.DownloadRules = v),
        ("Поиск файлов", "«Куда я сохранил договор?»: ищет в «Загрузках», «Документах» и на рабочем столе, показывает, откуда файл скачан.", () => S.FileMemory, v => S.FileMemory = v),
        ("Проверка перед выключением", "Выключаешь ПК с телефона — покажет несделанное на сегодня.", () => S.ShutdownCheck, v => S.ShutdownCheck = v),
        ("Вкладка на телефоне", "Сводка, дела и заметки в приложении Mishka.", () => S.PhoneTab, v => S.PhoneTab = v),
        ("Не мешать в игре", "В игре и на весь экран всё ждёт в списке «Пока ты был занят».", () => S.QuietInGame, v => S.QuietInGame = v),
    };

    public SecretaryPage()
    {
        InitializeComponent();
        Render();
        Secretary.Changed += OnChanged;
        SecMail.Changed += OnChanged;
        Unloaded += (_, _) => { Secretary.Changed -= OnChanged; SecMail.Changed -= OnChanged; };
    }

    void OnChanged() => Dispatcher.BeginInvoke(Render);

    static (Border Card, StackPanel Body) Card(string title, string? hint = null, double width = 392)
    {
        var sp = new StackPanel { Width = width };
        sp.Children.Add(new TextBlock { Text = title, Style = St("H2") });
        if (hint != null) { var h = UiKit.Text(hint, 12, true); h.Margin = new Thickness(0, 0, 0, 8); h.TextWrapping = TextWrapping.Wrap; sp.Children.Add(h); }
        return (UiKit.Card(sp), sp);
    }

    static Button Btn(Panel p, string text, Action click, bool accent = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 4, 8, 4) };
        if (accent) b.Style = St("AccentButton");
        b.Click += (_, _) => { try { click(); } catch (Exception e) { Osd.Show("", e.Message); } };
        p.Children.Add(b);
        return b;
    }

    static DockPanel Row(string text, params (string Label, Action Click)[] buttons)
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        foreach (var (label, click) in buttons)
        {
            var b = new Button { Content = label, Style = St("GhostButton"), Padding = new Thickness(8, 2, 8, 2) };
            b.Click += (_, _) => { try { click(); } catch (Exception e) { Osd.Show("", e.Message); } };
            DockPanel.SetDock(b, Dock.Right);
            row.Children.Add(b);
        }
        var t = UiKit.Text(text, 13); t.TextWrapping = TextWrapping.Wrap; t.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(t);
        return row;
    }

    void Render()
    {
        Cards.Children.Clear();
        Cards.Children.Add(MasterCard());
        if (!S.Enabled) { Cards.Children.Add(FeaturesCard()); return; }
        Cards.Children.Add(TodayCard());
        if (S.Tasks) Cards.Children.Add(TasksCard());
        Cards.Children.Add(SearchCard());
        if (SecStore.Cur.Suggestions.Count > 0) Cards.Children.Add(WaitingCard());
        Cards.Children.Add(RememberedCard());
        if (S.AppNotes) Cards.Children.Add(NotesCard());
        if (S.Mail) Cards.Children.Add(MailCard());
        if (S.DayLog && Secretary.DayLog() is { Count: > 0 } log) Cards.Children.Add(DayLogCard(log));
        if (S.Repeats || S.AppTriggers) Cards.Children.Add(RepeatsCard());
        if (S.Workspace) Cards.Children.Add(WorkspaceCard());
        if (S.Downloads) Cards.Children.Add(DownloadsCard());
        Cards.Children.Add(FeaturesCard());
    }

    UIElement MasterCard()
    {
        var (c, b) = Card("Режим секретарши");
        var sw = new CheckBox { Style = St("Switch"), Content = S.Enabled ? "Включена" : "Выключена", IsChecked = S.Enabled };
        sw.Click += (_, _) => { S.Enabled = sw.IsChecked == true; Settings.Save(); Secretary.Apply(); Render(); };
        b.Children.Add(sw);
        var hk = Settings.Current.Hotkeys;
        var keys = UiKit.Text($"Заметка к программе — {Hotkeys.Pretty(hk.GetValueOrDefault("secNote", ""))}\nБыстрая заметка — {Hotkeys.Pretty(hk.GetValueOrDefault("secQuick", ""))}\nГолосом — {Hotkeys.Pretty(hk.GetValueOrDefault("secVoice", ""))}\nЗапомнить страницу — {Hotkeys.Pretty(hk.GetValueOrDefault("secRemember", ""))}", 12, true);
        keys.Margin = new Thickness(0, 8, 0, 4);
        b.Children.Add(keys);
        Btn(b, "Поменять клавиши", () => App.Current.Navigate("Hotkeys"));
        return c;
    }

    UIElement TodayCard()
    {
        var (c, b) = Card("Сегодня");
        Fill();
        async void Fill()
        {
            var lines = await Secretary.Today();
            if (lines.Count == 0) b.Children.Add(UiKit.Text("Дел на сегодня нет.", 13, true));
            foreach (var l in lines)
                b.Children.Add(l.Click != null ? Row(l.Text, ("Открыть", l.Click)) : Row(l.Text));
            if (SecSchedule.NeedsLogin) b.Children.Add(Row("Чтобы показывать расписание, войди на сайт один раз в окне Mishka.", ("Войти", () => WebReader.OpenWindow(SecSchedule.LoginUrl, "Вход на сайт расписания"))));
        }
        return c;
    }

    UIElement TasksCard()
    {
        var (c, b) = Card("Дела", "Пиши как есть: «надо сдать отчёт», «завтра купить билеты». Срок необязателен.");
        foreach (var t in Secretary.OpenTasks().Take(20))
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var del = new Button { Content = "✕", Style = St("GhostButton"), Padding = new Thickness(8, 2, 8, 2) };
            del.Click += (_, _) => { SecStore.Cur.Tasks.Remove(t); SecStore.Save(); Render(); };
            DockPanel.SetDock(del, Dock.Right); row.Children.Add(del);
            var late = t.Due < DateTime.Today;
            var cb = new CheckBox { VerticalAlignment = VerticalAlignment.Center, Content = new TextBlock { Text = t.Text + (t.Due is { } d ? $"  ·  {When.Say(d, t.HasTime, DateTime.Now)}" : ""), TextWrapping = TextWrapping.Wrap, Foreground = (System.Windows.Media.Brush)FindResource(late ? "WarnBrush" : "TextBrush") } };
            cb.Click += (_, _) => { Secretary.Finish(t); };
            row.Children.Add(cb);
            b.Children.Add(row);
        }
        var box = new TextBox { Padding = new Thickness(8), Margin = new Thickness(0, 6, 0, 0) };
        box.KeyDown += (_, e) =>
        {
            if (e.Key != System.Windows.Input.Key.Enter || box.Text.Trim().Length == 0) return;
            var text = box.Text.Trim();
            var p = SecPlan.Understand(text, DateTime.Now);
            SecStore.Cur.Tasks.Add(new SecTask { Text = p.Kind == "remind" ? p.Text : Reminders.Cap(text), Due = p.Kind == "remind" ? p.At : null, HasTime = p.HasTime });
            SecStore.Save();
            Render();
        };
        b.Children.Add(UiKit.WithHint(box, "Новое дело, Enter — добавить"));
        return c;
    }

    UIElement DayLogCard(List<string> log)
    {
        var (c, b) = Card("Итог дня");
        foreach (var l in log) b.Children.Add(Row(l));
        return c;
    }

    UIElement RepeatsCard()
    {
        var (c, b) = Card("Повторы и условия", "Добавляются быстрой заметкой: «каждый вторник в 18 тренировка», «день рождения мамы 12 мая», «когда открою Word, проверить титульник».");
        foreach (var r in Settings.Current.Reminders.Where(r => r.Repeat.Length > 0).ToList())
            b.Children.Add(Row($"{r.Text} · {SecPlan.RepeatName(r.Repeat)} · дальше {When.Say(r.At, r.HasTime, DateTime.Now)}", ("✕", () => { Reminders.Remove(r); Render(); })));
        foreach (var t in SecStore.Cur.Triggers.ToList())
            b.Children.Add(Row($"Когда откроешь {t.Key}: {t.Text}", ("✕", () => { SecStore.Cur.Triggers.Remove(t); SecStore.Save(); Render(); })));
        return c;
    }

    UIElement SearchCard()
    {
        var (c, b) = Card("Найти", "«расписание», «пароль от вайфая», название сайта: поиск по заметкам и запомненному.");
        var box = new TextBox { Padding = new Thickness(8) };
        var results = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
        box.TextChanged += (_, _) =>
        {
            results.Children.Clear();
            if (box.Text.Trim().Length < 2) return;
            var found = Secretary.Search(box.Text);
            if (found.Count == 0) results.Children.Add(UiKit.Text("Ничего не нашла.", 12, true));
            foreach (var l in found.Take(12))
                results.Children.Add(l.Click != null ? Row(l.Text, ("Открыть", l.Click)) : Row(l.Text));
            if (S.FileMemory) Files(box.Text);
        };
        async void Files(string query)
        {
            await Task.Delay(400);   // only when typing has stopped
            if (box.Text != query) return;
            var files = await SecDownloads.Find(query);
            if (box.Text != query) return;
            foreach (var f in files.Take(10))
            {
                var host = Uri.TryCreate(f.Source, UriKind.Absolute, out var u) ? $" · с {u.Host}" : "";
                results.Children.Add(Row($"{f.Name}{host}", ("Показать", () => Shell.Open(Shell.Sys("explorer"), $"/select,\"{f.Path}\""))));
            }
        }
        b.Children.Add(box); b.Children.Add(results);
        return c;
    }

    UIElement WaitingCard()
    {
        var (c, b) = Card("Пока ты был занят");
        foreach (var s in SecStore.Cur.Suggestions.Take(10).ToList())
            b.Children.Add(Row(s.Text, ("Убрать", () => { SecStore.Cur.Suggestions.Remove(s); SecStore.Save(); Render(); })));
        Btn(b, "Убрать всё", () => { SecStore.Cur.Suggestions.Clear(); SecStore.Save(); Render(); });
        return c;
    }

    UIElement RememberedCard()
    {
        var (c, b) = Card("Запомнено", $"Открой страницу с расписанием и нажми {Hotkeys.Pretty(Settings.Current.Hotkeys.GetValueOrDefault("secRemember", ""))}.");
        foreach (var r in SecStore.Cur.Remembered.Take(15).ToList())
            b.Children.Add(Row((r.Daily ? "★ " : "") + r.Title,
                ("✕", () => { SecStore.Cur.Remembered.Remove(r); SecStore.Save(); Render(); }),
                (r.Daily ? "Не в сводку" : "В сводку", () => { r.Daily = !r.Daily; SecStore.Save(); Render(); }),
                ("Открыть", () => Secretary.Open(r))));
        return c;
    }

    UIElement NotesCard()
    {
        var (c, b) = Card("Заметки к программам");
        if (SecStore.Cur.AppNotes.Count == 0) b.Children.Add(UiKit.Text($"Пока пусто. Открой программу и нажми {Hotkeys.Pretty(Settings.Current.Hotkeys.GetValueOrDefault("secNote", ""))}.", 12, true));
        foreach (var (k, v) in SecStore.Cur.AppNotes.ToList())
            b.Children.Add(Row($"{k}: {(v.Length > 90 ? v[..90] + "…" : v)}", ("✕", () =>
            {
                if (!UiKit.Confirm($"Удалить заметку к «{k}»?")) return;
                SecStore.Cur.AppNotes.Remove(k); SecStore.Save(); Render();
            })));
        var recent = Notes.All.OrderByDescending(n => n.When).Take(5).ToList();
        if (recent.Count > 0) { b.Children.Add(new TextBlock { Text = "Последние быстрые заметки", Style = St("Dim"), Margin = new Thickness(0, 10, 0, 2) }); foreach (var n in recent) b.Children.Add(Row($"{n.When:dd.MM HH:mm} {n.Text}", ("✕", () => { Notes.Remove(n); Render(); }))); }
        return c;
    }

    UIElement MailCard()
    {
        var (c, b) = Card("Почта", "Нужен пароль приложения, а не основной пароль: Яндекс и Mail.ru выдают его в настройках безопасности аккаунта, Gmail — при включённой двухэтапной проверке. Хранится зашифрованным под твоей учётной записью Windows.");
        foreach (var a in SecStore.Cur.Mail.ToList())
            b.Children.Add(Row(a.Email, ("Убрать", () => { if (UiKit.Confirm($"Убрать почту {a.Email} и её пароль с этого ПК?")) { SecMail.Remove(a); Render(); } })));
        if (SecMail.LastError.Length > 0) { var e = UiKit.Text("Ошибка: " + SecMail.LastError, 12); e.Foreground = (System.Windows.Media.Brush)FindResource("WarnBrush"); e.TextWrapping = TextWrapping.Wrap; b.Children.Add(e); }
        foreach (var l in SecMail.Recent.Where(x => !x.Bulk).Take(5))
            b.Children.Add(Row($"{l.From}: {l.Subject}", ("Открыть", () => { if (l.Web.Length > 0) Shell.Open(l.Web); })));
        if (SecMail.Recent.Count(x => x.Bulk) is > 0 and var bulk) b.Children.Add(UiKit.Text($"И ещё рассылок: {bulk}", 12, true));

        var email = new TextBox { Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 4) };
        var pass = new PasswordBox { Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 4) };
        var host = new TextBox { Padding = new Thickness(8), Margin = new Thickness(0, 0, 0, 4) };
        var status = UiKit.Text("", 12, true);
        b.Children.Add(UiKit.WithHint(email, "Адрес почты"));
        b.Children.Add(UiKit.Text("Пароль приложения", 11, true));
        b.Children.Add(pass);
        b.Children.Add(UiKit.WithHint(host, "Сервер IMAP (только если почта не Яндекс, Mail.ru, Gmail, iCloud, Rambler)"));
        var add = Btn(b, "Подключить", () => { }, true);
        add.Click += async (_, _) =>
        {
            add.IsEnabled = false; status.Text = "Проверяю вход…";
            try
            {
                await SecMail.Add(email.Text.Trim(), pass.Password, host.Text.Trim().Length > 0 ? host.Text.Trim() : null);
                pass.Clear();
                Render();
            }
            catch (Exception e) { status.Text = "Не получилось: " + e.Message; add.IsEnabled = true; }
        };
        b.Children.Add(status);
        var imp = new TextBox { Text = string.Join(", ", S.MailImportant), Padding = new Thickness(8), Margin = new Thickness(0, 8, 0, 0) };
        imp.LostFocus += (_, _) => { S.MailImportant = imp.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(); Settings.Save(); };
        b.Children.Add(UiKit.WithHint(imp, "Сообщать только о письмах со словами (через запятую). Пусто — обо всех"));
        Btn(b, "Проверить сейчас", () => _ = SecMail.Check());
        return c;
    }

    UIElement WorkspaceCard()
    {
        var (c, b) = Card("Где я остановился и наборы", "Открытые программы, папки и активные вкладки браузера запоминаются сами каждые 15 минут. Набор с названием ещё включает профиль и «Не беспокоить», открывается с телефона и из палитры.");
        var st = SecStore.Cur;
        if (st.LastWorkspace.Count > 0) b.Children.Add(Row($"Последнее: {st.LastWorkspaceAt:dd.MM HH:mm} ({st.LastWorkspace.Count})", ("Вернуть", () => SecWorkspace.Restore(st.LastWorkspace))));
        foreach (var (name, items) in st.Workspaces.ToList())
        {
            b.Children.Add(Row($"{name} ({items.Count})", ("✕", () => { st.Workspaces.Remove(name); st.SetOptions.Remove(name); SecStore.Save(); Render(); }), ("Открыть", () => SecWorkspace.OpenSet(name))));
            if (!st.SetOptions.TryGetValue(name, out var o)) st.SetOptions[name] = o = new WorkSetOptions();
            var opts = new DockPanel { Margin = new Thickness(0, 0, 0, 6) };
            var dnd = new CheckBox { Content = "Не беспокоить", IsChecked = o.Dnd, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
            dnd.Click += (_, _) => { o.Dnd = dnd.IsChecked == true; SecStore.Save(); };
            DockPanel.SetDock(dnd, Dock.Right); opts.Children.Add(dnd);
            var prof = new ComboBox { ItemsSource = new[] { "Без профиля" }.Concat(Settings.Current.Profiles.Select(p => p.Name)).ToList(), SelectedItem = o.Profile.Length > 0 ? o.Profile : "Без профиля" };
            prof.SelectionChanged += (_, _) => { o.Profile = prof.SelectedIndex > 0 ? (string)prof.SelectedItem : ""; SecStore.Save(); };
            opts.Children.Add(prof);
            b.Children.Add(opts);
        }
        Btn(b, "Сохранить как…", () =>
        {
            var name = UiKit.Ask("Набор", "Название, например «Учёба» или «Монтаж»");
            if (!string.IsNullOrWhiteSpace(name)) _ = SecWorkspace.SaveNamed(name.Trim());
        });
        return c;
    }

    UIElement DownloadsCard()
    {
        var items = SecDownloads.Pending();
        var (c, b) = Card("Разбор «Загрузок»", items.Count == 0 ? "Разбирать нечего." : $"Файлов: {items.Count}. Без твоей кнопки ничего не двигается.");
        foreach (var g in items.GroupBy(i => i.Category))
        {
            var list = g.ToList();
            b.Children.Add(Row($"{g.Key}: {list.Count}", ($"Разложить ({list.Count})", () =>
            {
                if (!UiKit.Confirm($"Перенести файлы ({list.Count}) в «{g.Key}»?\n{list[0].Dest}")) return;
                int moved = 0;
                foreach (var i in list) try { SecDownloads.Move(i); moved++; } catch (Exception e) { Log.Error(e); }
                Osd.Show("", $"Перенесено: {moved}");
                Render();
            })));
        }
        if (S.DownloadRules)
        {
            b.Children.Add(new TextBlock { Text = "Твои правила", Style = St("Dim"), Margin = new Thickness(0, 10, 0, 2) });
            foreach (var r in SecStore.Cur.DownloadRules.ToList())
                b.Children.Add(Row($"«{r.Word}» → {r.Folder}", ("✕", () => { SecStore.Cur.DownloadRules.Remove(r); SecStore.Save(); Render(); })));
            var word = new TextBox { Padding = new Thickness(8), Margin = new Thickness(0, 4, 0, 4) };
            b.Children.Add(UiKit.WithHint(word, "Слово в имени файла, например «лаба»"));
            Btn(b, "Выбрать папку и добавить", () =>
            {
                if (word.Text.Trim().Length < 2) { Osd.Show("", "Сначала напиши слово"); return; }
                var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Куда складывать такие файлы" };
                if (dlg.ShowDialog() != true) return;
                if (!SecDownloads.AddRule(word.Text, dlg.FolderName)) { UiKit.Confirm("Папка должна быть внутри твоего профиля (Документы, Рабочий стол и т. п.)."); return; }
                Render();
            });
        }
        return c;
    }

    UIElement FeaturesCard()
    {
        var (c, b) = Card("Функции", "Каждую можно выключить отдельно.");
        foreach (var (name, hint, get, set) in Features)
        {
            var sw = new CheckBox { Style = St("Switch"), Content = name, IsChecked = get(), ToolTip = hint };
            sw.Click += (_, _) => { set(sw.IsChecked == true); Settings.Save(); Secretary.Apply(); Render(); };
            b.Children.Add(sw);
            var h = UiKit.Text(hint, 11, true); h.TextWrapping = TextWrapping.Wrap; h.Margin = new Thickness(0, 0, 0, 6);
            b.Children.Add(h);
        }
        return c;
    }
}
