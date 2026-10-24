using System.Text.Json.Nodes;

namespace MishkaHelper.Core;

/// <summary>
/// Local LLM agent (Qwen3 in llama.cpp) with native tool calling: PC commands (through the same parser as voice),
/// the user's web pages, notes, diagnostics. Fully offline.
/// </summary>
public static class Brain
{
    static readonly SemaphoreSlim One = new(1, 1);

    public static bool Ready => Settings.Current.Jarvis.AiEnabled && Models.Has(Models.LlmModel) && Models.Has(LlamaServer.Runtime);

    public static void Unload() => LlamaServer.Stop();

    /// <summary>Start loading the model in the background so the first question is fast.</summary>
    public static void Warmup() { if (Ready) _ = Task.Run(async () => { try { await LlamaServer.EnsureRunning(); } catch (Exception e) { Log.Error(e); } }); }

    static readonly System.Globalization.CultureInfo Ru = new("ru-RU");

    static string SystemPrompt()
    {
        var j = Settings.Current.Jarvis;
        var now = DateTime.Now;
        return $"""
            Ты — Джарвис, умный голосовой помощник на компьютере пользователя (программа Mishka Helper). Характер: вежливый, спокойный, с лёгкой иронией, как ИИ-дворецкий.
            {(j.Movie
                ? "Answer ONLY in English, like JARVIS from Iron Man: a calm, witty British AI butler who calls the user \"sir\". 1–3 short sentences, no lists or markdown — it will be spoken. The user speaks Russian; understand it, but reply in English. Tool arguments (pc_command etc.) stay in Russian."
                : "Отвечай по-русски, коротко: 1–3 предложения, без списков и markdown — ответ озвучивается.")}
            Сейчас {now.ToString("dddd, d MMMM yyyy, HH:mm", Ru)}. Завтра — {now.AddDays(1).ToString("dddd, d MMMM", Ru)}.
            Пользователь: {(j.UserName.Length > 0 ? j.UserName : "имя неизвестно")}. Ты о нём помнишь: {(j.Memory.Count > 0 ? string.Join("; ", j.Memory.TakeLast(10)) : "ничего")}.
            Состояние компьютера: {Diagnostics.Snapshot()}.
            Правила:
            - Не обещай действие словами — сразу вызывай инструмент, а потом коротко скажи результат.
            - НИКОГДА не говори «сделал», «включил», «переключил», если не вызвал для этого pc_command в этом ответе. Если только советуешь — так и скажи: «могу переключить, сказать?».
            - Просят что-то сделать на компьютере — pc_command (можно несколько раз подряд, по одной команде).
            - Вопросы о делах и планах пользователя («что у меня…», «что я записывал», «когда сдавать…») — сначала search_notes; если речь про учёбу, пары, расписание, оценки — ещё и calendar и read_site (что есть). Отвечай строго по найденному, не выдумывай.
            - Записать что-то — add_note.
            - «Почему тормозит», «что с компом», «перегрев» — diagnose, затем объясни результат простыми словами.
            - На общие вопросы отвечай сам, коротко.
            """;
    }

