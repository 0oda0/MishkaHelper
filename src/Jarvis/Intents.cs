using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using NAudio.CoreAudioApi;
using Windows.Devices.Radios;

namespace MishkaHelper.Core;

/// <summary>Result of a command. Confirm != null → dangerous, ask "да/нет" first.</summary>
public record Reply(string Say, Func<Task>? Confirm = null);

/// <summary>Offline Russian command parser. Returns null when the phrase isn't a known command (→ AI).</summary>
public static class Intents
{
    public static readonly string Help =
        "Я умею: громкость и яркость, Wi-Fi, Bluetooth, ночной свет, режимы и вентиляторы, профили и сценарии, " +
        "открывать и закрывать программы и сайты, искать в интернете, таймеры и напоминания, заметки, " +
        "температура и диагностика, очистка и проверка на вирусы, музыка (и Яндекс Музыка: «включи мою волну», «включи исполнителя Yanix»), выключение компьютера. " +
        "С включённым ИИ — отвечаю на любые вопросы и читаю твои сайты, например расписание.";

    static bool M(string t, string pattern, out Match m) => (m = Regex.Match(t, pattern)).Success;
    static bool Has(string t, string pattern) => Regex.IsMatch(t, pattern);

    public static async Task<Reply?> Try(string raw)
    {
        var t = Ears.Clean(raw);
        if (t.Length == 0) return null;
        Match m;

        // --- the user's own phrases come first: instant, no AI ---
        if (MatchCommand(t) is { } uc)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() => CommandPalette.RunAction(uc.Action));
            return new(uc.Reply);
        }

        // --- conversation ---
        if (Has(t, @"^(стоп|хватит|замолчи|отмена|тихо)$")) { Voice.Stop(); return new(""); }
        if (Has(t, @"что ты умеешь|^помощь$|какие команды|список команд")) return new(Help);
        if (Has(t, @"^(привет|здравствуй|здорово|хай|добрый (день|вечер)|доброе утро)")) return new(Greeting());
        if (Has(t, @"^(спасибо|благодарю|спс)")) return new("Обращайся!");
        if (Has(t, @"как (тебя|ты) зовут|кто ты")) return new("Я Джарвис, помощник Mishka Helper. Живу в твоём компьютере и работаю без интернета.");

        // --- memory ---
        if (M(t, @"^меня зовут (\w+)", out m))
        {
            Settings.Current.Jarvis.UserName = Cap(m.Groups[1].Value); Settings.Save();
            return new($"Приятно познакомиться, {Settings.Current.Jarvis.UserName}!");
        }
        if (M(t, @"^запомни(?: что)? (.+)", out m)) { Settings.Current.Jarvis.Memory.Add(m.Groups[1].Value); Settings.Save(); return new("Запомнил."); }
        if (Has(t, @"что ты (обо мне )?(помнишь|знаешь обо мне)"))
        {
            var mem = Settings.Current.Jarvis.Memory;
            var name = Settings.Current.Jarvis.UserName;
            return new((name.Length > 0 ? $"Тебя зовут {name}. " : "") + (mem.Count == 0 ? "Больше пока ничего." : "Помню: " + string.Join("; ", mem.TakeLast(8)) + "."));
        }
        if (M(t, @"^забудь (.+)", out m))
        {
            var q = m.Groups[1].Value;
            int n = q is "все" or "всё" ? Settings.Current.Jarvis.Memory.Count : Settings.Current.Jarvis.Memory.RemoveAll(x => Ears.Clean(x).Contains(q));
            if (q is "все" or "всё") Settings.Current.Jarvis.Memory.Clear();
            Settings.Save();
            return new(n > 0 ? "Забыл." : "Я такого не помню.");
        }

        // --- calendar (only when connected; otherwise the AI may still answer from notes/sites) ---
        if (Calendar.Configured && M(t, @"что у меня (?:(?:на|в) )?(сегодня|завтра|послезавтра)|(?:планы|дела|расписание|пары|календарь) на (сегодня|завтра|послезавтра)", out m))
        {
            var w = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
            return new(await Calendar.Say(DateTime.Today.AddDays(w == "сегодня" ? 0 : w == "завтра" ? 1 : 2)));
        }

        if (Has(t, @"сколько (?:я )?(?:сегодня )?(?:сижу|сидел|просидел|провел|был)? ?за (?:компом|компьютером|ноутом|ноутбуком)|экранное время"))
            return new(ScreenTime.Summary(Has(t, "недел") ? 7 : 1));

        // --- notes ---
        if (M(t, @"^(?:запиши|добавь|сделай|создай|сохрани)(?: в)? замет\w*(?: что)? (.+)", out m) || M(t, @"^запиши(?: что)? (.+)", out m))
        {
            Notes.Add(m.Groups[1].Value);
            return new("Записал в заметки.");
        }
        if (Has(t, @"^(покажи|прочитай|прочти|какие у меня|мои) замет"))
        {
            var last = Notes.All.OrderByDescending(n => n.When).Take(5).ToList();
            return new(last.Count == 0 ? "Заметок пока нет." : "Последние заметки: " + string.Join(". ", last.Select(n => n.Text)) + ".");
        }
        if (M(t, @"(?:что я (?:записывал|записала|записывала|сохранял)|найди в заметках|есть ли в заметках|в заметках)(?: про| о| об)? (.+)", out m))
        {
            var found = Notes.Search(m.Groups[1].Value);
            return new(found.Count == 0 ? "В заметках ничего такого нет." : string.Join(". ", found.Select(n => $"{n.When:d MMMM}: {n.Text}")) + ".");
        }

        // --- sound ---
        if (M(t, @"(?:громкость|звук)(?: на| в)? (.+?)(?: процент\w*)?$", out m) && Number(m.Groups[1].Value) is int vol)
        { Audio.Volume = vol; Osd.Show("", "Громкость", Audio.Volume); return new($"Громкость {Audio.Volume}."); }
        if (Has(t, @"(погромче|громче|прибавь звук|прибавь громкость)")) { Audio.Volume += 10; Osd.Show("", "Громкость", Audio.Volume); return new(""); }
        if (Has(t, @"(потише|тише|убавь звук|убавь громкость)")) { Audio.Volume -= 10; Osd.Show("", "Громкость", Audio.Volume); return new(""); }
        if (Has(t, @"(выключи|отключи|убери) звук|без звука")) { Audio.Muted = true; return new(""); }
        if (Has(t, @"(включи|верни) звук")) { Audio.Muted = false; return new("Звук включён."); }
        if (Has(t, @"(выключи|отключи) микрофон")) { Audio.MicMuted = true; return new("Микрофон выключен."); }
        if (Has(t, @"включи микрофон")) { Audio.MicMuted = false; return new("Микрофон включён."); }
        if (Has(t, @"(переключи звук|звук на наушники|звук на колонки|звук на динамики|следующее устройство)"))
            return new("Звук через " + Audio.NextOutput() + ".");

        // --- screen ---
        if (M(t, @"яркость(?: на| в)? (.+?)(?: процент\w*)?$", out m) && Number(m.Groups[1].Value) is int br)
        { Display.Brightness = br; Osd.Show("", "Яркость", br); return new($"Яркость {br}."); }
        if (Has(t, @"(ярче|прибавь яркость)")) { Display.Brightness = Math.Min(100, Display.Brightness + 15); return new(""); }
        if (Has(t, @"(темнее|убавь яркость)")) { Display.Brightness = Math.Max(0, Display.Brightness - 15); return new(""); }

        // --- toggles ---
        if (M(t, @"(включи|выключи|отключи|вруби|выруби) (вай ?фай|wi ?fi|wifi|интернет|блютуз|bluetooth|блютус|ночной свет|ночной режим|не беспокоить|режим самолета|самолет)", out m))
        {
            bool on = m.Groups[1].Value is "включи" or "вруби";
            var what = m.Groups[2].Value;
            if (what.StartsWith("вай") || what.StartsWith("wi") || what == "интернет") { await Radios.Set(RadioKind.WiFi, on); return new($"Wi-Fi {(on ? "включён" : "выключен")}."); }
            if (what.StartsWith("блю") || what.StartsWith("blue")) { await Radios.Set(RadioKind.Bluetooth, on); return new($"Bluetooth {(on ? "включён" : "выключен")}."); }
            if (what.StartsWith("ночной")) { Display.NightLight = on; return new($"Ночной свет {(on ? "включён" : "выключен")}."); }
            if (what == "не беспокоить") { Tweaks.DoNotDisturb = on; return new($"Режим «не беспокоить» {(on ? "включён" : "выключен")}."); }
            await Radios.SetAirplane(on); return new($"Режим в самолёте {(on ? "включён" : "выключен")}.");
        }

        // --- performance ---
        if (Acer.Available && M(t, @"(тихий|баланс|сбалансированный|производительност\w*|турбо|эко)(?: режим)?$", out m) && Has(t, @"режим|включи|поставь|^турбо|^тихий"))
        {
            var key = m.Groups[1].Value;
            int id = key.StartsWith("тих") ? 0 : key.StartsWith("бал") || key.StartsWith("сбал") ? 1 : key.StartsWith("произв") ? 4 : key == "турбо" ? 5 : 6;
            if (Acer.SupportedModes().All(x => x.Id != id)) return new("Этот режим ноутбук не поддерживает.");
            Acer.Mode = id;
            return new("Режим " + Acer.AllModes.First(x => x.Id == id).Name + ".");
        }
        if (M(t, @"вентилятор\w*(?: на)? (максимум|макс|полную|авто\w*)", out m) && Acer.Available)
        {
            bool max = !m.Groups[1].Value.StartsWith("авто");
            Acer.Fans = max ? Acer.FanMode.Max : Acer.FanMode.Auto;
            return new(max ? "Вентиляторы на максимум." : "Вентиляторы в автоматическом режиме.");
        }

        // --- profiles & scenarios ---
        if (M(t, @"^(?:запусти|включи|выполни|активируй)? ?сценари\w* (.+)", out m) && Scenarios.Find(m.Groups[1].Value) is { } sc)
        { _ = Scenarios.Run(sc); return new($"Запускаю сценарий «{sc.Name}»."); }
        if (M(t, @"(?:профиль|режим)(?: на)? (.+)$", out m) && FindProfile(m.Groups[1].Value) is { } pr)
        { Profiles.Apply(pr); return new($"Профиль «{pr.Name}»."); }

        // --- system info ---
        if (Has(t, @"почему (тормозит|лагает|глючит|медленно|виснет|греется)|что (грузит|тормозит|жрет)|диагностик|проверь (систему|компьютер|ноутбук)$"))
            return new(Diagnostics.Report());
        if (Has(t, @"температур|как (дела|себя чувствует|там)( у)? (комп|ноут|пк|систем)|нагрузк|сколько (памяти|оперативки)|загрузка процессора"))
            return new(Diagnostics.Status());
        if (Has(t, @"заряд|батаре|сколько осталось"))
        {
            var b = Power.Battery();
            var devs = (await Devices.List()).Where(d => d.Connected && d.Battery != null).Select(d => $"{d.Name} {d.Battery}%");
            return new((b.Present ? $"Ноутбук {b.Percent}%{(b.OnAc ? ", на зарядке" : b.Remaining is { } r ? $", хватит примерно на {r.Hours} ч {r.Minutes} мин" : "")}." : "") +
                       (devs.Any() ? " " + string.Join(", ", devs) + "." : ""));
        }
        if (Has(t, @"который час|сколько времени|сколько время")) return new($"Сейчас {DateTime.Now:H:mm}.");
        if (Has(t, @"какое (сегодня )?число|какой (сегодня )?день|какая дата|сегодня какое"))
            return new("Сегодня " + DateTime.Now.ToString("dddd, d MMMM", Ru) + ".");
        if (Has(t, @"погод")) return new(await Widgets.WeatherNow() is { Length: > 0 } w ? w : "Не получилось узнать погоду.");

        // --- timers & reminders ---
        if (M(t, @"таймер(?: на)? (.+?) (секунд\w*|минут\w*|час\w*)", out m) && Number(m.Groups[1].Value) is int tn)
        {
            var span = m.Groups[2].Value.StartsWith("сек") ? TimeSpan.FromSeconds(tn) : m.Groups[2].Value.StartsWith("час") ? TimeSpan.FromHours(tn) : TimeSpan.FromMinutes(tn);
            Later(span, "Таймер", $"Прошло {m.Groups[1].Value} {m.Groups[2].Value}");
            return new($"Таймер на {m.Groups[1].Value} {m.Groups[2].Value}.");
        }
        if (M(t, @"напомни(?: мне)? через (.+?) (минут\w*|час\w*|секунд\w*)(?: что| о том что| чтобы)? (.+)", out m) && Number(m.Groups[1].Value) is int rn)
        {
            var span = m.Groups[2].Value.StartsWith("час") ? TimeSpan.FromHours(rn) : m.Groups[2].Value.StartsWith("сек") ? TimeSpan.FromSeconds(rn) : TimeSpan.FromMinutes(rn);
            Later(span, "Напоминание", m.Groups[3].Value);
            return new($"Напомню через {m.Groups[1].Value} {m.Groups[2].Value}.");
        }
        if (M(t, @"напомни(?: мне)? в (\d{1,2})(?: (\d{2}))?(?: что| о том что| чтобы)? (.+)", out m))
        {
            var at = DateTime.Today.AddHours(int.Parse(m.Groups[1].Value)).AddMinutes(m.Groups[2].Success ? int.Parse(m.Groups[2].Value) : 0);
            if (at < DateTime.Now) at = at.AddDays(1);
            Later(at - DateTime.Now, "Напоминание", m.Groups[3].Value);
            return new($"Напомню в {at:H:mm}.");
        }

        // --- power (confirm first) ---
        if (M(t, @"(выключи|выруби) (компьютер|комп|ноутбук|ноут|пк)(?: через (.+?) минут\w*)?", out m))
        {
            int min = m.Groups[3].Success && Number(m.Groups[3].Value) is int x ? x : 0;
            return new(min > 0 ? $"Выключить компьютер через {min} минут?" : "Точно выключить компьютер?",
                () => { if (min > 0) Toolbox.SchedulePower("shutdown", min); else Shell.Run("shutdown", "/s /t 5"); return Task.CompletedTask; });
        }
        if (Has(t, @"перезагрузи|перезапусти (компьютер|комп|ноутбук|пк)"))
            return new("Перезагрузить компьютер?", () => { Shell.Run("shutdown", "/r /t 5"); return Task.CompletedTask; });
        if (Has(t, @"(спящий режим|усни|в сон|засыпай)"))
            return new("Отправить компьютер в сон?", () => { System.Windows.Forms.Application.SetSuspendState(System.Windows.Forms.PowerState.Suspend, false, false); return Task.CompletedTask; });
        if (Has(t, @"заблокируй|блокировка")) { Toolbox.Lock(); return new(""); }

        // --- maintenance ---
        if (Has(t, @"(очисти|почисти|ускорь|оптимизируй) (мусор|компьютер|комп|ноутбук|систему|память|кэш|временные)|^ускорь"))
        {
            var freed = await Cleaner.BoostAll();
            return new($"Готово, освободил {Cleaner.Size(freed)}.");
        }
        if (Has(t, @"(проверь|просканируй)(?: компьютер)? на вирусы|антивирус|проверка на вирусы"))
        {
            _ = Task.Run(async () =>
            {
                var (threats, _) = await Security.Scan(Security.ScanKind.Quick);
                _ = Voice.Say(threats ? "Внимание! Найдены угрозы. Открой раздел Защита." : "Проверка закончена, угроз нет.");
            });
            return new("Запускаю быструю проверку, это пара минут.");
        }

        // --- media ---
        // Yandex Music: explicit words only ("включи свет" must stay a light, not a song).
        if (Has(t, @"(включи|запусти|поставь|давай) (мою )?волну|^моя волна$")) { YandexMusic.MyWave(); return new("Включаю «Мою волну»."); }
        if (M(t, @"^(?:включи|поставь|запусти|найди) (?:песню|трек|исполнителя|группу|альбом|плейлист|музыку)\s+(.+)$", out m)
            || M(t, @"^(?:включи|поставь|запусти) (.+?) (?:в|на|из) (?:яндекс )?музык[еи]$", out m))
            return new(await YandexMusic.Play(m.Groups[1].Value));
        if (Has(t, @"что (сейчас )?(играет|за песня|за трек)|как называется (эта )?(песня|трек)"))
            return new(await Remote.NowPlaying() is { Length: > 0 } np ? "Сейчас играет: " + np + "." : "Сейчас ничего не играет.");
        if (Has(t, @"^(пауза|поставь на паузу|останови музыку|продолжи|играй|воспроизведи|включи музыку)$")) { Keys.Media(0xB3); return new(""); }
        if (Has(t, @"следующ\w* (трек|песн\w*|композици\w*)|^дальше$|переключи трек")) { Keys.Media(0xB0); return new(""); }
        if (Has(t, @"предыдущ\w* (трек|песн\w*)|^назад$")) { Keys.Media(0xB1); return new(""); }

        // --- windows ---
        if (Has(t, @"скриншот|снимок экрана")) { Actions.Run("screenshot"); return new(""); }
        if (Has(t, @"сверни (все|всё|окна)|покажи рабочий стол")) { Keys.Combo("Win+D"); return new(""); }
        if (Has(t, @"выключи экран|погаси экран")) { Actions.Run("screenOff"); return new(""); }

        // --- web search ---
        if (M(t, @"^(?:найди|загугли|поищи|поиск)(?: в интернете| в гугле| в яндексе)? (.+)", out m) && !Has(t, "в заметках"))
        {
            Shell.Open("https://yandex.ru/search/?text=" + Uri.EscapeDataString(m.Groups[1].Value));
            return new($"Ищу «{m.Groups[1].Value}».");
        }

        // --- Mishka pages ---
        if (M(t, @"^(?:открой|покажи) (настройки|систему|устройства|ускорение|защиту|инструменты|профили|виджеты|горячие клавиши|сценарии|джарвис\w*|главную)(?: мишк\w*)?$", out m))
        {
            var page = m.Groups[1].Value switch
            {
                "настройки" => "Settings", "систему" => "System", "устройства" => "Devices", "ускорение" => "Boost", "защиту" => "Security",
                "инструменты" => "Tools", "профили" => "Profiles", "виджеты" => "Widgets", "горячие клавиши" => "Hotkeys", "сценарии" => "Scenarios",
                "главную" => "Home", _ => "Jarvis",
            };
            System.Windows.Application.Current.Dispatcher.Invoke(() => App.Current.Navigate(page));
            return new("");
        }

        // --- apps & sites ---
        // Verbs as speech recognition actually hears them ("закрою", "открыть", "вруби"…).
        if (M(t, @"^(?:откро\w*|откры\w*|запуст\w*|включи|вруби|покажи|зайди в)(?: мне| пожалуйста)? (?:программу |приложение |сайт )?(.+)", out m))
        {
            var what = m.Groups[1].Value;
            if (Launch(what) is { } name) return new($"Открываю {name}.");
            if (WebReader.Match(what) is { } src) { Shell.Open(src.Url); return new($"Открываю {src.Name}."); }
        }
        if (M(t, @"^(?:закро\w*|закры\w*|выключи|выруби|убей|заверши|останови)(?: мне| пожалуйста)? (?:программу |приложение |окно )?(.+)", out m) && Close(m.Groups[1].Value) is { } closed)
            return new($"Закрыл {closed}.");

        return null;
    }

    static readonly System.Globalization.CultureInfo Ru = new("ru-RU");
    static string Cap(string s) => s.Length == 0 ? s : char.ToUpper(s[0]) + s[1..];

    static string Greeting()
    {
        var h = DateTime.Now.Hour;
        var name = Settings.Current.Jarvis.UserName;
        return (h < 6 ? "Доброй ночи" : h < 12 ? "Доброе утро" : h < 18 ? "Добрый день" : "Добрый вечер") + (name.Length > 0 ? ", " + name : "") + "! Чем помочь?";
    }

    static Profile? FindProfile(string q) =>
        Settings.Current.Profiles.FirstOrDefault(p => Ears.Clean(p.Name) == q) ??
        Settings.Current.Profiles.FirstOrDefault(p => Ears.Distance(Ears.Clean(p.Name), q) <= 2);

    /// <summary>Reminders/timers: toast + voice when due.</summary>
    static void Later(TimeSpan span, string title, string text)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var t = new DispatcherTimer { Interval = span };
            t.Tick += (_, _) =>
            {
                t.Stop();
                Toast.Show("", title, text);
                System.Media.SystemSounds.Exclamation.Play();
                _ = Voice.Say($"{title}: {text}");
            };
            t.Start();
        });
    }

    // ---------- numbers ("тридцать пять" → 35) ----------
    static readonly Dictionary<string, int> Nums = new()
    {
        ["ноль"] = 0, ["один"] = 1, ["одну"] = 1, ["одна"] = 1, ["два"] = 2, ["две"] = 2, ["три"] = 3, ["четыре"] = 4, ["пять"] = 5, ["шесть"] = 6,
        ["семь"] = 7, ["восемь"] = 8, ["девять"] = 9, ["десять"] = 10, ["одиннадцать"] = 11, ["двенадцать"] = 12, ["тринадцать"] = 13,
        ["четырнадцать"] = 14, ["пятнадцать"] = 15, ["шестнадцать"] = 16, ["семнадцать"] = 17, ["восемнадцать"] = 18, ["девятнадцать"] = 19,
        ["двадцать"] = 20, ["тридцать"] = 30, ["сорок"] = 40, ["пятьдесят"] = 50, ["шестьдесят"] = 60, ["семьдесят"] = 70, ["восемьдесят"] = 80,
        ["девяносто"] = 90, ["сто"] = 100, ["полчаса"] = 30, ["максимум"] = 100, ["минимум"] = 0, ["половину"] = 50, ["ноль"] = 0,
    };

    public static int? Number(string s)
    {
        s = s.Trim();
        if (int.TryParse(s.Split(' ')[0], out var d)) return d;
        int sum = 0; bool any = false;
        foreach (var w in s.Split(' '))
            if (Nums.TryGetValue(w, out var v)) { sum += v; any = true; }
            else if (any) break;
        return any ? sum : null;
    }

    // ---------- apps ----------
    static List<(string Name, string Path)>? _startMenu;

    static List<(string Name, string Path)> StartMenu() => _startMenu ??=
        new[] { Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), Environment.GetFolderPath(Environment.SpecialFolder.Programs) }
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.lnk", SearchOption.AllDirectories))
            .Select(f => (Ears.Clean(Path.GetFileNameWithoutExtension(f)), f))
            .Where(x => !x.Item1.Contains("uninstall") && !x.Item1.Contains("удал"))
            .ToList();

    static readonly Dictionary<string, (string Name, string Url)> Sites = new()
    {
        ["ютуб"] = ("YouTube", "https://youtube.com"), ["youtube"] = ("YouTube", "https://youtube.com"),
        ["вк"] = ("ВКонтакте", "https://vk.com"), ["вконтакте"] = ("ВКонтакте", "https://vk.com"),
        ["гугл"] = ("Google", "https://google.com"), ["яндекс"] = ("Яндекс", "https://ya.ru"),
        ["почту"] = ("почту", "https://mail.yandex.ru"), ["почта"] = ("почту", "https://mail.yandex.ru"),
        ["гитхаб"] = ("GitHub", "https://github.com"), ["кинопоиск"] = ("Кинопоиск", "https://kinopoisk.ru"),
        ["твич"] = ("Twitch", "https://twitch.tv"), ["чат гпт"] = ("ChatGPT", "https://chatgpt.com"), ["карты"] = ("Яндекс Карты", "https://yandex.ru/maps"),
        ["переводчик"] = ("переводчик", "https://translate.yandex.ru"),
    };

    static readonly Dictionary<string, string> Aliases = new()
    {
        ["дискорд"] = "discord", ["телеграм"] = "telegram", ["телегу"] = "telegram", ["стим"] = "steam", ["хром"] = "chrome",
        ["браузер"] = "яндекс браузер", ["блокнот"] = "блокнот", ["калькулятор"] = "калькулятор", ["проводник"] = "проводник",
        ["фотошоп"] = "photoshop", ["эксель"] = "excel", ["ворд"] = "word", ["обс"] = "obs", ["спотифай"] = "spotify",
        ["яндекс музыку"] = "яндекс музыка", ["музыку"] = "яндекс музыка", ["диспетчер задач"] = "диспетчер задач", ["терминал"] = "терминал",
        ["пайчарм"] = "pycharm", ["эпик"] = "epic games", ["майнкрафт"] = "minecraft", ["настройки windows"] = "параметры",
    };

    /// <summary>User phrase match: exact, or close enough that a misheard word or two still counts (≤ 20% letters off).</summary>
    public static VoiceCommand? MatchCommand(string clean) =>
        Settings.Current.Jarvis.Commands
            .Select(c => (c, d: c.Phrases.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(ph => Ears.Clean(ph)).Where(ph => ph.Length > 0)
                .Select(ph => (double)Ears.Distance(ph, clean) / Math.Max(ph.Length, clean.Length)).DefaultIfEmpty(1).Min()))
            .Where(x => x.d <= 0.2).OrderBy(x => x.d).Select(x => x.c).FirstOrDefault();

    // ---------- fuzzy names: Cyrillic speech vs Latin program names ----------
    static readonly Dictionary<char, string> Lat = new()
    {
        ['а'] = "a", ['б'] = "b", ['в'] = "v", ['г'] = "g", ['д'] = "d", ['е'] = "e", ['ж'] = "zh", ['з'] = "z", ['и'] = "i", ['й'] = "y",
        ['к'] = "k", ['л'] = "l", ['м'] = "m", ['н'] = "n", ['о'] = "o", ['п'] = "p", ['р'] = "r", ['с'] = "s", ['т'] = "t", ['у'] = "u",
        ['ф'] = "f", ['х'] = "h", ['ц'] = "ts", ['ч'] = "ch", ['ш'] = "sh", ['щ'] = "sch", ['ъ'] = "", ['ы'] = "y", ['ь'] = "", ['э'] = "e",
        ['ю'] = "yu", ['я'] = "ya",
    };

    /// <summary>"дискорд" → "diskord", "яндекс" → "yandeks": lets spoken Russian match Latin names.</summary>
    public static string Translit(string s) => string.Concat(s.Select(c => Lat.TryGetValue(c, out var l) ? l : c.ToString()));

    /// <summary>How well a spoken name fits a program name: 0 = exact … 1 = unrelated. Spelling-tolerant, script-independent.</summary>
    public static double Fit(string spoken, string name)
    {
        if (spoken.Length == 0 || name.Length == 0) return 1;
        string a = Translit(spoken).Replace("ks", "x").Replace(" ", ""), b = Translit(name).Replace("ks", "x").Replace(" ", "");
        if (a == b) return 0;
        if (b.StartsWith(a) && a.Length >= 3) return 0.1;
        if (b.Contains(a) && a.Length >= 4) return 0.2;
        // Same consonants = same word said the Russian way ("стим" ~ steam, "дискорт" ~ discord).
        var sa = Skeleton(a);
        if (sa.Length >= 2 && sa == Skeleton(b)) return 0.15;
        return (double)Ears.Distance(a, b) / Math.Max(a.Length, b.Length);
    }

    static string Skeleton(string s)
    {
        s = s.Replace("ph", "f").Replace("ck", "k").Replace("c", "k").Replace("q", "k").Replace("w", "v").Replace("x", "ks").Replace("ts", "z").TrimEnd('t', 'd') + "|";
        return new string(s.Where(ch => "aeiouy|".IndexOf(ch) < 0).ToArray());
    }

    /// <summary>The browser that opens links (http UserChoice → its command), so "открой браузер" works with any browser.</summary>
    static string? DefaultBrowser()
    {
        try
        {
            using var uc = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\Shell\Associations\UrlAssociations\https\UserChoice");
            if (uc?.GetValue("ProgId") is not string prog) return null;
            using var cmd = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey($@"{prog}\shell\open\command");
            var line = cmd?.GetValue("") as string ?? "";
            var exe = line.StartsWith('"') ? line[1..line.IndexOf('"', 1)] : line.Split(' ')[0];
            return File.Exists(exe) ? exe : null;
        }
        catch { return null; }
    }

    /// <summary>Launchers → Start menu → known sites. Returns a display name or null.</summary>
    public static string? Launch(string q)
    {
        if (q is "браузер" or "браузеры" or "интернет" && DefaultBrowser() is { } browser)
        {
            Shell.Open(browser);
            return "браузер";
        }
        q = Aliases.TryGetValue(q, out var a) ? a : q;
        var ls = Settings.Current.Launchers;
        int li = ls.FindIndex(l => Ears.Clean(l.Name) == q || Ears.Clean(l.Name).Contains(q));
        if (li >= 0) { Actions.Run("launch:" + li); return ls[li].Name; }

        if (q is "проводник") { Shell.Open("explorer.exe"); return "Проводник"; }
        if (q is "диспетчер задач") { Shell.Open("taskmgr.exe"); return "Диспетчер задач"; }
        if (q is "параметры") { Shell.Open("ms-settings:"); return "Параметры"; }

        var sm = StartMenu();
        var hit = sm.FirstOrDefault(x => x.Name == q);
        if (hit.Path == null) hit = sm.Where(x => x.Name.StartsWith(q) || x.Name.Contains(" " + q)).OrderBy(x => x.Name.Length).FirstOrDefault();
        // Misheard or said in Russian ("дискорт", "телеграмм", "обсидиан"): best fuzzy match across scripts.
        if (hit.Path == null && q.Length >= 4)   // short words ("свет", "звук") are too easy to mis-match
        {
            var best = sm.Select(x => (x, f: Math.Min(Fit(q, x.Name), x.Name.Split(' ').Min(w => Fit(q, w) + 0.05)))).OrderBy(x => x.f).FirstOrDefault();
            if (best.x.Path != null && best.f <= 0.3) hit = best.x;
        }
        if (hit.Path != null) { Shell.Open(hit.Path); return Path.GetFileNameWithoutExtension(hit.Path); }

        foreach (var (k, (name, url)) in Sites)
            if (q == k || q.StartsWith(k + " ")) { Shell.Open(url); return name; }
        if (Regex.IsMatch(q, @"^[a-z0-9\-]+ (точка|dot) [a-z]{2,}$")) { var u = "https://" + q.Replace(" точка ", ".").Replace(" dot ", "."); Shell.Open(u); return u; }
        return null;
    }

    /// <summary>Closes the window that best fits the spoken name: process name, product name ("Yandex" for browser.exe) or title.</summary>
    static string? Close(string q)
    {
        q = Aliases.TryGetValue(q, out var a) ? a : q;
        if (q is "браузер" && DefaultBrowser() is { } browser) q = Path.GetFileNameWithoutExtension(browser).ToLowerInvariant();
        var candidates = new List<(Process P, double Fit, string Name)>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.MainWindowHandle == IntPtr.Zero || p.Id == Environment.ProcessId) { p.Dispose(); continue; }
                string desc = "", product = "";
                try { var vi = p.MainModule?.FileVersionInfo; desc = vi?.FileDescription ?? ""; product = vi?.ProductName ?? ""; } catch { }
                var title = Ears.Clean(p.MainWindowTitle);
                double fit = new[] { Ears.Clean(p.ProcessName), Ears.Clean(desc), Ears.Clean(product) }
                    .Where(s => s.Length > 0)
                    .Select(s => Math.Min(Fit(q, s), s.Split(' ').Min(w => Fit(q, w) + 0.05)))
                    .DefaultIfEmpty(1).Min();
                if (title.Contains(q) || Translit(title).Contains(Translit(q))) fit = Math.Min(fit, 0.25);
                candidates.Add((p, fit, desc.Length > 0 ? desc : p.ProcessName));
            }
            catch { p.Dispose(); }
        }
        var best = candidates.OrderBy(c => c.Fit).FirstOrDefault();
        string? closed = null;
        if (best.P != null && best.Fit <= 0.3)
        {
            // Every window of that program (a browser with two windows closes fully).
            foreach (var c in candidates.Where(c => c.P.ProcessName == best.P.ProcessName)) try { c.P.CloseMainWindow(); } catch { }
            closed = best.Name;
        }
        foreach (var c in candidates) c.P.Dispose();
        return closed;
    }
}

