using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MishkaHelper.Core;

/// <summary>
/// Builds a game's base: sources are read (GameSources), the brain (online or local) writes Russian entries in batches, every step is saved,
/// so a stopped build keeps what it has. Big lists (items) get Russian titles now and their text when opened (Expand).
/// Sizes are chosen for a free key: about 40 requests, well under a day's limit.
/// </summary>
public static class GameBuilder
{
    static readonly HashSet<string> Running = new();
    public static bool IsRunning(string key) { lock (Running) return Running.Contains(key); }

    public static bool BrainReady => Online.Active || LlamaServer.LocalReady;

    /// <summary>How many articles of each kind get a written entry; items get titles only.</summary>
    static readonly (string Kind, int Max)[] Plan = { ("boss", 35), ("location", 40), ("quest", 45), ("character", 30), ("mechanic", 25), ("item", 300) };

    const string System = "Ты — редактор русской базы знаний по компьютерной игре. Пиши только по-русски, кратко и по делу. " +
        "Названия бери из официальной русской локализации игры, если знаешь их; иначе переводи понятно. Опирайся только на данный текст, не выдумывай. " +
        "Отвечай только JSON без пояснений.";

    public static async Task Build(GameKb.Game g)
    {
        if (!BrainReady)
        {
            Toast.Show("", "Игровой помощник", "Для базы нужна нейросеть: подключи бесплатный ключ на странице «Игры» (или скачай локальную модель Джарвиса).",
                () => App.Current.Navigate("Games"));
            return;
        }
        lock (Running) if (!Running.Add(g.Key)) return;
        var b = GameKb.All().FirstOrDefault(x => x.Key == g.Key) ?? new GameBase { Key = g.Key, Name = g.Name, AppId = g.AppId };
        if (g.Exe.Length > 0 && !b.Exe.Contains(g.Exe, StringComparer.OrdinalIgnoreCase)) b.Exe.Add(g.Exe);
        void Step(string what, double done) { b.Status = what; b.Done = done; GameKb.Save(b); Log.Info($"game kb {b.Key}: {what}"); }
        try
        {
            Osd.Show("", $"Собираю базу: {g.Name}");
            // 1. What the game is: Steam's own Russian description.
            Step("Описание из Steam…", 0.02);
            if (b.AppId == 0) b.AppId = await GameSources.SteamFind(g.Name);
            if (b.AppId > 0 && await GameSources.Steam(b.AppId) is { } st)
            {
                b.Name = Regex.Replace(st.Name, @"\s+[–—-]\s+.*$", "").Trim() is { Length: > 1 } n ? n : st.Name;
                Upsert(b, new KbEntry { Kind = "about", Title = "Об игре", TitleEn = st.Name, Text = (st.Short + "\n\n" + st.About).Trim() + (st.Genres.Length > 0 ? "\n\nЖанр: " + st.Genres : ""), Source = $"https://store.steampowered.com/app/{b.AppId}" });
                AddSource(b, $"https://store.steampowered.com/app/{b.AppId}");
            }

            // 2. Stages and the plot by stage: Wikipedia's plot section (spoilers, hidden until reached).
            Step("Сюжет и этапы…", 0.08);
            var wp = await GameSources.Wikipedia(b.Name);
            if (wp is { } w)
            {
                AddSource(b, w.Url);
                if (b.Entries.All(e => e.Kind != "about")) Upsert(b, new KbEntry { Kind = "about", Title = "Об игре", Text = w.Intro, Source = w.Url });
            }
            var wiki = await GameSources.FindWiki(b.AppId, b.Name);
            if (wiki is { } wk) { b.Wiki = wk.Api; AddSource(b, wk.Api.Replace("api.php", "")); }
            // Stages are made anew on every build: an update must not keep an old guess.
            lock (b) { b.Stages.Clear(); b.Entries.RemoveAll(e => e.Kind == "stage"); }
            await Stages(b, wp?.Plot ?? "", wp?.Intro ?? "");
            GameKb.Save(b);

            // 3. The wiki: the brain picks which categories hold bosses, locations…, then writes entries in batches.
            if (wiki is { } wk2)
            {
                Step("Разбираю вики…", 0.15);
                var cats = await GameSources.Categories(wk2.Api);
                var pick = await PickCategories(b, cats);
                double share = 0.75 / Plan.Length, at = 0.18;
                foreach (var (kind, max) in Plan)
                {
                    if (!pick.TryGetValue(kind, out var names) || names.Count == 0) { at += share; continue; }
                    var titles = new List<string>();
                    foreach (var c in names) foreach (var t in await GameSources.Members(wk2.Api, c, max)) if (!titles.Contains(t)) titles.Add(t);
                    titles = titles.Where(t => b.Entries.All(e => e.TitleEn != t) && !Junk.IsMatch(t)).Take(max).ToList();
                    if (kind == "item") await ItemTitles(b, wk2.Api, titles, f => Step($"{GameKb.KindName(kind)}: {f:P0}", at + share * f));
                    else await Articles(b, wk2.Api, kind, titles, f => Step($"{GameKb.KindName(kind)}: {f:P0}", at + share * f));
                    at += share;
                }
            }

            // 4. Known problems and fixes.
            Step("Проблемы и решения…", 0.95);
            if (await GameSources.Problems(b.Name) is { } pr)
            {
                AddSource(b, pr.Url);
                lock (b) b.Entries.RemoveAll(e => e.Kind == "problem");   // reworded on every build: replace, not add
                var arr = await AskJson($"Игра: {b.Name}. Ниже — раздел вики о технических проблемах игры на ПК. Составь список: проблема и как её решить. " +
                    "Верни JSON-массив объектов {\"title\": коротко о проблеме, \"text\": что делать, по шагам, если надо}.\n\n" + pr.Text, 2500);
                foreach (var o in arr.OfType<JsonObject>())
                    if (S(o, "title") is { Length: > 0 } t) Upsert(b, new KbEntry { Kind = "problem", Title = t, Text = S(o, "text"), Source = pr.Url });
            }

            b.Built = DateTime.Now; b.Done = 1;
            b.Status = "";
            GameKb.Save(b);
            Toast.Show("", "База готова", $"«{b.Name}»: записей {b.Entries.Count}. Открывается в игре по {Hotkeys.Pretty(Settings.Current.Hotkeys.GetValueOrDefault("gameKb", "Ctrl+Alt+B"))}.",
                () => GameKbWindow.Open(b, IntPtr.Zero));
        }
        catch (Exception e)
        {
            Log.Error(e);
            b.Status = "Не дособрано: " + e.Message;
            GameKb.Save(b);
            Toast.Show("", "База не дособрана", $"{b.Name}: {e.Message}. Уже собранное сохранено; продолжить можно на странице «Игры».");
        }
        finally { lock (Running) Running.Remove(g.Key); }
    }

