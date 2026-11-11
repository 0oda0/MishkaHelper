using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace MishkaHelper.Core;

public class InboxItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Kind { get; set; } = "note";     // note, offer, overdue, habit, file, mail
    public string Title { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime? At { get; set; }
    public bool HasTime { get; set; }
    public string Ref { get; set; } = "";          // task id, file path, address
    public DateTime When { get; set; } = DateTime.Now;
}

/// <summary>Everything that needs an answer stays here until answered: a toast can be missed, this can't.</summary>
public static class SecInbox
{
    public static event Action? Changed;
    public static int Count => SecStore.Cur.Inbox.Count;

    public static void Add(InboxItem i)
    {
        var l = SecStore.Cur.Inbox;
        if (l.Any(x => x.Kind == i.Kind && x.Title == i.Title && x.Text == i.Text && x.Ref == i.Ref)) return;
        l.Insert(0, i);
        if (l.Count > 100) l.RemoveRange(100, l.Count - 100);
        SecStore.Save();
        Changed?.Invoke();
    }

    public static void Remove(InboxItem i) { SecStore.Cur.Inbox.Remove(i); SecStore.Save(); Changed?.Invoke(); }

    /// <summary>Late tasks are asked about once, together, instead of quietly staying in a list.</summary>
    public static void ReviewOverdue(DateTime now)
    {
        foreach (var t in Secretary.OpenTasks().Where(t => t.Due?.Date < now.Date))
            Add(new InboxItem { Kind = "overdue", Title = "Просрочено", Text = t.Text, Ref = t.Id, At = t.Due });
    }

    public static (string Label, Action Click)[] Buttons(InboxItem i)
    {
        SecTask? Task() => SecStore.Cur.Tasks.FirstOrDefault(t => t.Id == i.Ref);
        switch (i.Kind)
        {
            case "offer":
                return new (string, Action)[]
                {
                    ("Напомнить", () => { Reminders.Add(i.Text, i.At ?? DateTime.Now.AddDays(1), i.HasTime); Remove(i); Osd.Show("", "Напоминание добавлено"); }),
                    ("Не надо", () => Remove(i)),
                };
            case "overdue" or "plan":
                return new (string, Action)[]
                {
                    ("Сделано", () => { if (Task() is { } t) Secretary.Finish(t); Remove(i); }),
                    ("На завтра", () => { if (Task() is { } t) { t.Due = DateTime.Today.AddDays(1); t.HasTime = false; SecStore.Save(); } Remove(i); }),
                    ("Удалить", () => { if (Task() is { } t) SecStore.Cur.Tasks.Remove(t); Remove(i); }),
                };
            case "habit":
                return new (string, Action)[]
                {
                    ("Да, напоминай", () => { var r = Reminders.Add(i.Text, i.At ?? DateTime.Now.AddDays(7), true); r.Repeat = "weekly"; Settings.Save(); Remove(i); Osd.Show("", "Буду напоминать каждую неделю"); }),
                    ("Не предлагай", () => Remove(i)),
                };
            case "file":
                return new (string, Action)[]
                {
                    ("Показать", () => { Shell.Open(Shell.Sys("explorer"), $"/select,\"{i.Ref}\""); Remove(i); }),
                    ("Убрать", () => Remove(i)),
                };
            case "mail":
                return new (string, Action)[]
                {
                    ("Ответить", () => { Shell.Open(i.Ref); Remove(i); }),
                    ("Убрать", () => Remove(i)),
                };
            default:
                return new (string, Action)[] { ("Убрать", () => Remove(i)) };
        }
    }
}

