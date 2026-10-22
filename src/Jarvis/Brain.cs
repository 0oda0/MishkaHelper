using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LLama;
using LLama.Common;
using LLama.Native;
using LLama.Sampling;

namespace MishkaHelper.Core;

/// <summary>
/// Local LLM (Qwen3 via llama.cpp) used as an agent: it answers in JSON and may call tools —
/// PC commands (through the same parser as voice), web sources, notes, system status. Fully offline.
/// </summary>
public static class Brain
{
    static LLamaWeights? _weights;
    static ModelParams? _params;
    static string? _loaded;
    static readonly SemaphoreSlim One = new(1, 1);

    public static bool Ready => Settings.Current.Jarvis.AiEnabled && Models.Has(Models.LlmModel);
    public static bool Loaded => _weights != null;

    public static void Unload()
    {
        _weights?.Dispose(); _weights = null; _loaded = null;
        GC.Collect();
    }

    static async Task EnsureLoaded()
    {
        var m = Models.LlmModel;
        if (_weights != null && _loaded == m.Key) return;
        Unload();
        // LLamaSharp picks the best installed backend (Vulkan → any GPU, else CPU); GpuLayerCount=0 forces CPU.
        _params = new ModelParams(Models.PathOf(m))
        {
            ContextSize = 8192,
            GpuLayerCount = Settings.Current.Jarvis.AiGpu ? 99 : 0,
            Threads = Math.Max(2, Environment.ProcessorCount / 2),
        };
        _weights = await Task.Run(() => LLamaWeights.LoadFromFile(_params));
        _loaded = m.Key;
    }

    static readonly System.Globalization.CultureInfo Ru = new("ru-RU");

    static string SystemPrompt()
    {
        var j = Settings.Current.Jarvis;
        var now = DateTime.Now;
        var tomorrow = now.AddDays(1);
        var sources = j.Sources.Count == 0 ? "нет" : string.Join(", ", j.Sources.Select(s => $"«{s.Name}»"));
        return $$"""
            Ты — Джарвис, голосовой помощник в программе Mishka Helper на компьютере пользователя. Отвечай по-русски, коротко (1–3 предложения), дружелюбно, без markdown — ответ будет озвучен.
            Сейчас {{now.ToString("dddd, d MMMM yyyy, HH:mm", Ru)}}. Завтра — {{tomorrow.ToString("dddd, d MMMM", Ru)}}.
            Пользователь: {{(j.UserName.Length > 0 ? j.UserName : "имя неизвестно")}}. Помнишь о нём: {{(j.Memory.Count > 0 ? string.Join("; ", j.Memory.TakeLast(10)) : "ничего")}}.
            Компьютер сейчас: {{Diagnostics.Snapshot()}}.

            Отвечай ТОЛЬКО одним JSON-объектом.
            Чтобы ответить пользователю: {"say": "текст"}
            Чтобы использовать инструмент: {"tool": "имя", "arg": "аргумент"}
            Инструменты:
            - cmd: команда компьютеру на русском, как сказал бы человек. Примеры: "громкость 30", "включи wi-fi", "режим турбо", "вентиляторы на максимум", "открой discord", "закрой chrome", "профиль Игра", "таймер на 10 минут", "напомни через 20 минут выпить воды", "выключи ночной свет", "следующий трек", "очисти мусор", "найди рецепт блинов".
            - web: прочитать сайт пользователя по имени источника. Доступные источники: {{sources}}. Вернёт текст страницы.
            - note_add: записать заметку (arg — текст).
            - notes: найти в заметках (arg — о чём).
            - diag: подробная проверка компьютера (почему тормозит, перегрев, место на диске).
            После результата инструмента дай финальный ответ через "say" или вызови следующий инструмент.
            Если для ответа нужен сайт (расписание, оценки, задания) — сначала вызови web. Отвечай только по данным из него, не выдумывай.
            Можно выполнить несколько команд подряд, по одной за шаг.
            """;
    }

    public record Turn(string Role, string Text);