    /// <summary>Wiki pages that aren't game content: credits, lists, galleries, patch notes.</summary>
    static readonly Regex Junk = new(@"(?i)\b(credits|cast|list of|gallery|changelog|patch(es)? ?\d|patch notes|version history|modding|soundtrack)\b|/");

    static void AddSource(GameBase b, string url) { if (!b.Sources.Contains(url)) b.Sources.Add(url); }

    static void Upsert(GameBase b, KbEntry e)
    {
        lock (b)
        {
            var old = b.Entries.FindIndex(x => x.Kind == e.Kind && (e.TitleEn.Length > 0 ? x.TitleEn == e.TitleEn : x.Title == e.Title));
            if (old >= 0) { e.Id = b.Entries[old].Id; b.Entries[old] = e; } else b.Entries.Add(e);
        }
    }

    static string S(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<string>(out var s) ? s.Trim() : o[k]?.ToString().Trim() ?? "";
    static int I(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<int>(out var i) ? i : int.TryParse(S(o, k), out var j) ? j : 0;
    static bool B(JsonObject o, string k) => o[k] is JsonValue v && v.TryGetValue<bool>(out var x) ? x : S(o, k) == "true";

    /// <summary>One request; the answer is JSON (array or object), cut out of whatever text surrounds it.</summary>
    static async Task<JsonNode?> Ask(string user, int maxTokens)
    {
        var msgs = new JsonArray(new JsonObject { ["role"] = "system", ["content"] = System }, new JsonObject { ["role"] = "user", ["content"] = user });
        var m = await LlamaServer.Chat(msgs, maxTokens: maxTokens);
        return ParseJson(m["content"]?.GetValue<string>() ?? "");
    }

    internal static JsonNode? ParseJson(string text)
    {
        text = Regex.Replace(text, @"^```(json)?|```$", "", RegexOptions.Multiline).Trim();
        int a = text.IndexOfAny(new[] { '[', '{' });
        if (a < 0) return null;
        int z = text.LastIndexOf(text[a] == '[' ? ']' : '}');
        if (z <= a) return null;
        try { return JsonNode.Parse(text[a..(z + 1)]); } catch { return null; }
    }

    static async Task<JsonArray> AskJson(string user, int maxTokens) => await Ask(user, maxTokens) as JsonArray ?? new JsonArray();

    static string StageList(GameBase b) => b.Stages.Count == 0 ? "этапы неизвестны (ставь 0)" : string.Join("; ", b.Stages.Select(s => $"{s.N} — {s.Name}"));

    /// <summary>The stages of the game (acts, chapters) and what happens in each: plot entries are spoilers of their stage.</summary>
    static async Task Stages(GameBase b, string plot, string intro)
    {
        // Without a real plot text there are no stages: a small model asked anyway invents a story the game doesn't have.
        if (plot.Length < 300) { Log.Info($"game kb {b.Key}: no plot source, no stages"); return; }
        var o = await Ask($"Игра: {b.Name}.\nТекст о сюжете:\n{plot}\n\n" +
            "Раздели игру на этапы так, как они названы в тексте (акты, главы) — от 1 до 8; не придумывай этапов, которых нет в тексте. Пиши только по тексту. Верни JSON-объект: {\"stages\": [{\"n\": 1, \"name\": название этапа, \"summary\": одно предложение без раскрытия сюжета}], " +
            "\"plot\": [{\"stage\": номер, \"title\": заголовок, \"text\": что происходит на этом этапе, 3–6 предложений}]}.", 2500) as JsonObject;
        if (o?["stages"] is JsonArray st)
            foreach (var s in st.OfType<JsonObject>())
                if (I(s, "n") is > 0 and var n && b.Stages.All(x => x.N != n)) b.Stages.Add(new KbStage { N = n, Name = S(s, "name"), Summary = S(s, "summary") });
        b.Stages.Sort((x, y) => x.N.CompareTo(y.N));
        foreach (var s in b.Stages) Upsert(b, new KbEntry { Kind = "stage", Title = $"Этап {s.N}. {s.Name}", Text = s.Summary, Stage = s.N, Spoiler = s.N > 1 });   // a later act's name already tells the story
        if (o?["plot"] is JsonArray pl)
            foreach (var p in pl.OfType<JsonObject>())
                Upsert(b, new KbEntry { Kind = "stage", Title = "Сюжет: " + S(p, "title"), Text = S(p, "text"), Stage = I(p, "stage"), Spoiler = true });
    }

    /// <summary>Which wiki categories hold each kind: names differ on every wiki, so the brain picks from the list.</summary>
    static async Task<Dictionary<string, List<string>>> PickCategories(GameBase b, List<GameSources.Category> cats)
    {
        var list = string.Join("\n", cats.Where(c => c.Pages >= 3 && !Regex.IsMatch(c.Name, "(?i)template|stub|image|file|pages? (with|needing|lacking)|deprecated|disambiguation|articles|шаблон|файл|изображ"))
            .OrderByDescending(c => c.Pages).Take(250).Select(c => $"{c.Name} ({c.Pages})"));
        var o = await Ask($"Игра: {b.Name}. Ниже — категории её вики (в скобках — число статей). Для каждого вида выбери до 3 категорий, где лежат именно такие статьи " +
            "(главные, а не служебные: не «Common items», не «Pages with…»). Если подходящих нет — пустой список.\n" +
            "Виды: boss (боссы), location (локации, места), quest (задания, квесты), character (важные персонажи, спутники), mechanic (механики игры, системы), item (предметы, оружие, броня — самые ценные).\n" +
            "Верни JSON-объект {\"boss\": [...], \"location\": [...], \"quest\": [...], \"character\": [...], \"mechanic\": [...], \"item\": [...]} с точными названиями категорий.\n\n" + list, 900) as JsonObject;
        var res = new Dictionary<string, List<string>>();
        var known = cats.Select(c => c.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var (kind, _) in Plan)
            res[kind] = (o?[kind] as JsonArray)?.Select(x => x?.ToString().Trim() ?? "").Select(n => Regex.Replace(n, @"\s*\(\d+\)$", "")).Where(known.Contains).Take(3).ToList() ?? new();
        Log.Info($"game kb {b.Key}: categories " + string.Join("; ", res.Select(kv => kv.Key + "=" + string.Join("|", kv.Value))));
        return res;
    }

    static readonly Dictionary<string, string> Asks = new()
    {
        ["boss"] = "кто это, где и на каком этапе встречается, и главное — как победить: слабости, опасные приёмы, тактика",
        ["location"] = "что это за место, на каком этапе, что там важного: задания, торговцы, тайники, опасности",
        ["quest"] = "как начать задание, что делать по шагам кратко, важные выборы и к чему они ведут",
        ["character"] = "кто это, роль в сюжете, где встретить, можно ли взять в команду",
        ["mechanic"] = "как это работает в игре и как этим пользоваться с выгодой",
        ["item"] = "что это за предмет, чем полезен, где взять",
    };

    /// <summary>Articles → entries, 4 per request (a free key allows about 8K tokens a minute).</summary>
    static async Task Articles(GameBase b, string api, string kind, List<string> titles, Action<double> progress)
    {
        for (int i = 0; i < titles.Count; i += 4)
        {
            var batch = titles.Skip(i).Take(4).ToList();
            var sb = new StringBuilder();
            for (int k = 0; k < batch.Count; k++) sb.Append($"[{k + 1}] {batch[k]}\n{await GameSources.Text(api, batch[k], 1800)}\n\n");
            var arr = await AskJson($"Игра: {b.Name}. Этапы: {StageList(b)}.\nВид статей: {GameKb.KindName(kind)}. Для каждой статьи напиши по-русски 3–6 предложений: {Asks[kind]}.\n" +
                "Верни JSON-массив объектов {\"i\": номер статьи, \"title\": русское название, \"text\": текст, \"stage\": номер этапа (0 если неясно), \"spoiler\": true, если раскрывает сюжет}.\n\n" + sb, 2600);
            foreach (var o in arr.OfType<JsonObject>())
            {
                int n = I(o, "i") - 1;
                if (n < 0 || n >= batch.Count || S(o, "text").Length < 10) continue;
                Upsert(b, new KbEntry { Kind = kind, Title = S(o, "title") is { Length: > 0 } t ? t : batch[n], TitleEn = batch[n], Text = S(o, "text"), Stage = I(o, "stage"), Spoiler = B(o, "spoiler"), Source = GameSources.PageUrl(api, batch[n]) });
            }
            progress(Math.Min(1, (i + 4.0) / titles.Count));
        }
    }

    /// <summary>Many items: Russian titles in batches of 60; the text comes when one is opened.</summary>
    static async Task ItemTitles(GameBase b, string api, List<string> titles, Action<double> progress)
    {
        for (int i = 0; i < titles.Count; i += 60)
        {
            var batch = titles.Skip(i).Take(60).ToList();
            var arr = await AskJson($"Игра: {b.Name}. Переведи названия предметов на русский (как в официальной локализации, если знаешь). " +
                "Верни JSON-массив строк в том же порядке и того же размера.\n\n" + string.Join("\n", batch.Select((t, k) => $"{k + 1}. {t}")), 2400);
            for (int k = 0; k < batch.Count; k++)
                Upsert(b, new KbEntry { Kind = "item", TitleEn = batch[k], Title = k < arr.Count && arr[k]?.ToString() is { Length: > 0 } ru ? ru : batch[k], Source = GameSources.PageUrl(api, batch[k]) });
            progress(Math.Min(1, (i + 60.0) / titles.Count));
        }
    }

    /// <summary>An entry opened without text yet (items): its page is read and written up now, then kept.</summary>
    public static async Task<KbEntry> Expand(GameBase b, KbEntry e)
    {
        if (e.Text.Length > 0 || b.Wiki.Length == 0 || e.TitleEn.Length == 0) return e;
        var text = await GameSources.Text(b.Wiki, e.TitleEn, 2500);
        var arr = await AskJson($"Игра: {b.Name}. Этапы: {StageList(b)}.\nСтатья вики о предмете «{e.TitleEn}». Напиши по-русски 3–6 предложений: {Asks[e.Kind == "item" ? "item" : "mechanic"]}. " +
            "Верни JSON-массив из одного объекта {\"title\": русское название, \"text\": текст, \"stage\": номер этапа или 0}.\n\n" + text, 900);
        if (arr.FirstOrDefault() is JsonObject o && S(o, "text").Length > 10)
        {
            e.Text = S(o, "text"); e.Stage = I(o, "stage");
            if (S(o, "title") is { Length: > 0 } t) e.Title = t;
            GameKb.Save(b);
        }
        return e;
    }

    /// <summary>"Не нашёл в базе": the wiki is searched for it, and what's found is written up and kept.</summary>
    public static async Task<KbEntry?> Lookup(GameBase b, string query)
    {
        if (b.Wiki.Length == 0) return null;
        var title = await GameSources.Search(b.Wiki, query);
        if (title == null) return null;
        if (b.Entries.FirstOrDefault(x => x.TitleEn == title) is { } have) return await Expand(b, have);
        var text = await GameSources.Text(b.Wiki, title, 3000);
        var arr = await AskJson($"Игра: {b.Name}. Этапы: {StageList(b)}.\nВопрос игрока: «{query}». Статья вики «{title}». Напиши по-русски 4–8 предложений по делу: что это и что игроку важно знать. " +
            "Верни JSON-массив из одного объекта {\"title\": русское название, \"text\": текст, \"stage\": номер этапа или 0, \"spoiler\": true/false}.\n\n" + text, 1200);
        if (arr.FirstOrDefault() is not JsonObject o || S(o, "text").Length < 10) return null;
        var e = new KbEntry { Kind = "found", Title = S(o, "title") is { Length: > 0 } t ? t : title, TitleEn = title, Text = S(o, "text"), Stage = I(o, "stage"), Spoiler = B(o, "spoiler"), Source = GameSources.PageUrl(b.Wiki, title) };
        Upsert(b, e);
        GameKb.Save(b);
        return e;
    }

    /// <summary>A question about the game: answered from the entries that match it, without spoilers past the player's stage.</summary>
    public static async Task<string> Answer(GameBase b, string question)
    {
        var top = Rank(b, question).Take(8).ToList();
        // A general question ("с чего начать?") matches no words: the overview and how the game works are the context then.
        if (top.Count < 3)
            top.AddRange(b.Entries.Where(e => e.Kind is "about" or "mechanic" or "stage" && e.Text.Length > 0 && !top.Contains(e) && !(e.Spoiler && e.Stage > b.Progress)).Take(8 - top.Count));
        var ctx = string.Join("\n\n", top.Select(e => $"[{GameKb.KindName(e.Kind)}{(e.Stage > 0 ? ", этап " + e.Stage : "")}] {e.Title}: {e.Text}"));
        var msgs = new JsonArray(
            new JsonObject { ["role"] = "system", ["content"] = $"Ты помогаешь игроку в игре {b.Name}. Отвечай по-русски, коротко и по делу, только по заметкам из базы. Если в заметках нет ответа — честно скажи, что в базе этого нет, и предложи поискать в окне базы. Не выдумывай. Игрок сейчас на этапе {b.Progress} из {Math.Max(1, b.Stages.Count)}: не раскрывай сюжет более поздних этапов, если он сам не просит." },
            new JsonObject { ["role"] = "user", ["content"] = $"Заметки из базы:\n{ctx}\n\nВопрос: {question}" });
        var m = await LlamaServer.Chat(msgs, maxTokens: 700);
        return m["content"]?.GetValue<string>()?.Trim() is { Length: > 0 } a ? a : "Не знаю.";
    }

    // ---------------- search ----------------
    /// <summary>Entries matching every word (Russian or original title, or text), title hits first. Word endings are cut, so «босса» finds «босс».</summary>
    public static IEnumerable<KbEntry> Search(GameBase b, string query)
    {
        var words = Regex.Split(query.ToLowerInvariant(), @"[^\p{L}\p{N}]+").Where(w => w.Length > 1).Select(Stem).ToList();
        if (words.Count == 0) return b.Entries;
        return b.Entries.Select(e =>
            {
                var t = (e.Title + " " + e.TitleEn).ToLowerInvariant();
                var all = t + " " + e.Text.ToLowerInvariant();
                if (!words.All(all.Contains)) return (e, -1);
                return (e, words.Count(t.Contains) * 10 + (e.Text.Length > 0 ? 1 : 0));
            })
            .Where(x => x.Item2 >= 0).OrderByDescending(x => x.Item2).Select(x => x.e);
    }

    /// <summary>
    /// For questions: entries by how many of its meaningful words they share (a title hit counts most).
    /// «как победить Этель?» must find Этель even though "как" and "победить" aren't in her entry.
    /// </summary>
    internal static IEnumerable<KbEntry> Rank(GameBase b, string question)
    {
        var words = Regex.Split(question.ToLowerInvariant(), @"[^\p{L}\p{N}]+").Where(w => w.Length > 3 && !Stop.Contains(w)).Select(Stem).Distinct().ToList();
        return b.Entries.Select(e =>
            {
                var t = (e.Title + " " + e.TitleEn).ToLowerInvariant();
                var x = e.Text.ToLowerInvariant();
                return (e, s: words.Sum(w => (t.Contains(w) ? 10 : 0) + (x.Contains(w) ? 1 : 0)));
            })
            // Only entries with a text help the answer; on a tie, the kinds a question is usually about come first.
            .Where(r => r.s > 0 && r.e.Text.Length > 0)
            .OrderByDescending(r => r.s).ThenBy(r => Array.IndexOf(AskOrder, r.e.Kind)).Select(r => r.e);
    }

    static readonly string[] AskOrder = { "boss", "quest", "location", "character", "problem", "mechanic", "found", "item", "stage", "about" };

    static readonly HashSet<string> Stop = new() { "какой", "какая", "какие", "каком", "где", "найти", "можно", "нужно", "чтобы", "победить", "убить", "пройти", "сделать", "делать", "есть", "если", "этот", "этой", "игре", "игра" };

    internal static string Stem(string w) => w.Length > 6 ? w[..^2] : w.Length > 4 ? w[..^1] : w;
}