/// <summary>What a line typed into the secretary's window asks for. Pure, so the phrases are tested; anything else is taken down as a note.</summary>
public static class SecAsk
{
    const RegexOptions Ru = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static (string Kind, string Arg) Route(string text)
    {
        var t = text.Trim();
        if (t.Length == 0) return ("none", "");
        Match m;
        if (Regex.IsMatch(t, @"^(помощь|что ты умеешь|что умеешь)\??$", Ru)) return ("help", "");
        if (Regex.IsMatch(t, @"\bрасписани", Ru) && Regex.IsMatch(t, @"^(где|что|какое|покажи|открой|есть|а\s)|\?$|завтра|сегодня|^расписание", Ru)) return ("schedule", t);
        if (Regex.IsMatch(t, @"\bпар[ыа]?\b.*\?|^какие пары|^что по парам", Ru)) return ("schedule", t);
        if (Regex.IsMatch(t, @"почт|письм", Ru) && Regex.IsMatch(t, @"^(что|есть|как|покажи|проверь)|\?$", Ru)) return ("mail", "");
        if (Regex.IsMatch(t, @"^(что\s+)?просрочен|^что я (забыл|пропустил)", Ru)) return ("overdue", "");
        if (Regex.IsMatch(t, @"^(какой\s+)?план\b|^составь план|^распланируй", Ru)) return ("plan", "");
        if ((m = Regex.Match(t, @"^(?:перенеси|перенести|сдвинь|отложи)\s+(.+)$", Ru)).Success) return ("move", m.Groups[1].Value);
        if ((m = Regex.Match(t, @"^(?:сделал[аи]?|готово|выполнено|закрой|закрыть)\s+(.+)$", Ru)).Success) return ("done", m.Groups[1].Value);
        if ((m = Regex.Match(t, @"^(?:где|найди|найти|куда я (?:сохранил|положил|записал)\w*)\s+(.+?)\??$", Ru)).Success) return ("find", m.Groups[1].Value);
        if (Regex.IsMatch(t, @"^(что|какие дела|какие планы|чем)\b.*(сегодня|завтра|послезавтра|понедельник|вторник|сред|четверг|пятниц|суббот|воскресень|\d)", Ru) || Regex.IsMatch(t, @"^что у меня\b", Ru)) return ("agenda", t);
        return ("note", t);
    }

    /// <summary>Best match by shared word stems. Null unless at least one real word matches.</summary>
    internal static T? Best<T>(string query, IEnumerable<T> items, Func<T, string> text) where T : class
    {
        string[] Words(string s) => RuText.Clean(s).Split(' ', StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length > 2).Select(w => w.Length > 4 ? w[..^2] : w).ToArray();
        var q = Words(query);
        return items.Select(i => (i, score: q.Count(w => Words(text(i)).Any(x => x.StartsWith(w) || w.StartsWith(x))))).Where(x => x.score > 0).OrderByDescending(x => x.score).Select(x => x.i).FirstOrDefault();
    }

    public record Answer(string Text, (string Label, Action Click)[]? Buttons = null);