    /// <summary>Film mode: turn a short Russian command reply ("Громкость 30.") into JARVIS-style English.</summary>
    public static async Task<string> ToEnglish(string russian)
    {
        try
        {
            var msgs = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = "Rewrite the message in English as JARVIS from Iron Man would say it: one short, calm, polite British sentence, may address the user as \"sir\". Output only the sentence." },
                new JsonObject { ["role"] = "user", ["content"] = russian },
            };
            var m = await LlamaServer.Chat(msgs, maxTokens: 80);
            return (m["content"]?.GetValue<string>() ?? "").Trim().Trim('"') is { Length: > 0 } en ? en : russian;
        }
        catch { return russian; }
    }

    static JsonObject Fn(string name, string desc, JsonObject? props = null, params string[] required) => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = name,
            ["description"] = desc,
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = props ?? new JsonObject(),
                ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()),
            },
        },
    };

    static JsonObject Str(string desc) => new() { ["type"] = "string", ["description"] = desc };

    static JsonArray Tools()
    {
        var sources = Settings.Current.Jarvis.Sources.Select(s => s.Name).ToList();
        var site = Str("Название сайта");
        if (sources.Count > 0) site["enum"] = new JsonArray(sources.Select(s => (JsonNode)s).ToArray());
        var tools = new JsonArray
        {
            Fn("pc_command",
                "Выполнить одну команду на компьютере, сформулированную по-русски. Примеры: «громкость 30», «громче», «включи ночной свет», «выключи wi-fi», " +
                "«режим турбо», «вентиляторы на максимум», «открой discord», «закрой chrome», «профиль Игра», «таймер на 10 минут», " +
                "«напомни через 20 минут выпить воды», «следующий трек», «пауза», «очисти мусор», «найди рецепт блинов», «скриншот», «выключи компьютер».",
                new JsonObject { ["command"] = Str("Команда") }, "command"),
            Fn("add_note", "Записать заметку пользователя.", new JsonObject { ["text"] = Str("Текст заметки") }, "text"),
            Fn("search_notes", "Найти заметки пользователя по теме.", new JsonObject { ["query"] = Str("О чём искать") }, "query"),
            Fn("play_music", "Включить музыку в Яндекс Музыке: исполнителя, песню, альбом, плейлист или жанр. Пустой запрос — «Моя волна».",
                new JsonObject { ["query"] = Str("Что включить, например «Скриптонит» или «Yanix Party»; пусто — Моя волна") }, "query"),
            Fn("diagnose","Проверить компьютер: перегрев, нагрузка, память, диски, режим питания, автозагрузка."),
        };
        if (Calendar.Configured)
            tools.Add(Fn("calendar", "События из календаря пользователя на день.",
                new JsonObject { ["days_from_today"] = new JsonObject { ["type"] = "integer", ["description"] = "0 — сегодня, 1 — завтра, -1 — вчера" } }, "days_from_today"));
        if (sources.Count > 0)
            tools.Add(Fn("read_site", "Открыть сайт пользователя (например личный кабинет с расписанием) и получить текст страницы.",
                new JsonObject { ["name"] = site }, "name"));
        return tools;
    }

    /// <summary>Agent loop: tool calls until the model answers in text (max 6 rounds).</summary>
    public static async Task<Reply> Ask(string question, Action<string>? progress = null, Action<string>? onSentence = null)
    {
        await One.WaitAsync();
        try
        {
            progress?.Invoke("Думаю…");
            var msgs = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = SystemPrompt() } };
            foreach (var l in ChatHistory.Session.TakeLast(9).SkipLast(1))
                msgs.Add(new JsonObject { ["role"] = l.FromUser ? "user" : "assistant", ["content"] = l.Text });
            msgs.Add(new JsonObject { ["role"] = "user", ["content"] = question });
            var tools = Tools();
            bool readWeb = false;   // after a web page is in the context, its text could be giving the orders

            for (int round = 0; round < 6; round++)
            {
                var msg = onSentence != null && Settings.Current.Jarvis.Stream
                    ? await LlamaServer.ChatStream(msgs, tools, onSentence)
                    : await LlamaServer.Chat(msgs, tools);
                var calls = msg["tool_calls"] as JsonArray;
                if (calls == null || calls.Count == 0)
                    return new((msg["content"]?.GetValue<string>() ?? "").Trim() is { Length: > 0 } t ? t : "Готово.");

                msgs.Add(msg);
                foreach (var call in calls)
                {
                    var name = call!["function"]!["name"]!.GetValue<string>();
                    JsonObject args;
                    try { args = JsonNode.Parse(call["function"]!["arguments"]!.GetValue<string>())!.AsObject(); } catch { args = new(); }
                    string Arg(string k) => args[k]?.ToString() ?? "";
                    progress?.Invoke(name switch { "read_site" => $"Открываю «{Arg("name")}»…", "pc_command" => Arg("command"), "search_notes" => "Ищу в заметках…", _ => "Думаю…" });

                    if (name == "read_site") readWeb = true;
                    else if (name == "pc_command" && readWeb)
                    {
                        // Prompt injection guard: a page could say "выключи компьютер". The user confirms any PC action that follows reading a site.
                        var c = Arg("command");
                        return new Reply($"После чтения сайта хочу выполнить «{c}». Выполнить?", async () => { if (await Intents.Try(c) is { Confirm: { } more }) await more(); });
                    }
                    var (result, confirm) = await Tool(name, Arg);
                    if (confirm != null) return confirm;
                    msgs.Add(new JsonObject { ["role"] = "tool", ["tool_call_id"] = call["id"]?.ToString() ?? name, ["content"] = result });
                }
            }
            return new("Что-то я запутался. Попробуй спросить по-другому.");
        }
        catch (Exception e)
        {
            Log.Error(e);
            return new("Нейросеть не ответила: " + e.Message);
        }
        finally { One.Release(); }
    }

    static async Task<(string Result, Reply? Confirm)> Tool(string name, Func<string, string> arg)
    {
        switch (name)
        {
            case "pc_command":
                var r = await Intents.Try(arg("command"));
                if (r == null) return ("Такой команды нет — скажи пользователю, что этого пока не умеешь.", null);
                if (r.Confirm != null) return ("", r);
                return (r.Say.Length > 0 ? r.Say : "Выполнено.", null);
            case "read_site":
                var src = Settings.Current.Jarvis.Sources.FirstOrDefault(s => s.Name == arg("name")) ?? WebReader.Match(arg("name")) ?? Settings.Current.Jarvis.Sources.FirstOrDefault();
                if (src == null) return ("Сайтов не настроено: пусть пользователь добавит его на странице «Джарвис» → «Мои сайты».", null);
                var text = await await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => WebReader.ReadText(src.Url, 6000));
                if (text == "__LOGIN__") return ($"Сайт «{src.Name}» просит войти. Скажи пользователю нажать «Войти» у этого сайта на странице «Джарвис».", null);
                return ($"Текст страницы «{src.Name}»:\n{text}", null);
            case "add_note": Notes.Add(arg("text")); return ("Заметка сохранена.", null);
            case "search_notes":
                var found = Notes.Search(arg("query"));
                return (found.Count == 0 ? "Ничего не найдено." : string.Join("\n", found.Select(n => $"{n.When:dd.MM.yyyy}: {n.Text}")), null);
            case "diagnose": return (Diagnostics.Report(), null);
            case "play_music":
                if (arg("query").Trim().Length == 0) { YandexMusic.MyWave(); return ("Включена «Моя волна».", null); }
                return (await YandexMusic.Play(arg("query")), null);
            case "calendar": return (await Calendar.Say(DateTime.Today.AddDays(int.TryParse(arg("days_from_today"), out var d) ? d : 0)), null);
            default: return ("Нет такого инструмента.", null);
        }
    }
}