    /// <summary>Agent loop: up to 5 tool calls, then a spoken answer.</summary>
    public static async Task<Reply> Ask(string question, Action<string>? progress = null)
    {
        await One.WaitAsync();
        try
        {
            progress?.Invoke("Думаю…");
            await EnsureLoaded();
            var turns = new List<Turn>();
            foreach (var l in ChatHistory.Session.TakeLast(8).SkipLast(1))
                turns.Add(new(l.FromUser ? "user" : "assistant", l.FromUser ? l.Text : JsonSerializer.Serialize(new { say = l.Text })));
            turns.Add(new("user", question));

            for (int step = 0; step < 6; step++)
            {
                var raw = await Complete(turns);
                var (say, tool, arg) = Parse(raw);
                if (tool == null) return new(say ?? raw.Trim());

                turns.Add(new("assistant", raw.Trim()));
                progress?.Invoke(tool switch { "web" => $"Открываю «{arg}»…", "cmd" => arg ?? "", "notes" => "Ищу в заметках…", _ => "Думаю…" });
                var (result, confirm) = await Tool(tool, arg ?? "");
                if (confirm != null) return confirm;
                turns.Add(new("user", "Результат инструмента: " + result));
            }
            return new("Что-то я запутался. Попробуй спросить по-другому.");
        }
        catch (Exception e)
        {
            Log.Error(e);
            return new("Нейросеть не запустилась: " + e.Message);
        }
        finally { One.Release(); }
    }

    static async Task<string> Complete(List<Turn> turns)
    {
        var sb = new StringBuilder();
        sb.Append("<|im_start|>system\n").Append(SystemPrompt()).Append("<|im_end|>\n");
        foreach (var t in turns) sb.Append($"<|im_start|>{t.Role}\n{t.Text}<|im_end|>\n");
        sb.Append("<|im_start|>assistant\n<think>\n\n</think>\n\n");   // Qwen3: skip the long "thinking" part

        var ex = new StatelessExecutor(_weights!, _params!);
        var p = new InferenceParams
        {
            MaxTokens = 400,
            AntiPrompts = new[] { "<|im_end|>", "<|im_start|>" },
            SamplingPipeline = new DefaultSamplingPipeline { Temperature = 0.3f },
        };
        var outp = new StringBuilder();
        await foreach (var tok in ex.InferAsync(sb.ToString(), p)) outp.Append(tok);
        var s = outp.ToString();
        foreach (var stop in p.AntiPrompts) s = s.Replace(stop, "");
        return s;
    }

    static (string? Say, string? Tool, string? Arg) Parse(string raw)
    {
        var m = Regex.Match(raw, @"\{[\s\S]*\}");
        if (!m.Success) return (raw.Trim(), null, null);
        try
        {
            using var d = JsonDocument.Parse(m.Value);
            var r = d.RootElement;
            if (r.TryGetProperty("tool", out var t) && t.GetString() is { Length: > 0 } tool)
                return (null, tool, r.TryGetProperty("arg", out var a) ? a.ValueKind == JsonValueKind.String ? a.GetString() : a.ToString() : "");
            if (r.TryGetProperty("say", out var s)) return (s.GetString(), null, null);
        }
        catch { }
        return (Regex.Replace(raw, @"[{}""]|say\s*:", "").Trim(), null, null);
    }

    static async Task<(string Result, Reply? Confirm)> Tool(string tool, string arg)
    {
        switch (tool)
        {
            case "cmd":
                var r = await Intents.Try(arg);
                if (r == null) return ("Такой команды нет. Скажи пользователю, что этого не умеешь.", null);
                if (r.Confirm != null) return ("", r);
                return (r.Say.Length > 0 ? r.Say : "Выполнено.", null);
            case "web":
                var src = Settings.Current.Jarvis.Sources.FirstOrDefault(s => Ears.Clean(s.Name) == Ears.Clean(arg)) ?? WebReader.Match(arg)
                          ?? Settings.Current.Jarvis.Sources.FirstOrDefault();
                if (src == null) return ("Источников не настроено. Скажи пользователю добавить сайт на странице «Джарвис» → «Мои сайты».", null);
                var text = await System.Windows.Application.Current.Dispatcher.Invoke(() => WebReader.ReadText(src.Url, 5000));
                if (text == "__LOGIN__") return ($"Сайт «{src.Name}» просит войти. Скажи пользователю нажать «Войти» у этого источника на странице «Джарвис».", null);
                return ($"Текст страницы «{src.Name}»:\n{text}", null);
            case "note_add": Notes.Add(arg); return ("Заметка сохранена.", null);
            case "notes":
                var found = Notes.Search(arg);
                return (found.Count == 0 ? "Ничего не найдено." : string.Join("\n", found.Select(n => $"{n.When:dd.MM.yyyy}: {n.Text}")), null);
            case "diag": return (Diagnostics.Report(), null);
            default: return ("Нет такого инструмента.", null);
        }
    }
}