    public static async Task<Answer> Reply(string text)
    {
        var now = DateTime.Now;
        var (kind, arg) = Route(text);
        var s = Settings.Current.Secretary;
        switch (kind)
        {
            case "none": return new("");
            case "help":
                return new("Спроси: «что завтра?», «расписание на завтра», «где договор», «что с почтой», «что просрочено», «какой план».\nПоручи: «перенеси отчёт на пятницу», «сделал отчёт», «завтра в 6 тренировка». Остальное запишу в заметки.");
            case "agenda":
                var day = When.Parse(arg, now)?.At.Date ?? now.Date;
                var items = await Agenda(day);
                return new(items.Count == 0 ? $"{Day(day, now)}: ничего нет." : $"{Day(day, now)}:\n" + string.Join("\n", items.Select(x => "• " + x)));
            case "schedule":
                var when = When.Parse(arg, now)?.At.Date ?? now.Date;
                return await ScheduleAnswer(when, now);
            case "mail":
                if (SecStore.Cur.Mail.Count == 0) return new("Почта не подключена: это делается на странице «Секретарша».");
                var real = SecMail.Recent.Where(l => !l.Bulk).Take(5).ToList();
                return new($"Непрочитанных: {SecMail.Unread}." + (real.Count > 0 ? "\n" + string.Join("\n", real.Select(l => $"• {SecMail.ShortFrom(l.From)}: {l.Subject}")) : "\nВажных среди последних нет."), new (string, Action)[] { ("Открыть почту", SecMail.OpenWeb) });
            case "overdue":
                var late = Secretary.OpenTasks().Where(t => t.Due?.Date < now.Date).ToList();
                SecInbox.ReviewOverdue(now);
                return new(late.Count == 0 ? "Ничего не просрочено." : $"Просрочено: {late.Count}. Решить можно ниже, во «Входящих».");
            case "plan":
                var plan = await DayPlan(now);
                return new(plan.Count == 0 ? "На сегодня планировать нечего." : "План:\n" + string.Join("\n", plan.Select(x => "• " + x)));
            case "move":
                var w = When.Parse(arg, now);
                if (w == null) return new("На когда перенести? Например: «перенеси отчёт на пятницу».");
                var what = Regex.Replace(w.Rest, @"\s+на$", "", Ru);
                var task = Best(what, Secretary.OpenTasks(), t => t.Text);
                var rem = Best(what, Settings.Current.Reminders.Where(r => !r.Done), r => r.Text);
                if (task == null && rem == null) return new($"Не нашла «{what}» в делах.");
                if (task != null) { task.Due = w.At; task.HasTime = w.HasTime; SecStore.Save(); }
                if (rem != null) { rem.At = w.At; rem.HasTime = w.HasTime; rem.EveWarned = false; Settings.Save(); }
                Secretary.Apply();
                return new($"Перенесла «{task?.Text ?? rem!.Text}» на {When.Say(w.At, w.HasTime, now)}.");
            case "done":
                var d = Best(arg, Secretary.OpenTasks(), t => t.Text);
                if (d == null) return new($"Не нашла «{arg}» в делах.");
                Secretary.Finish(d);
                return new($"Отметила: {d.Text}.");
            case "find":
                var found = Secretary.Search(arg);
                var files = s.FileMemory ? await SecDownloads.Find(arg) : new();
                if (found.Count == 0 && files.Count == 0) return new("Ничего не нашла.");
                var first = found.FirstOrDefault(f => f.Click != null);
                var btns = new List<(string, Action)>();
                if (first != null) btns.Add(("Открыть", first.Click!));
                if (files.Count > 0) { var f = files[0]; btns.Add(("Показать файл", () => Shell.Open(Shell.Sys("explorer"), $"/select,\"{f.Path}\""))); }
                return new(string.Join("\n", found.Take(4).Select(x => "• " + x.Text).Concat(files.Take(4).Select(x => "• файл: " + x.Name))), btns.ToArray());
            default:
                return new(SecPlan.Apply(text.Trim(), Secretary.CurrentApp) is var said ? said + "." : "");
        }
    }

    static string Day(DateTime d, DateTime now) => Reminders.Cap(When.Say(d, false, now));

    static async Task<Answer> ScheduleAnswer(DateTime day, DateTime now)
    {
        var page = SecSchedule.Page;
        if (page == null) return new("Запомненного расписания нет. Открой его в браузере и нажми Ctrl+Alt+S.");
        (string, Action) open = ("Открыть сайт", () => Secretary.Open(page));
        var lines = await SecSchedule.Lines(day, fresh: true);
        if (lines.Count > 0) return new($"{Day(day, now)}, {page.Title}:\n" + string.Join("\n", lines.Select(x => "• " + x)), new[] { open });
        return SecSchedule.Problem(day) switch
        {
            "login" => new("Сайт просит вход. Войди один раз в окне Mishka, потом спроси снова.", new (string, Action)[] { ("Войти", () => WebReader.OpenWindow(page.Target, "Вход на сайт расписания")), open }),
            "offline" => new(Vpn.IsUp()
                ? "Сайт не открылся: похоже, его не пускает VPN. Выключи VPN на минуту и спроси снова — неделя запомнится, дальше VPN не помешает. Или скопируй страницу расписания (Ctrl+A, Ctrl+C) и нажми «Из буфера» на странице «Секретарша»."
                : "Сайт не открылся. Попробуй позже или скопируй страницу расписания (Ctrl+A, Ctrl+C) и нажми «Из буфера» на странице «Секретарша».", new[] { open }),
            "empty" => new($"На сайте пусто: {Day(day, now).ToLowerInvariant()} занятий не нашла.", new[] { open }),
            _ => new("Страница открылась, но день на ней не нашла. Показать сайт?", new[] { open }),
        };
    }

