using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace MishkaHelper.Core;

/// <summary>Orchestrates ears → commands/scenarios/AI → voice, confirmations and proactive tips.</summary>
public static class Jarvis
{
    public static event Action<string, bool>? Message;   // text, fromUser
    public static event Action<string>? Status;
    static Reply? _pending;
    static DispatcherTimer? _tips;
    static bool _hooked;

    public static async Task Start()
    {
        var j = Settings.Current.Jarvis;
        if (!j.Enabled) return;
        if (!_hooked)
        {
            _hooked = true;
            Ears.Heard += t => Application.Current.Dispatcher.BeginInvoke(() => _ = Handle(t));
            Ears.Partial += p => Application.Current.Dispatcher.BeginInvoke(() => Status?.Invoke(p));
            Voice.Finished += OnSpeechFinished;
        }
        if (j.ShowOrb) JarvisOrb.Show();
        Brain.Warmup();
        try { await Ears.Start(); }
        catch (Exception e) { Status?.Invoke(e.Message); Toast.Show("", "Джарвис", e.Message, () => App.Current.Navigate("Jarvis")); }
        _tips ??= new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _tips.Tick -= TipsTick; _tips.Tick += TipsTick;
        _tips.Start();
    }

    public static void Stop()
    {
        Ears.Stop();
        Voice.Stop();
        _tips?.Stop();
        JarvisOrb.Hide();
    }

    public static async Task Restart() { Stop(); await Start(); }

    /// <summary>Hotkey / orb click: start listening for a command right away.</summary>
    public static void Listen()
    {
        if (!Settings.Current.Jarvis.Enabled) { JarvisChat.Open(); return; }
        Voice.Stop();
        Ears.Listen();
        Status?.Invoke("Слушаю…");
    }

    static readonly string[] Yes = { "да", "давай", "конечно", "подтверждаю", "ага", "угу", "выполняй", "согласен", "yes", "ок", "окей" };
    static readonly string[] No = { "нет", "не надо", "отмена", "не", "стоп", "отбой" };

    public static async Task Handle(string text)
    {
        text = text.Trim();
        if (text.Length == 0) return;
        ChatHistory.Add(true, text);
        Message?.Invoke(text, true);

        Reply? r = null;
        bool streamed = false;
        var clean = Ears.Clean(text);
        if (_pending != null)
        {
            var p = _pending;
            _pending = null;
            if (Yes.Any(y => clean == y || clean.StartsWith(y + " "))) { await p.Confirm!(); r = new("Выполняю."); }
            else if (No.Any(n => clean == n || clean.StartsWith(n + " "))) r = new("Хорошо, отменил.");
        }

        try
        {
            if (r == null && Scenarios.ByVoice(text) is { } sc) { _ = Scenarios.Run(sc); r = new($"Запускаю «{sc.Name}»."); }
            r ??= await Compound(text);
            if (r == null && !Brain.Ready && WebReader.Match(text) is { } src)
            {
                Shell.Open(src.Url);
                r = new($"Открыл «{src.Name}». Чтобы я сам читал страницу и рассказывал — включи ИИ на странице «Джарвис».");
            }
            if (r == null)
            {
                if (Brain.Ready)
                {
                    Ears.Thinking(true);
                    Status?.Invoke("Думаю…");
                    // Streamed: the first sentence is spoken while the rest is still being generated.
                    r = await Brain.Ask(text, s => Application.Current.Dispatcher.BeginInvoke(() => Status?.Invoke(s)),
                        sentence => { streamed = true; Voice.Enqueue(sentence); });
                    Ears.Thinking(false);
                }
                else r = new("Не понял. Скажи «что ты умеешь», или включи ИИ на странице «Джарвис» — тогда я пойму любые фразы и вопросы.");
            }
        }
        catch (Exception e) { Log.Error(e); r = new("Ошибка: " + e.Message); }

        if (r.Confirm != null)
        {
            _pending = r;
            Respond(r.Say + " Скажи «да» или «нет».");
            _ = Task.Delay(300).ContinueWith(_ => Application.Current.Dispatcher.BeginInvoke(() => { if (Ears.State != EarState.Off) Ears.Listen(10); }));
            return;
        }
        Respond(r.Say.Length > 0 ? r.Say : "Готово.", speak: r.Say.Length > 0 && !streamed, alreadySpoken: streamed);
    }

