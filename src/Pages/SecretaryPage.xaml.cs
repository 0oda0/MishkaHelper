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
        Cards.Children.Add(SearchCard());
        if (SecStore.Cur.Suggestions.Count > 0) Cards.Children.Add(WaitingCard());
        Cards.Children.Add(RememberedCard());
        if (S.AppNotes) Cards.Children.Add(NotesCard());
        if (S.Mail) Cards.Children.Add(MailCard());
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
        }
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
        };
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
        foreach (var l in SecMail.Recent.Take(5))
            b.Children.Add(Row($"{l.From}: {l.Subject}", ("Открыть", () => { if (l.Web.Length > 0) Shell.Open(l.Web); })));

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
        var (c, b) = Card("Где я остановился", "Открытые программы, папки и активные вкладки браузера. Запоминаются сами каждые 15 минут.");
        var st = SecStore.Cur;
        if (st.LastWorkspace.Count > 0) b.Children.Add(Row($"Последнее: {st.LastWorkspaceAt:dd.MM HH:mm} ({st.LastWorkspace.Count})", ("Вернуть", () => SecWorkspace.Restore(st.LastWorkspace))));
        foreach (var (name, items) in st.Workspaces.ToList())
            b.Children.Add(Row($"{name} ({items.Count})", ("✕", () => { st.Workspaces.Remove(name); SecStore.Save(); Render(); }), ("Вернуть", () => SecWorkspace.Restore(items))));
        Btn(b, "Сохранить как…", () =>
        {
            var name = UiKit.Ask("Рабочее место", "Название, например «Учёба» или «Монтаж»");
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