    public static async Task<List<string>> Agenda(DateTime day)
    {
        var l = new List<string>();
        foreach (var line in await SecSchedule.Lines(day)) l.Add(line);
        if (Calendar.Configured)
            try { foreach (var e in await Calendar.On(day)) l.Add((e.AllDay ? "" : e.Start.ToString("HH:mm ")) + e.Title); } catch (Exception e) { Log.Error(e); }
        foreach (var r in Settings.Current.Reminders.Where(r => !r.Done && Reminders.RingAt(r).Date == day)) l.Add((r.HasTime ? r.At.ToString("HH:mm ") : "") + r.Text);
        foreach (var t in Secretary.OpenTasks().Where(t => t.Due?.Date == day && !l.Any(x => x.EndsWith(t.Text)))) l.Add(t.Text);
        return l;
    }

    public static async Task<List<string>> DayPlan(DateTime now)
    {
        var busy = new List<(TimeSpan From, TimeSpan To, string Text)>();
        foreach (var line in await SecSchedule.Lines(now.Date))
            if (SecPlan.LineTime(line) is { } at) busy.Add((at, at + TimeSpan.FromMinutes(90), line));
        if (Calendar.Configured)
            try { foreach (var e in await Calendar.On(now.Date)) if (!e.AllDay) busy.Add((e.Start.TimeOfDay, e.End.TimeOfDay, e.Title)); } catch (Exception e) { Log.Error(e); }
        var tasks = Secretary.OpenTasks().OrderBy(t => t.Due ?? DateTime.MaxValue).Select(t => t.Text).Take(8).ToList();
        return SecPlan.PlanDay(now, busy, tasks);
    }
}

/// <summary>What is worth saying about the program in front, and what is better left alone. Pure, so the choice is tested.</summary>
public static class SecContext
{
    static readonly HashSet<string> Silent = new(StringComparer.OrdinalIgnoreCase)
    {
        "MishkaHelper", "explorer", "ApplicationFrameHost", "SearchHost", "StartMenuExperienceHost", "ShellExperienceHost", "TextInputHost", "LockApp", "SystemSettings",
        "Taskmgr", "cmd", "powershell", "pwsh", "WindowsTerminal", "conhost", "dllhost", "rundll32", "Idle", "consent", "SnippingTool", "ScreenClippingHost", "Calculator", "CalculatorApp", "claude",
    };

    public static bool ShouldStayQuiet(string process, bool fullScreen) => fullScreen || process.Length == 0 || Silent.Contains(process);

    public record Hint(string Key, string Text, string Action);   // Action: note, page, tasks

    public static Hint? For(string process, string title, string? url, bool fullScreen, IDictionary<string, string> notes, IEnumerable<RememberedItem> pages, IEnumerable<string> tasks)
    {
        if (ShouldStayQuiet(process, fullScreen)) return null;
        var host = url != null && Uri.TryCreate(url, UriKind.Absolute, out var u) ? u.Host.Replace("www.", "").ToLowerInvariant() : null;
        var key = host ?? process.ToLowerInvariant();
        if (notes.TryGetValue(key, out var note) && note.Trim().Length > 0)
            return new(key, "Заметка: " + (note.Length > 90 ? note[..90] + "…" : note), "note");
        if (host != null && pages.FirstOrDefault(p => Uri.TryCreate(p.Target, UriKind.Absolute, out var pu) && pu.Host.Replace("www.", "").Equals(host, StringComparison.OrdinalIgnoreCase)) is { } page)
            return new(key, $"Ты это запоминал: {page.Title}", "page");
        var task = tasks.FirstOrDefault(t => SecPlan.TaskMentions(t, process, title, url));
        return task != null ? new(key + "|" + task, "По делу: " + task, "tasks") : null;
    }
}

/// <summary>The little round button that stays on screen: opens the secretary, shows how many things wait, says short hints beside itself.</summary>
public static class SecBubble
{
    static Window? _w;
    static TextBlock? _badge, _hint;
    static Border? _hintBox, _badgeBox;
    static Action? _hintClick;
    static readonly DispatcherTimer HintTimer = new() { Interval = TimeSpan.FromSeconds(9) };
    static SecretarySettings S => Settings.Current.Secretary;
    public static string LastHint { get; private set; } = "";