    /// <summary>"сделай погромче и включи ночной свет": run every part if all are known commands; otherwise leave it to the AI.</summary>
    static async Task<Reply?> Compound(string text)
    {
        var parts = System.Text.RegularExpressions.Regex.Split(Ears.Clean(text), @"\s+(?:и|а потом|потом|затем|и еще|а еще|плюс)\s+")
            .Where(p => p.Length > 1).ToList();
        if (parts.Count < 2) return await Intents.Try(text);
        // Only split when the first part alone is a command (avoids breaking "открой блокнот и запиши…" for the AI).
        var replies = new List<Reply>();
        foreach (var p in parts)
        {
            var r = await Intents.Try(p);
            if (r == null) return replies.Count == 0 && !Brain.Ready ? await Intents.Try(text) : replies.Count == 0 ? null : Merge(replies, partial: true);
            if (r.Confirm != null) return r;
            replies.Add(r);
        }
        return Merge(replies, partial: false);
    }

    static Reply Merge(List<Reply> rs, bool partial) =>
        new(string.Join(" ", rs.Select(x => x.Say).Where(s => s.Length > 0)) is { Length: > 0 } s ? s : "Готово." + (partial ? " Остальное не понял." : ""));

    static bool _awaitFollowUp;

    /// <summary>Dialog mode: after Jarvis finishes an answer, the next phrase within a few seconds needs no wake word.</summary>
    static void OnSpeechFinished()
    {
        if (!_awaitFollowUp) return;
        _awaitFollowUp = false;
        if (Settings.Current.Jarvis.FollowUp && Ears.State != EarState.Off)
            Application.Current.Dispatcher.BeginInvoke(() => { Ears.Listen(6); Status?.Invoke("Слушаю продолжение…"); });
    }

    static async void Respond(string text, bool speak = true, bool alreadySpoken = false)
    {
        if (speak || alreadySpoken) _awaitFollowUp = true;
        // Film mode: command replies are written in Russian → have them said the JARVIS way, in English.
        if (Settings.Current.Jarvis.Movie && Brain.Ready && text.Any(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я'))
            text = await Brain.ToEnglish(text);
        ChatHistory.Add(false, text);
        Message?.Invoke(text, false);
        Status?.Invoke(text);
        if (speak) _ = Voice.Say(text);
    }

    /// <summary>Jarvis offers something on its own; "да" (voice or click) runs it.</summary>
    public static void Offer(string key, string text, Func<Task> action, int cooldownMin = 60)
    {
        if (!Settings.Current.Jarvis.Proactive) return;
        if (TipsShown.TryGetValue(key, out var t) && (DateTime.Now - t).TotalMinutes < cooldownMin) return;
        TipsShown[key] = DateTime.Now;
        _pending = new Reply(text, action);
        Toast.Show("", "Джарвис", text + " (нажми, чтобы сделать)", () => { _pending = null; _ = action(); });
        if (Settings.Current.Jarvis.Enabled) Respond(text + " Сделать?");
        else { ChatHistory.Add(false, text); Message?.Invoke(text, false); }
    }

    static readonly Dictionary<string, DateTime> TipsShown = new();
    static int _hotTicks;
    static DateTime? _gameSince;

    static void TipsTick(object? s, EventArgs e)
    {
        var st = SysMon.Last;
        _hotTicks = st.CpuTemp >= 92 ? _hotTicks + 1 : 0;
        if (_hotTicks >= 2 && Acer.Available && Acer.Fans != Acer.FanMode.Max)
            Offer("hot", $"Процессор уже {st.CpuTemp}°. Включить вентиляторы на максимум?", () => { Acer.Fans = Acer.FanMode.Max; return Task.CompletedTask; }, 30);

        if (Power.OnAc && Power.Mode == "Saver")
            Offer("saver", "Ноутбук на зарядке, а Windows в режиме «Экономия». Переключить на «Баланс»?", () => { Power.Mode = "Balanced"; return Task.CompletedTask; }, 24 * 60);

        if (st.Ram >= 92)
            Offer("ram", $"Память занята на {st.Ram:0}%. Освободить?", async () => { await Cleaner.BoostAll(); }, 30);

        var b = Power.Battery();
        if (b.Present && !b.OnAc && b.Percent <= 15 && Settings.Current.Profiles.FirstOrDefault(p => p.Name == "Батарея") is { } save)
            Offer("battery", $"Осталось {b.Percent}% заряда. Включить профиль «Батарея»?", () => { Profiles.Apply(save); return Task.CompletedTask; }, 120);

        // Long gaming session → break reminder (games = processes from auto-profile rules).
        var games = Settings.Current.AppRules.Select(r => r.Process).ToList();
        bool playing = games.Count > 0 && games.Any(g => Process.GetProcessesByName(g).Length > 0);
        _gameSince = playing ? _gameSince ?? DateTime.Now : null;
        if (_gameSince is { } since && (DateTime.Now - since).TotalHours >= 2)
            Offer("break", "Ты играешь уже больше двух часов. Может, перерыв на пять минут? Поставить таймер?", async () => { await Intents.Try("таймер на 5 минут"); }, 120);
    }
}
