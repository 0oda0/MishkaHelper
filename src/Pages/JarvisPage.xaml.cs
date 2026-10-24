using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

public partial class JarvisPage : UserControl
{
    static JarvisSettings J => Settings.Current.Jarvis;
    static Style S(string k) => (Style)Application.Current.Resources[k];

    public JarvisPage()
    {
        InitializeComponent();
        Cards.Children.Add(MainCard());
        Cards.Children.Add(CommandsCard());
        Cards.Children.Add(ModelsCard());
        Cards.Children.Add(SitesCard());
        Cards.Children.Add(CalendarCard());
        Cards.Children.Add(NotesCard());
    }

    static (Border, StackPanel) Card(string title, string? hint = null)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = title, Style = S("H2") });
        if (hint != null) { var h = UiKit.Text(hint, 12, true); h.Margin = new Thickness(0, 0, 0, 8); sp.Children.Add(h); }
        return (UiKit.Card(sp), sp);
    }

    static CheckBox Switch(Panel p, string text, bool v, Action<bool> set)
    {
        var cb = new CheckBox { Style = S("Switch"), Content = text, IsChecked = v };
        cb.Click += (_, _) => { set(cb.IsChecked == true); Settings.Save(); };
        p.Children.Add(cb);
        return cb;
    }

    static ComboBox Combo(Panel p, string label, IEnumerable<(string Key, string Name)> items, string cur, Action<string> set)
    {
        p.Children.Add(UiKit.Text(label, 12, true));
        var list = items.ToList();
        var cb = new ComboBox { Margin = new Thickness(0, 2, 0, 8) };
        foreach (var i in list) cb.Items.Add(i.Name);
        cb.SelectedIndex = Math.Max(0, list.FindIndex(i => i.Key == cur));
        cb.SelectionChanged += (_, _) => { if (cb.SelectedIndex >= 0) { set(list[cb.SelectedIndex].Key); Settings.Save(); } };
        p.Children.Add(cb);
        return cb;
    }

    UIElement MainCard()
    {
        var (c, b) = Card("Включение", "Скажи «Джарвис» и команду, или нажми Ctrl+Alt+J, или кликни по шару.");
        Switch(b, "Джарвис слушает микрофон", J.Enabled, async v => { J.Enabled = v; Settings.Save(); if (v) await Jarvis.Start(); else Jarvis.Stop(); });
        Switch(b, "Слово-активатор", J.WakeWordOn, v => J.WakeWordOn = v);
        var wake = new TextBox { Text = J.WakeWord, Margin = new Thickness(0, 2, 0, 8) };
        wake.LostFocus += (_, _) => { J.WakeWord = wake.Text.Trim().ToLowerInvariant(); Settings.Save(); };
        b.Children.Add(wake);
        Switch(b, "Отвечать голосом", J.Speak, v => J.Speak = v);
        Switch(b, "Диалог: после ответа 6 с слушать без «Джарвис»", J.FollowUp, v => J.FollowUp = v);
        Switch(b, "Перебивать словом «стоп» (лучше в наушниках)", J.BargeIn, v => J.BargeIn = v);
        Switch(b, "Начинать говорить, пока ИИ ещё думает", J.Stream, v => J.Stream = v);
        Switch(b, "Сигнал «слушаю», когда слышу «Джарвис»", J.WakeSound, v => J.WakeSound = v);
        Switch(b, "Приглушать музыку и видео, когда слышу «Джарвис»", J.Duck, v => J.Duck = v);
        var duckText = UiKit.Text($"Громкость других программ на это время: {J.DuckPercent}%", 12, true);
        var duck = new Slider { Minimum = 0, Maximum = 60, Value = J.DuckPercent, TickFrequency = 5, IsSnapToTickEnabled = true };
        duck.ValueChanged += (_, e) => { J.DuckPercent = (int)e.NewValue; duckText.Text = $"Громкость других программ на это время: {J.DuckPercent}%"; };
        duck.LostMouseCapture += (_, _) => Settings.Save();
        b.Children.Add(duckText); b.Children.Add(duck);
        Switch(b, "Неоновый шар на экране", J.ShowOrb, v => { J.ShowOrb = v; if (v) JarvisOrb.Show(); else JarvisOrb.Hide(); });
        Switch(b, "Подсказки по ситуации (перегрев, память, батарея)", J.Proactive, v => J.Proactive = v);
        Combo(b, "Микрофон", new[] { ("", "По умолчанию") }.Concat(Ears.Microphones().Select(m => (m, m))), J.Microphone, async v => { J.Microphone = v; if (J.Enabled) await Jarvis.Restart(); });
        var chat = new Button { Content = "Открыть чат", Style = S("AccentButton"), HorizontalAlignment = HorizontalAlignment.Left };
        chat.Click += (_, _) => JarvisChat.Open();
        b.Children.Add(chat);

        // Voice
        b.Children.Add(new TextBlock { Text = "Голос", Style = S("H2"), Margin = new Thickness(0, 14, 0, 6) });
        Combo(b, "Движок", new[] { ("edge", "Нейроголоса Microsoft — самые живые (нужен интернет)"), ("piper", "Piper — офлайн"), ("windows", "Голос Windows — роботизированный") }, J.Tts, v => J.Tts = v);
        Switch(b, "Как в фильме: британский голос, ответы на английском (команды — по-русски)", J.Movie, v => J.Movie = v);
        Combo(b, "Голос «как в фильме»", EdgeTts.Movie.Select(v => (v.Id, v.Name)), J.MovieVoice, v => J.MovieVoice = v);
        Combo(b, "Голос Джарвиса (по-русски)", EdgeTts.Russian.Select(v => (v.Id, v.Name)), J.EdgeVoice, v => J.EdgeVoice = v);
        var pitchText = UiKit.Text($"Тон: {J.EdgePitch:+0;-0;0}%", 12, true);
        var pitch = new Slider { Minimum = -20, Maximum = 10, Value = J.EdgePitch, TickFrequency = 1, IsSnapToTickEnabled = true };
        pitch.ValueChanged += (_, e) => { J.EdgePitch = (int)e.NewValue; pitchText.Text = $"Тон: {J.EdgePitch:+0;-0;0}%"; };
        pitch.LostMouseCapture += (_, _) => Settings.Save();
        b.Children.Add(pitchText); b.Children.Add(pitch);
        b.Children.Add(UiKit.Text("Без интернета Джарвис сам переключится на офлайн-голос Piper (если скачан).", 11, true));
        var status = UiKit.Text("", 12, true);
        void Status() => status.Text = Voice.PiperReady(J.PiperVoice) ? "✅ Голос скачан" : "Голос не скачан (≈80 МБ вместе с Piper)";
        Combo(b, "Офлайн-голос Piper", Voice.AllPiperVoices().Select(v => (v.Key, v.Name)), J.PiperVoice, v => { J.PiperVoice = v; Status(); });
        Status();
        b.Children.Add(status);
        Combo(b, "Голос Windows (запасной)", Voice.WindowsVoices().Select(v => (v, v)), J.Voice, v => J.Voice = v);
        Switch(b, "Эффект «ИИ-дворецкий» (ниже тон, цифровой оттенок)", J.VoiceFx, v => J.VoiceFx = v);
        var rateText = UiKit.Text($"Скорость речи: {J.Rate:0.0}×", 12, true);
        var rate = new Slider { Minimum = 0.7, Maximum = 1.5, Value = J.Rate, TickFrequency = 0.05, IsSnapToTickEnabled = true };
        rate.ValueChanged += (_, e) => { J.Rate = Math.Round(e.NewValue, 2); rateText.Text = $"Скорость речи: {J.Rate:0.0}×"; };
        rate.LostMouseCapture += (_, _) => Settings.Save();
        b.Children.Add(rateText); b.Children.Add(rate);
        var wp = new WrapPanel();
        var dl = new Button { Content = "Скачать голос" };
        dl.Click += async (_, _) =>
        {
            dl.IsEnabled = false;
            try { await Voice.DownloadPiper(J.PiperVoice, f => Dispatcher.BeginInvoke(() => dl.Content = $"{f * 100:0}%")); }
            catch (Exception e) { Osd.Show("", e.Message); }
            dl.Content = "Скачать голос"; dl.IsEnabled = true; Status();
        };
        var test = new Button { Content = "Послушать" };
        test.Click += (_, _) => _ = Voice.Say("Добрый вечер, сэр. Все системы в норме. Чем могу помочь?");
        var own = new Button { Content = "Свой голос (.onnx)…", ToolTip = "Голос Piper: файл .onnx и рядом .onnx.json" };
        own.Click += async (_, _) =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = "Голос Piper (*.onnx)|*.onnx", Title = "Голос Piper: выбери .onnx (рядом должен лежать .onnx.json)" };
            if (d.ShowDialog() != true) return;
            own.IsEnabled = false;
            try
            {
                J.PiperVoice = Voice.ImportPiper(d.FileName);
                J.Tts = "piper";   // an imported voice is meant to be heard, not to sit behind the online one
                Settings.Save();
                await Voice.DownloadPiper(J.PiperVoice, f => Dispatcher.BeginInvoke(() => own.Content = $"Piper: {f * 100:0}%"));
                _ = Voice.Say("Добрый вечер, сэр. Новый голос установлен.");
                App.Current.Navigate("Jarvis");   // rebuild so the lists show the new voice
            }
            catch (Exception e) { Osd.Show("", e.Message); own.Content = "Свой голос (.onnx)…"; own.IsEnabled = true; }
        };
        wp.Children.Add(dl); wp.Children.Add(test); wp.Children.Add(own);
        b.Children.Add(wp);
        return c;
    }

    UIElement ModelsCard()
    {
        var (c, b) = Card("Модели (скачиваются один раз)", "Всё бесплатно и без регистрации. Хранится в %LocalAppData%\\MishkaHelper\\models.");
        var rec = Models.Recommend();
        b.Children.Add(UiKit.Text(rec.Why, 12, true));
        b.Children.Add(AutoSetupButton());
        Combo(b, "Распознавание речи", new[] { ("whisper", "Whisper — точное (рекомендую)"), ("vosk", "Vosk — лёгкое, для слабых ПК") }, J.Engine, async v => { J.Engine = v; Ears.ResetWhisper(); if (J.Enabled) await Jarvis.Restart(); });
        Combo(b, "Модель Whisper", Models.Whisper.Select(w => (w.Key, w.Name)), J.WhisperModel, async v => { J.WhisperModel = v; Ears.ResetWhisper(); if (J.Enabled) await Jarvis.Restart(); });
        foreach (var w in Models.Whisper) Row(b, w, async () => { if (J.Enabled) await Jarvis.Restart(); });
        Row(b, Models.Vosk, async () => { if (J.Enabled) await Jarvis.Restart(); });

        b.Children.Add(new Border { Height = 8 });
        Row(b, LlamaServer.Runtime, () => Task.CompletedTask);
        Switch(b, "ИИ: понимать любые фразы и отвечать на вопросы", J.AiEnabled, v => { J.AiEnabled = v; if (!v) Brain.Unload(); });
        Switch(b, "ИИ на видеокарте (быстрее)", J.AiGpu, v => { J.AiGpu = v; Brain.Unload(); });
        Combo(b, "Нейросеть", Models.Llm.Select(m => (m.Key, m.Name)), J.AiModel, v => { J.AiModel = v; Brain.Unload(); });
        foreach (var m in Models.Llm) Row(b, m, () => Task.CompletedTask);
        return c;
    }

    static void Row(Panel p, ModelInfo m, Func<Task> after)
    {
        var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
        var btn = new Button { Margin = new Thickness(0), Padding = new Thickness(10, 4, 10, 4) };
        var status = UiKit.Text("", 12, true);
        void Refresh()
        {
            bool has = Models.Has(m);
            btn.Content = has ? "Удалить" : "Скачать";
            status.Text = (has ? "✅ " : "") + m.Name;
        }
        btn.Click += async (_, _) =>
        {
            if (Models.Has(m)) { if (UiKit.Confirm($"Удалить {m.Name}?")) { Models.Delete(m); Refresh(); } return; }
            btn.IsEnabled = false;
            try
            {
                await Models.Download(m, f => Application.Current.Dispatcher.BeginInvoke(() => btn.Content = $"{f * 100:0}%"));
                await after();
            }
            catch (Exception e) { Osd.Show("", e.Message); }
            btn.IsEnabled = true;
            Refresh();
        };
        Refresh();
        DockPanel.SetDock(btn, Dock.Right);
        row.Children.Add(btn);
        status.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(status);
        p.Children.Add(row);
    }

    UIElement SitesCard()
    {
        var (c, b) = Card("Мои сайты", "Например расписание в личном кабинете. Нажми «Войти» и войди как обычно — Джарвис запомнит вход и сможет читать страницу. Пароль он не видит.");
        var list = new StackPanel();
        void Render()
        {
            list.Children.Clear();
            foreach (var s in J.Sources.ToList())
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var del = new Button { Content = "", FontFamily = (FontFamily)Application.Current.Resources["IconFont"], Style = S("GhostButton") };
                del.Click += (_, _) => { J.Sources.Remove(s); Settings.Save(); Render(); };
                var login = new Button { Content = "Войти", Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(10, 4, 10, 4) };
                login.Click += (_, _) => WebReader.OpenWindow(s.Url, s.Name);
                var test = new Button { Content = "Проверить", Margin = new Thickness(0, 0, 4, 0), Padding = new Thickness(10, 4, 10, 4) };
                test.Click += async (_, _) =>
                {
                    test.IsEnabled = false;
                    var t = await WebReader.ReadText(s.Url, 400);
                    MessageBox.Show(t == "__LOGIN__" ? "Сайт просит войти — нажми «Войти»." : "Джарвис видит:\n\n" + t, s.Name);
                    test.IsEnabled = true;
                };
                foreach (var x in new[] { del, login, test }) { DockPanel.SetDock(x, Dock.Right); row.Children.Add(x); }
                var info = new StackPanel();
                info.Children.Add(UiKit.Text(s.Name, 13, weight: FontWeights.SemiBold));
                info.Children.Add(UiKit.Text($"{s.Url} · слова: {s.Keywords}", 11, true));
                row.Children.Add(info);
                list.Children.Add(row);
            }
        }
        Render();
        var name = new TextBox { Margin = new Thickness(0, 2, 0, 6) };
        var url = new TextBox { Margin = new Thickness(0, 2, 0, 6) };
        var keys = new TextBox { Margin = new Thickness(0, 2, 0, 6), Text = "пары, расписание, занятия" };
        var add = new Button { Content = "+ Добавить сайт", Style = S("AccentButton"), HorizontalAlignment = HorizontalAlignment.Left };
        add.Click += (_, _) =>
        {
            if (url.Text.Trim().Length < 8) return;
            var u = url.Text.Trim(); if (!u.StartsWith("http")) u = "https://" + u;
            J.Sources.Add(new WebSource { Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : "Сайт", Url = u, Keywords = keys.Text.Trim() });
            Settings.Save(); name.Text = url.Text = ""; Render();
        };
        b.Children.Add(list);
        b.Children.Add(UiKit.Text("Название (например «Расписание»)", 12, true)); b.Children.Add(name);
        b.Children.Add(UiKit.Text("Ссылка на страницу", 12, true)); b.Children.Add(url);
        b.Children.Add(UiKit.Text("Слова, по которым понять, что спрашивают про этот сайт", 12, true)); b.Children.Add(keys);
        b.Children.Add(add);
        return c;
    }

    /// <summary>Downloads what this PC can run (sequentially, with sizes), switches everything on.</summary>
    Button AutoSetupButton()
    {
        var rec = Models.Recommend();
        const string Label = "Настроить Джарвиса под этот ПК";
        var auto = new Button { Content = Label, Style = S("AccentButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 10) };
        auto.Click += async (_, _) =>
        {
            auto.IsEnabled = false;
            try
            {
                var todo = new[] { rec.Stt, LlamaServer.Runtime, rec.Llm }.Where(m => !Models.Has(m)).ToList();
                long totalMb = todo.Sum(m => m.ApproxMb);
                for (int i = 0; i < todo.Count; i++)
                {
                    var m = todo[i];
                    var name = m.Name.Split(" (")[0].Split(" —")[0];
                    // Parallel multi-GB downloads only slow each other down — one by one, and say how much is left.
                    await Models.Download(m, f => Dispatcher.BeginInvoke(() =>
                        auto.Content = $"Скачиваю {i + 1}/{todo.Count}: {name} — {f * 100:0}% из {m.ApproxMb} МБ (всего ≈{totalMb / 1024.0:0.0} ГБ)"));
                }
                J.Engine = rec.Engine;
                if (rec.Stt != Models.Vosk) J.WhisperModel = rec.Stt.Key;
                J.AiModel = rec.Llm.Key; J.AiGpu = rec.Gpu; J.AiEnabled = true; J.Enabled = true;
                Settings.Save();
                Ears.ResetWhisper(); Brain.Unload();
                await Jarvis.Restart();
                Osd.Show("", "Джарвис готов — скажи «Джарвис»");
                App.Current.Navigate("Jarvis");   // rebuild the page so switches show the new values
            }
            catch (Exception e) { Osd.Show("", e.Message); auto.Content = Label; auto.IsEnabled = true; }
        };
        return auto;
    }

    /// <summary>Until speech recognition is downloaded nothing else on this page works — say so at the very top.</summary>
    UIElement? SetupBanner()
    {
        if (Models.Has(Models.WhisperModel) || Models.Has(Models.Vosk)) return null;
        var rec = Models.Recommend();
        var (c, b) = Card("Джарвис ещё не настроен",
            $"Нужно один раз скачать модели (≈{(rec.Stt.ApproxMb + rec.Llm.ApproxMb + LlamaServer.Runtime.ApproxMb) / 1024.0:0.0} ГБ) — бесплатно, без регистрации. Дальше всё работает без интернета.");
        b.Children.Add(UiKit.Text(rec.Why, 12, true));
        b.Children.Add(AutoSetupButton());
        c.BorderBrush = (Brush)FindResource("AccentBrush");
        return c;
    }

    UIElement CommandsCard()
    {
        var (c, b) = Card("Мои команды (без ИИ, мгновенно)",
            "Фраза → действие. Срабатывает сразу, даже если распознало с ошибкой в пару букв. Несколько вариантов фразы — через «;». " +
            "Встроенные команды тоже работают без ИИ: «открой браузер», «закрой Яндекс», «громкость 30», «включи ночной свет», «профиль Игра»…");
        var list = new StackPanel();
        void Render()
        {
            list.Children.Clear();
            foreach (var cmd in J.Commands.ToList())
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var del = new Button { Content = "Убрать", Padding = new Thickness(10, 3, 10, 3) };
                del.Click += (_, _) => { J.Commands.Remove(cmd); Settings.Save(); Render(); };
                DockPanel.SetDock(del, Dock.Right); row.Children.Add(del);
                var info = new StackPanel();
                info.Children.Add(UiKit.Text("«" + cmd.Phrases.Replace(";", "» / «") + "»", 13, weight: FontWeights.SemiBold));
                info.Children.Add(UiKit.Text("→ " + cmd.ActionName + (cmd.Reply.Length > 0 ? $" · ответ: «{cmd.Reply}»" : ""), 11, true));
                row.Children.Add(info);
                list.Children.Add(row);
            }
            if (J.Commands.Count == 0) list.Children.Add(UiKit.Text("Пока нет. Например: «игровой режим; погнали играть» → Профиль: Игра.", 12, true));
        }
        Render();
        b.Children.Add(list);

        b.Children.Add(UiKit.Text("Фраза (можно несколько через ;)", 12, true));
        var phrase = new TextBox { Margin = new Thickness(0, 2, 0, 6), Padding = new Thickness(8, 6, 8, 6) };
        b.Children.Add(UiKit.WithHint(phrase, "игровой режим; погнали играть"));
        b.Children.Add(UiKit.Text("Действие", 12, true));
        var (picker, selected, reset) = UiKit.ActionPicker();
        b.Children.Add(picker);
        b.Children.Add(UiKit.Text("Что ответить (необязательно; пусто — молча)", 12, true));
        var reply = new TextBox { Margin = new Thickness(0, 2, 0, 6), Padding = new Thickness(8, 6, 8, 6) };
        b.Children.Add(UiKit.WithHint(reply, "Поехали!"));
        var add = new Button { Content = "+ Добавить команду", Style = S("AccentButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0) };
        add.Click += (_, _) =>
        {
            if (phrase.Text.Trim().Length < 2 || selected() is not { } cmd) { Osd.Show("", "Напиши фразу и выбери действие в списке"); return; }
            J.Commands.Add(new VoiceCommand { Phrases = phrase.Text.Trim(), Action = cmd.Action, ActionName = cmd.Name, Reply = reply.Text.Trim() });
            Settings.Save();
            phrase.Text = reply.Text = ""; reset();
            Render();
        };
        b.Children.Add(add);
        return c;
    }

    UIElement CalendarCard()
    {
        var (c, b) = Card("Календарь", "Ссылка iCal (.ics): Google Календарь → Настройки → «Секретный адрес в формате iCal», Outlook → «Опубликовать календарь», или экспорт расписания вуза. Потом: «что у меня завтра?»");
        var list = new StackPanel();
        void Render()
        {
            list.Children.Clear();
            foreach (var u in J.Calendars.ToList())
            {
                var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
                var del = new Button { Content = "Удалить", Padding = new Thickness(10, 4, 10, 4) };
                del.Click += (_, _) => { J.Calendars.Remove(u); Settings.Save(); Render(); };
                DockPanel.SetDock(del, Dock.Right); row.Children.Add(del);
                row.Children.Add(UiKit.Text(u.Length > 70 ? u[..70] + "…" : u, 12, true));
                list.Children.Add(row);
            }
        }
        Render();
        var url = new TextBox { Margin = new Thickness(0, 2, 0, 6) };
        var wp = new WrapPanel();
        var add = new Button { Content = "+ Добавить", Style = S("AccentButton") };
        add.Click += (_, _) => { var u = url.Text.Trim(); if (u.Length < 8) return; J.Calendars.Add(u); Settings.Save(); url.Text = ""; Render(); };
        var test = new Button { Content = "Что завтра?" };
        test.Click += async (_, _) => { test.IsEnabled = false; await Core.Calendar.Load(); MessageBox.Show(await Core.Calendar.Say(DateTime.Today.AddDays(1)), "Календарь"); test.IsEnabled = true; };
        wp.Children.Add(add); wp.Children.Add(test);
        b.Children.Add(list); b.Children.Add(url); b.Children.Add(wp);
        return c;
    }

    UIElement NotesCard()
    {
        var (c, b) = Card("Заметки и память", "«Запиши в заметку…», «что я записывал про…», «запомни, что…», «меня зовут…».");
        var list = new StackPanel();
        void Render()
        {
            list.Children.Clear();
            if (J.UserName.Length > 0) list.Children.Add(UiKit.Text($"Имя: {J.UserName}", 13));
            foreach (var m in J.Memory) list.Children.Add(UiKit.Text("🧠 " + m, 12));
            foreach (var n in Notes.All.OrderByDescending(n => n.When).Take(30).ToList())
            {
                var row = new DockPanel();
                var del = new Button { Content = "", FontFamily = (FontFamily)Application.Current.Resources["IconFont"], Style = S("GhostButton") };
                del.Click += (_, _) => { Notes.Remove(n); Render(); };
                DockPanel.SetDock(del, Dock.Right);
                row.Children.Add(del);
                row.Children.Add(UiKit.Text($"{n.When:dd.MM HH:mm}  {n.Text}", 13));
                list.Children.Add(row);
            }
            if (list.Children.Count == 0) list.Children.Add(UiKit.Text("Пока пусто", 12, true));
        }
        Render();
        var wp = new WrapPanel();
        var refresh = new Button { Content = "Обновить" }; refresh.Click += (_, _) => Render();
        var forget = new Button { Content = "Забыть всё обо мне" };
        forget.Click += (_, _) => { if (UiKit.Confirm("Стереть имя и всё, что Джарвис о тебе запомнил? Заметки останутся.")) { J.Memory.Clear(); J.UserName = ""; Settings.Save(); Render(); } };
        wp.Children.Add(refresh); wp.Children.Add(forget);
        b.Children.Add(list); b.Children.Add(wp);
        return c;
    }
}