    public static void Apply(bool fullScreen)
    {
        bool want = S.Enabled && S.Bubble && !fullScreen && !Focus.On;
        if (!want) { _w?.Hide(); return; }
        if (_w == null) Build();
        if (!_w!.IsVisible) { _w.Show(); Place(); }
        _badge!.Text = SecInbox.Count.ToString();
        _badgeBox!.Visibility = SecInbox.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _w.ToolTip = SecInbox.Count > 0 ? $"{S.Name}: ждёт ответа {SecInbox.Count}" : S.Name;
    }

    static void Place()
    {
        var wa = SystemParameters.WorkArea;
        _w!.UpdateLayout();
        // Anchored by the button's right edge, so a hint grows to the left and the button stays where the user put it.
        double right = S.BubbleX >= 0 ? S.BubbleX : wa.Right - 8, top = S.BubbleY >= 0 ? S.BubbleY : wa.Top + wa.Height * 0.55;
        _w.Left = Math.Clamp(right - _w.ActualWidth, wa.Left - 20, wa.Right - 60);
        _w.Top = Math.Clamp(top, wa.Top - 10, wa.Bottom - 70);
    }

    static void Build()
    {
        var w = Floating.Create(clickThrough: false);
        var accent = (Brush)Application.Current.Resources["AccentBrush"];
        _hint = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, MaxWidth = 240, VerticalAlignment = VerticalAlignment.Center };
        _hintBox = new Border { Child = _hint, Background = new SolidColorBrush(Color.FromArgb(0xF0, 16, 16, 28)), BorderBrush = accent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 8, 0), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center };
        _hintBox.MouseLeftButtonUp += (_, e) => { e.Handled = true; HideHint(); _hintClick?.Invoke(); };
        var icon = Floating.Icon("", 20);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        _badge = new TextBlock { FontSize = 10, FontWeight = FontWeights.Bold, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center };
        _badgeBox = new Border { Child = _badge, Background = (Brush)Application.Current.Resources["WarnBrush"], CornerRadius = new CornerRadius(8), MinWidth = 16, Padding = new Thickness(3, 0, 3, 0), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Visibility = Visibility.Collapsed };
        _badge.Foreground = Brushes.Black;
        var ball = new Grid { Width = 46, Height = 46 };
        ball.Children.Add(new Border { CornerRadius = new CornerRadius(23), Background = new SolidColorBrush(Color.FromArgb(0xF2, 28, 20, 48)), BorderBrush = accent, BorderThickness = new Thickness(1.5), Child = icon, Effect = (System.Windows.Media.Effects.Effect)Application.Current.Resources["Glow"] });
        ball.Children.Add(_badgeBox);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10) };
        row.Children.Add(_hintBox); row.Children.Add(ball);
        w.Content = row;
        // DragMove runs until the button is released and swallows the release, so the press ends where it returns: not moved = a click.
        ball.MouseLeftButtonDown += (_, e) =>
        {
            e.Handled = true;
            double x = w.Left, y = w.Top;
            try { w.DragMove(); } catch { }
            if (Math.Abs(w.Left - x) + Math.Abs(w.Top - y) < 4) { SecDesk.Toggle(); return; }
            S.BubbleX = w.Left + w.ActualWidth; S.BubbleY = w.Top;
            Settings.Save();
        };
        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Открыть" }; open.Click += (_, _) => SecDesk.Toggle();
        var page = new MenuItem { Header = "Страница секретарши" }; page.Click += (_, _) => App.Current.Navigate("Secretary");
        var hide = new MenuItem { Header = "Скрыть кнопку" }; hide.Click += (_, _) => { S.Bubble = false; Settings.Save(); Apply(false); Toast.Show("", S.Name, "Кнопка скрыта. Окно по-прежнему открывается клавишами, вернуть кнопку можно на странице «Секретарша»."); };
        menu.Items.Add(open); menu.Items.Add(page); menu.Items.Add(hide);
        ball.ContextMenu = menu;
        HintTimer.Tick += (_, _) => HideHint();
        _w = w;
    }

    public static void Say(string text, Action? click)
    {
        LastHint = text;
        if (_w == null || !_w.IsVisible) return;
        _hint!.Text = text; _hintClick = click;
        double right = _w.Left + _w.ActualWidth;
        _hintBox!.Visibility = Visibility.Visible;
        _w.UpdateLayout();
        _w.Left = right - _w.ActualWidth;
        HintTimer.Stop(); HintTimer.Start();
    }

    static void HideHint()
    {
        HintTimer.Stop();
        if (_w == null || _hintBox!.Visibility != Visibility.Visible) return;
        double right = _w.Left + _w.ActualWidth;
        _hintBox.Visibility = Visibility.Collapsed;
        _w.UpdateLayout();
        _w.Left = right - _w.ActualWidth;
    }

    public static bool Shown => _w?.IsVisible == true;
}