/// <summary>Synthetic key presses for scenarios and media commands.</summary>
public static class Keys
{
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    public static void Media(byte vk) { keybd_event(vk, 0, 0, UIntPtr.Zero); keybd_event(vk, 0, 2, UIntPtr.Zero); }

    /// <summary>"Ctrl+Shift+Esc", "Win+D", "Alt+F4"…</summary>
    public static void Combo(string combo)
    {
        var vks = new List<byte>();
        foreach (var part in combo.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            byte vk = part.ToLowerInvariant() switch
            {
                "ctrl" => 0x11, "alt" => 0x12, "shift" => 0x10, "win" => 0x5B,
                _ => Enum.TryParse<System.Windows.Input.Key>(part, true, out var k) ? (byte)System.Windows.Input.KeyInterop.VirtualKeyFromKey(k) : (byte)0,
            };
            if (vk != 0) vks.Add(vk);
        }
        foreach (var v in vks) keybd_event(v, 0, 0, UIntPtr.Zero);
        foreach (var v in Enumerable.Reverse(vks)) keybd_event(v, 0, 2, UIntPtr.Zero);
    }

    /// <summary>Type text into the focused window (Unicode-safe via clipboard-free SendKeys escaping).</summary>
    public static void Type(string text)
    {
        var escaped = Regex.Replace(text, @"[+^%~(){}\[\]]", "{$0}");
        System.Windows.Forms.SendKeys.SendWait(escaped);
    }
}