/// <summary>The secretary's own window: one line for questions and orders, today, and everything waiting for an answer.</summary>
public static class SecDesk
{
    static Window? _w;
    static StackPanel? _answer, _today, _inbox;
    static TextBox? _box;
    static SecretarySettings S => Settings.Current.Secretary;

    public static void Toggle() { if (_w != null) _w.Close(); else Open(activate: true); }

    public static void Open(bool activate, string? greeting = null)
    {
        if (_w != null) { if (activate) _w.Activate(); return; }
        if (!Secretary.Require(true, S.Name)) return;
        var w = Floating.Create(clickThrough: false, noActivate: false);
        w.ShowActivated = activate;   // the morning summary must not steal the keyboard from what is being typed
        var sp = new StackPanel { Width = 480 };
        var head = new DockPanel();
        var close = new Button { Content = "", FontFamily = (FontFamily)Application.Current.Resources["IconFont"], Style = (Style)Application.Current.Resources["GhostButton"], Padding = new Thickness(6, 2, 6, 2) };
        close.Click += (_, _) => w.Close();
        DockPanel.SetDock(close, Dock.Right); head.Children.Add(close);
        head.Children.Add(UiKit.Text(greeting ?? SecVoiceTone.Greeting(DateTime.Now, S), 15, false, FontWeights.SemiBold));
        sp.Children.Add(head);
        _box = new TextBox { FontSize = 15, Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 8, 0, 4) };
        sp.Children.Add(UiKit.WithHint(_box, "Спроси или поручи: что завтра? · расписание · перенеси отчёт на пятницу"));
        _answer = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        sp.Children.Add(_answer);
        _today = new StackPanel();
        _inbox = new StackPanel();
        var scroll = new ScrollViewer { MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = new StackPanel { Children = { _today, _inbox } } };
        sp.Children.Add(scroll);
        var panel = Floating.Panel(sp, 16);
        panel.Padding = new Thickness(14);
        w.Content = panel;
        _box.PreviewKeyDown += async (_, e) =>
        {
            if (e.Key == Key.Escape) { w.Close(); return; }
            if (e.Key != Key.Enter || _box.Text.Trim().Length == 0) return;
            e.Handled = true;
            var q = _box.Text.Trim(); _box.Clear();
            ShowAnswer(new("…"));
            try { ShowAnswer(await SecAsk.Reply(q)); } catch (Exception x) { Log.Error(x); ShowAnswer(new("Не получилось: " + x.Message)); }
            Fill();
        };
        w.Closed += (_, _) => { _w = null; SecInbox.Changed -= Fill; };
        SecInbox.Changed += Fill;
        _w = w;
        w.Show();
        var wa = SystemParameters.WorkArea;
        w.Left = wa.Right - w.ActualWidth - 70;
        w.Top = Math.Max(wa.Top, wa.Top + (wa.Height - w.ActualHeight) * 0.3);
        if (activate) { Native.SetForegroundWindow(new WindowInteropHelper(w).Handle); w.Activate(); _box.Focus(); }
        Fill();
    }

    static void ShowAnswer(SecAsk.Answer a)
    {
        if (_answer == null) return;
        _answer.Children.Clear();
        if (a.Text.Length == 0) return;
        var t = UiKit.Text(a.Text, 13); t.TextWrapping = TextWrapping.Wrap;
        _answer.Children.Add(t);
        if (a.Buttons is { Length: > 0 }) _answer.Children.Add(Bar(a.Buttons));
    }

    static WrapPanel Bar((string Label, Action Click)[] buttons)
    {
        var bar = new WrapPanel { Margin = new Thickness(0, 4, 0, 2) };
        foreach (var (label, click) in buttons)
        {
            var b = new Button { Content = label, Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(0, 0, 6, 2), FontSize = 12 };
            b.Click += (_, _) => { try { click(); } catch (Exception e) { Log.Error(e); Osd.Show("", e.Message); } };
            bar.Children.Add(b);
        }
        return bar;
    }

    static async void Fill()
    {
        if (_today == null || _inbox == null) return;
        var lines = await Secretary.Today();
        if (_w == null) return;
        _today.Children.Clear();
        _today.Children.Add(new TextBlock { Text = "Сегодня", Style = (Style)Application.Current.Resources["Dim"], Margin = new Thickness(0, 6, 0, 2) });
        if (lines.Count == 0) _today.Children.Add(UiKit.Text("Дел нет.", 13, true));
        foreach (var l in lines)
        {
            var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
            if (l.Click != null) { var b = new Button { Content = "Открыть", Style = (Style)Application.Current.Resources["GhostButton"], Padding = new Thickness(8, 1, 8, 1) }; var c = l.Click; b.Click += (_, _) => c(); DockPanel.SetDock(b, Dock.Right); row.Children.Add(b); }
            var t = UiKit.Text(l.Text, 13); t.TextWrapping = TextWrapping.Wrap; row.Children.Add(t);
            _today.Children.Add(row);
        }
        _inbox.Children.Clear();
        var items = SecStore.Cur.Inbox.ToList();
        if (items.Count > 0) _inbox.Children.Add(new TextBlock { Text = $"Входящие: {items.Count}", Style = (Style)Application.Current.Resources["Dim"], Margin = new Thickness(0, 10, 0, 2) });
        foreach (var i in items.Take(20))
        {
            var box = new StackPanel { Margin = new Thickness(0, 3, 0, 5) };
            var t = UiKit.Text(string.IsNullOrEmpty(i.Title) ? i.Text : $"{i.Title}: {i.Text}", 13); t.TextWrapping = TextWrapping.Wrap;
            box.Children.Add(t); box.Children.Add(Bar(SecInbox.Buttons(i)));
            _inbox.Children.Add(box);
        }
    }

    public static bool IsOpen => _w != null;
}

/// <summary>A name and a manner of speaking: greetings only; facts are always said the same way.</summary>
public static class SecVoiceTone
{
    public static string Greeting(DateTime now, SecretarySettings s)
    {
        var part = now.Hour switch { >= 5 and < 12 => "Доброе утро", >= 12 and < 18 => "Добрый день", >= 18 and < 23 => "Добрый вечер", _ => "Доброй ночи" };
        return s.Tone switch
        {
            "friend" => $"{part}! Это {s.Name}, вот что у нас",
            "short" => s.Name,
            _ => $"{part}. {s.Name} на связи",
        };
    }
}

/// <summary>Weekly habits: the same program at the same weekday and hour, three different weeks in a row of notes. Asked once, then never again.</summary>
public static class SecHabits
{
    public static void Note(string process, DateTime now, Dictionary<string, List<string>> log)
    {
        var key = $"{(int)now.DayOfWeek}|{now.Hour}|{process.ToLowerInvariant()}";
        if (!log.TryGetValue(key, out var days)) log[key] = days = new();
        var day = now.ToString("yyyy-MM-dd");
        if (days.Contains(day)) return;
        days.Add(day);
        if (days.Count > 12) days.RemoveAt(0);
    }

    /// <summary>Key of a habit: 3+ different days on the same weekday and hour, within the last five weeks.</summary>
    public static IEnumerable<string> Found(Dictionary<string, List<string>> log, ISet<string> asked, DateTime now) =>
        log.Where(kv => !asked.Contains(kv.Key) && kv.Value.Count(d => DateTime.TryParse(d, out var x) && (now - x).TotalDays <= 35) >= 3).Select(kv => kv.Key);
}
