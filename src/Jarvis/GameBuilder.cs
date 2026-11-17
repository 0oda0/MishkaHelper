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
        // The local model needs the video card; next to a running game it gets none (on the processor a build would take hours).
        // So it waits: AfterGame starts it once the game is closed.
        if (!Online.Active && GameKb.Playing())
        {
            lock (Later) { if (Later.Any(x => x.Key == g.Key)) return; Later.Add(g); }
            Toast.Show("\uE7FC", "Игровой помощник", $"«{g.Name}»: соберу, как только закончишь играть — пока идёт игра, видеокарта занята. С бесплатным ключом на странице «Игры» можно прямо во время игры.",
                () => App.Current.Navigate("Games"));
            return;
        }
        lock (Running) if (!Running.Add(g.Key)) return;
        var b = GameKb.All().FirstOrDefault(x => x.Key == g.Key) ?? new GameBase { Key = g.Key, Name = g.Name, AppId = g.AppId };
        if (g.Exe.Length > 0 && !b.Exe.Contains(g.Exe, StringComparer.OrdinalIgnoreCase)) b.Exe.Add(g.Exe);
        var failed = new List<string>();
        void Step(string what, double done) { b.Status = what; b.Done = done; GameKb.Save(b); Log.Info($"game kb {b.Key}: {what}"); }
        bool turn = false;
        try
        {
            // One build at a time: two would share one model and both crawl.
            if (One.CurrentCount == 0) Step("В очереди…", b.Done);
            await One.WaitAsync(); turn = true;
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
            if (wiki is { } wk) { b.Wiki = wk.Api; b.WikiLang = wk.Lang; AddSource(b, wk.Api.Replace("api.php", "")); }
            // Stages are made anew on every build: an update must not keep an old guess.
            await Safe(failed, "этапы", () => Stages(b, wp?.Plot ?? "", wp?.Intro ?? ""));
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
                    var from = at;
                    if (kind == "item") await Safe(failed, kind, () => ItemTitles(b, wk2.Api, titles, f => Step($"{GameKb.KindName(kind)}: {f:P0}", from + share * f)));
                    else await Safe(failed, kind, () => Articles(b, wk2.Api, kind, titles, f => Step($"{GameKb.KindName(kind)}: {f:P0}", from + share * f)));
                    at += share;
                }
            }

            // 3b. The main characters' storylines, stage by stage: where to find them and what to do.
            if (b.Wiki.Length > 0 && b.Stages.Count > 0) await Safe(failed, "линии", () => Arcs(b, f => Step($"{GameKb.KindName("arc")}: {f:P0}", 0.9 + 0.05 * f)));

            // 4. Known problems and fixes.
            Step("Проблемы и решения…", 0.95);
            await Safe(failed, "проблемы", async () =>
            {
            if (await GameSources.Problems(b.Name) is { } pr)
            {
                AddSource(b, pr.Url);
                lock (b) b.Entries.RemoveAll(e => e.Kind == "problem");   // reworded on every build: replace, not add
                var arr = await AskJson($"Игра: {b.Name}. Ниже — раздел вики о технических проблемах игры на ПК. Составь список: проблема и как её решить. " +
                    "Верни JSON-массив объектов {\"title\": коротко о проблеме, \"text\": что делать, по шагам, если надо}.\n\n" + pr.Text, 2500);
                foreach (var o in arr.OfType<JsonObject>())
                    if (S(o, "title") is { Length: > 0 } t) Upsert(b, new KbEntry { Kind = "problem", Title = t, Text = S(o, "text"), Source = pr.Url });
            }
            });

            b.Built = DateTime.Now; b.Done = 1;
            b.Status = failed.Count > 0 ? "Собрано, кроме: " + string.Join(", ", failed) + " — нажми «Обновить», чтобы дособрать" : "";
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
        finally { if (turn) One.Release(); lock (Running) Running.Remove(g.Key); }
    }

    static readonly SemaphoreSlim One = new(1, 1);
    static readonly List<GameKb.Game> Later = new();
    public static bool Waiting(string key) { lock (Later) return Later.Any(g => g.Key == key); }

    /// <summary>Every 10 s: builds put off during a game start when it's over.</summary>
    public static void AfterGame()
    {
        List<GameKb.Game> go;
        lock (Later) { if (Later.Count == 0) return; }
        if (GameKb.Playing()) return;
        lock (Later) { go = Later.ToList(); Later.Clear(); }
        foreach (var g in go) _ = Build(g);
    }

    /// <summary>Wiki pages that aren't game content: credits, lists, galleries, patch notes.</summary>
    static readonly Regex Junk = new(@"(?i)\b(credits|cast|list of|gallery|changelog|patch(es)? ?\d|patch notes|version history|modding|soundtrack)\b|/|^(companions|characters|npcs|bosses|quests|locations|items|weapons|armour|armor)$");

    /// <summary>One step of the build: a failure is logged and the rest goes on (what's done is already saved).</summary>
    static async Task Safe(List<string> failed, string what, Func<Task> step)
    {
        try { await step(); }
        catch (Exception e) { Log.Info($"game kb step '{what}' failed: {e.Message}"); lock (failed) failed.Add(what); }
    }

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
        // Made aside and swapped in only when the answer is good: a failed update keeps the stages it had.
        var stages = new List<KbStage>();
        if (o?["stages"] is JsonArray st)
            foreach (var s in st.OfType<JsonObject>())
                if (I(s, "n") is > 0 and var n && stages.All(x => x.N != n)) stages.Add(new KbStage { N = n, Name = S(s, "name"), Summary = S(s, "summary") });
        if (stages.Count == 0) return;
        stages.Sort((x, y) => x.N.CompareTo(y.N));
        var entries = stages.Select(s => new KbEntry { Kind = "stage", Title = $"Этап {s.N}. {s.Name}", Text = s.Summary, Stage = s.N, Spoiler = s.N > 1 }).ToList();   // a later act's name already tells the story
        if (o?["plot"] is JsonArray pl)
            foreach (var p in pl.OfType<JsonObject>())
                entries.Add(new KbEntry { Kind = "stage", Title = "Сюжет: " + S(p, "title"), Text = S(p, "text"), Stage = I(p, "stage"), Spoiler = true });
        lock (b) { b.Stages = stages; b.Entries.RemoveAll(e => e.Kind == "stage"); b.Entries.AddRange(entries); }
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

    /// <summary>
    /// The storyline of each main character, split by stage: where to find them, what to do, which choices matter.
    /// Who: the wiki's own "Companions" category (longest pages first), else the characters the base has.
    /// From the story sections of the page (a whole page is mostly approval tables). One entry per character and stage, a spoiler of that stage.
    /// </summary>
    internal static Task ArcsFor(GameBase b, List<string> names) => Arcs(b, _ => { }, names);

    static async Task Arcs(GameBase b, Action<double> progress, List<string>? only = null)
    {
        const int MaxCharacters = 12;
        var names = only ?? await GameSources.Members(b.Wiki, b.WikiLang == "ru" ? "Спутники" : "Companions", MaxCharacters);
        if (names.Count < 3) names = b.Entries.Where(e => e.Kind == "character" && e.TitleEn.Length > 0).Select(e => e.TitleEn).Take(MaxCharacters).ToList();
        names = names.Where(n => !Junk.IsMatch(n)).ToList();
        for (int i = 0; i < names.Count; i++)
        {
            var en = names[i];
            progress((double)i / names.Count);
            if (b.Entries.Any(e => e.Kind == "arc" && e.TitleEn == en)) continue;
            var page = await GameSources.Story(b.Wiki, en, 10000);
            if (page.Length < 1500) continue;   // hirelings and extras: no storyline to tell
            var ru = b.Entries.FirstOrDefault(e => e.Kind == "character" && e.TitleEn == en)?.Title;
            var o = await Ask($"Игра: {b.Name}. Этапы игры: {StageList(b)}.\nПерсонаж: {ru ?? en} ({en}). Ниже — его сюжет из вики.\n" +
                "Распиши его сюжетную линию по этапам игры — только те этапы, где с ним что-то происходит. Для каждого шага: что происходит, где его искать (локация), " +
                "что делать игроку по шагам (личные задания, разговоры, кого найти), важные выборы и к чему они ведут. Пиши конкретно, с названиями мест и персонажей, только по тексту.\n" +
                "Верни JSON-объект {\"name\": имя персонажа по-русски, \"steps\": [{\"stage\": номер этапа, \"title\": короткий заголовок шага, \"text\": 4–10 предложений}]}.\n\n" + page, 3000) as JsonObject;
            var who = ru ?? (o != null && S(o, "name") is { Length: > 1 } n ? n : en);
            var src = GameSources.PageUrl(b.Wiki, en);
            foreach (var st in (o?["steps"] as JsonArray ?? new()).OfType<JsonObject>())
            {
                if (S(st, "text").Length < 20) continue;
                int stage = I(st, "stage");
                lock (b) b.Entries.Add(new KbEntry
                {
                    Kind = "arc", Title = $"{who}: {(stage > 0 ? $"этап {stage} — " : "")}{S(st, "title")}", TitleEn = en,
                    Text = S(st, "text"), Stage = stage, Spoiler = stage > 1, Source = src,
                });
            }
            GameKb.Save(b);
        }
        progress(1);
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
        if (await WikiTitle(b, query) is not { } hit) return null;
        if (b.Entries.FirstOrDefault(x => x.TitleEn == hit.Title) is { } have) return await Expand(b, have);
        var text = await GameSources.Text(b.Wiki, hit.Title, 3000);
        var arr = await AskJson($"Игра: {b.Name}. Этапы: {StageList(b)}.\nВопрос игрока: «{query}». Статья вики «{hit.Title}». Напиши по-русски 4–8 предложений по делу: что это и что игроку важно знать. " +
            "Верни JSON-массив из одного объекта {\"title\": русское название, \"text\": текст, \"stage\": номер этапа или 0, \"spoiler\": true/false}.\n\n" + text, 1200);
        if (arr.FirstOrDefault() is not JsonObject o || S(o, "text").Length < 10) return null;
        var e = new KbEntry { Kind = "found", Title = S(o, "title") is { Length: > 0 } t ? t : hit.Ru, TitleEn = hit.Title, Text = S(o, "text"), Stage = I(o, "stage"), Spoiler = B(o, "spoiler"), Source = GameSources.PageUrl(b.Wiki, hit.Title) };
        Upsert(b, e);
        GameKb.Save(b);
        return e;
    }

    /// <summary>
    /// The wiki page a question is about, and its subject in Russian. An English wiki doesn't know «воровское лакомство»:
    /// the brain names the subject and guesses up to three English names (Rogue's Morsel, Thief's Treat…); every guess is searched,
    /// and the page sharing most words with a guess wins.
    /// </summary>
    internal static async Task<(string Title, string Ru)?> WikiTitle(GameBase b, string query)
    {
        if (b.Wiki.Length == 0) return null;
        if (b.WikiLang == "ru" || !Regex.IsMatch(query, "[а-яё]", RegexOptions.IgnoreCase))
            return await GameSources.Search(b.Wiki, query) is { } t ? (t, query) : null;
        var o = await Ask($"Игра: {b.Name}. Вопрос игрока: «{query}».\nО чём именно вопрос (предмет, персонаж, место, задание, загадка…)? Назови это по-русски в именительном падеже " +
            "и дай до трёх вариантов, как это может называться в английской версии игры (официальное название, если знаешь)." +
            "Ещё для каждого значимого слова названия (не больше трёх слов) дай до четырёх английских синонимов: локализаторы переводят вольно, " +
            "например «огненная склянка» → [[\"fire\",\"flame\",\"alchemist\"],[\"flask\",\"bottle\",\"vial\"]]. " +
            "Верни JSON-объект {\"ru\": название по-русски, \"en\": [варианты], \"words\": [[синонимы 1-го слова], [синонимы 2-го слова]]}.", 300) as JsonObject;
        var guesses = (o?["en"] as JsonArray)?.Select(x => x?.ToString().Trim() ?? "").Where(x => x.Length > 1).Take(3).ToList() ?? new();
        // Every combination of the words' synonyms is a guess too: the small local model rarely knows the official name, but it knows synonyms.
        var combos = new List<string> { "" };
        foreach (var syn in (o?["words"] as JsonArray ?? new()).OfType<JsonArray>().Take(3))
        {
            var ws = syn.Select(x => x?.ToString().Trim() ?? "").Where(x => x.Length > 1).Take(4).ToList();
            if (ws.Count > 0) combos = combos.SelectMany(c => ws.Select(w => (c + " " + w).Trim())).Take(24).ToList();
        }
        guesses.AddRange(combos.Where(c => c.Length > 1 && !guesses.Contains(c)));
        var ru = o != null && S(o, "ru") is { Length: > 1 } r ? char.ToUpper(r[0]) + r[1..] : query;
        var cands = new List<string>();
        foreach (var hits in await Task.WhenAll(guesses.Select(g => GameSources.SearchMany(b.Wiki, g, 3))))
            foreach (var t in hits) if (!cands.Contains(t)) cands.Add(t);
        var best = BestTitle(cands, guesses);
        Log.Info($"game kb wiki title for «{query}»: ru={ru}, guesses={string.Join(" | ", guesses)}, found={string.Join(" | ", cands)} → {best}");
        return best == null ? null : (best, ru);
    }

    /// <summary>The candidate sharing most words with any guess (apostrophes and "'s" ignored); the first one on a tie.</summary>
    internal static string? BestTitle(List<string> cands, List<string> guesses)
    {
        static HashSet<string> W(string s) => Regex.Split(Regex.Replace(s.ToLowerInvariant(), "['’]s\\b", ""), @"[^\p{L}\p{N}]+").Where(w => w.Length > 1).ToHashSet();
        var gw = guesses.Select(W).ToList();
        // A page counts only if it has every word of some guess: one shared word ("Thief's Gloves" for "Thief's Treat") is some other thing.
        return cands.Select((c, i) => (c, i, s: gw.Count == 0 ? 0 : gw.Where(g => g.Count > 0 && g.All(W(c).Contains)).Select(g => g.Count).DefaultIfEmpty(-1).Max()))
            .Where(x => x.s > 0 || gw.Count == 0)
            .OrderByDescending(x => x.s).ThenBy(x => gw.Count == 0 ? 0 : W(x.c).Count).ThenBy(x => x.i)   // "Rogue's Morsel" before "Salts of Rogue's Morsel"
            .Select(x => x.c).FirstOrDefault();
    }

    /// <summary>
    /// A question about the game. The thing asked about is found in the base by its title — or on the wiki, and kept —
    /// and the pages it leads to are read too (an ingredient → the salt made of it → the potion), so "what is it for" gets answered.
    /// Names of entries come back as [[links]]; nothing past the player's stage is told.
    /// </summary>
    public static async Task<string> Answer(GameBase b, string question)
    {
        var ranked = Ranked(b, question).ToList();
        var words = Words(question).Count;
        var notes = new List<string>();
        // The subject: an entry whose title has at least half the question's words.
        var main = ranked.FirstOrDefault(r => r.Title * 2 >= Math.Max(1, words)).e;
        string? wikiTitle = null, ru = null;
        if (main == null && await WikiTitle(b, question) is { } hit)
        {
            main = b.Entries.FirstOrDefault(x => x.TitleEn == hit.Title);
            if (main == null) { wikiTitle = hit.Title; ru = hit.Ru; notes.Add($"[Вики] {hit.Title}: {await GameSources.Text(b.Wiki, hit.Title, 2500)}"); }
        }
        if (main is { Text.Length: 0 }) await Expand(b, main);
        // One hop further for things (what an ingredient is made into, what a key opens): their pages, as written on the wiki.
        var page = wikiTitle ?? (main is { Kind: "item" or "found" or "mechanic", TitleEn.Length: > 0 } ? main.TitleEn : null);
        if (page != null && b.Wiki.Length > 0)
        {
            var intro = await GameSources.Text(b.Wiki, page, 900);
            foreach (var l in await GameSources.Linked(b.Wiki, page, intro, 3))
                notes.Add(b.Entries.FirstOrDefault(x => x.TitleEn == l && x.Text.Length > 0) is { } known ? Note(known) : $"[Вики] {l}: {await GameSources.Text(b.Wiki, l, 900)}");
        }
        var top = ranked.Select(r => r.e).Where(e => e.Text.Length > 0 && e != main && !(e.Spoiler && e.Stage > b.Progress)).Take(main == null && notes.Count == 0 ? 8 : 4).ToList();
        if (main != null) top.Insert(0, main);
        // A general question ("с чего начать?") matches no words: the overview and how the game works are the context then.
        if (top.Count + notes.Count < 3)
            top.AddRange(b.Entries.Where(e => e.Kind is "about" or "mechanic" or "stage" && e.Text.Length > 0 && !top.Contains(e) && !(e.Spoiler && e.Stage > b.Progress)).Take(6 - top.Count));
        var ctx = string.Join("\n\n", top.Select(Note).Concat(notes));
        var msgs = new JsonArray(
            new JsonObject { ["role"] = "system", ["content"] = $"Ты помогаешь игроку в игре {b.Name}. Отвечай по-русски, по делу и коротко (до 8 предложений), только по заметкам. Если в заметках нет ответа — одной фразой скажи, что не нашёл. Не выдумывай. " +
                "Разбери по порядку: что это, что делает или зачем нужно, где взять или как сделать (из чего, где крафтится), если это есть в заметках. " +
                "Когда упоминаешь то, о чём есть заметка, ставь ссылку: [[название как в заметке]] или [[название как в заметке|как сказать по-русски]]. Английские названия переводи на русский. " +
                $"Игрок сейчас на этапе {b.Progress} из {Math.Max(1, b.Stages.Count)}: не раскрывай сюжет более поздних этапов, если он сам не просит." },
            new JsonObject { ["role"] = "user", ["content"] = $"Заметки:\n{ctx}\n\nВопрос: {question}" });
        var m = await LlamaServer.Chat(msgs, maxTokens: 500);   // on the processor (during a game) every token is ~0,2 s
        var answer = m["content"]?.GetValue<string>()?.Trim() is { Length: > 0 } a ? a : "Не знаю.";
        // Found on the wiki: kept, so the next question (and the search) find it.
        if (wikiTitle != null && answer.Length > 40)
        {
            Upsert(b, new KbEntry { Kind = "found", Title = ru ?? wikiTitle, TitleEn = wikiTitle, Text = Unlink(answer), Source = GameSources.PageUrl(b.Wiki, wikiTitle) });
            GameKb.Save(b);
        }
        return answer;
    }

    static string Note(KbEntry e) => $"[{GameKb.KindName(e.Kind)}{(e.Stage > 0 ? ", этап " + e.Stage : "")}] {e.Title}{(e.TitleEn.Length > 0 && e.TitleEn != e.Title ? $" ({e.TitleEn})" : "")}: {e.Text}";

    /// <summary>[[A|b]] → b, [[A]] → A: for the phone and the voice.</summary>
    public static string Unlink(string s) => Regex.Replace(s, @"\[\[([^\]|]+)(?:\|([^\]]+))?\]\]", m => m.Groups[2].Success ? m.Groups[2].Value : m.Groups[1].Value);

    /// <summary>An entry a link names: by Russian or original title, spelled loosely.</summary>
    public static KbEntry? Find(GameBase b, string name)
    {
        var n = Norm(name.Trim());
        return b.Entries.FirstOrDefault(e => Norm(e.Title) == n || Norm(e.TitleEn) == n)
            ?? b.Entries.FirstOrDefault(e => n.Length > 4 && (Norm(e.Title).StartsWith(n) || Norm(e.TitleEn).StartsWith(n)));
    }

    // ---------------- search ----------------
    /// <summary>Entries matching every word (Russian or original title, or text), title hits first. Word endings are cut, so «босса» finds «босс».</summary>
    public static IEnumerable<KbEntry> Search(GameBase b, string query)
    {
        var words = Regex.Split(Norm(query), @"[^\p{L}\p{N}]+").Where(w => w.Length > 1).Select(Stem).ToList();
        if (words.Count == 0) return b.Entries;
        return b.Entries.Select(e =>
            {
                var t = Norm(e.Title + " " + e.TitleEn);
                var all = t + " " + Norm(e.Text);
                if (!words.All(all.Contains)) return (e, -1);
                return (e, words.Count(t.Contains) * 10 + (e.Text.Length > 0 ? 1 : 0));
            })
            .Where(x => x.Item2 >= 0).OrderByDescending(x => x.Item2).Select(x => x.e);
    }

    /// <summary>
    /// For questions: entries by how many of its meaningful words they share (a title hit counts most).
    /// «как победить Этель?» must find Этель even though "как" and "победить" aren't in her entry.
    /// </summary>
    internal static IEnumerable<KbEntry> Rank(GameBase b, string question) => Ranked(b, question).Where(r => r.e.Text.Length > 0).Select(r => r.e);

    static List<string> Words(string question) =>
        Regex.Split(question.ToLowerInvariant(), @"[^\p{L}\p{N}]+").Where(w => w.Length > 3 && !Stop.Contains(w)).Select(w => Stem(Norm(w))).Distinct().ToList();

    /// <summary>Entries by shared words (Title = how many are in the title); on a tie, the kinds a question is usually about come first.</summary>
    static IEnumerable<(KbEntry e, int Title)> Ranked(GameBase b, string question)
    {
        var words = Words(question);
        return b.Entries.Select(e =>
            {
                var t = Norm(e.Title + " " + e.TitleEn);
                var x = Norm(e.Text);
                int th = words.Count(t.Contains);
                return (e, th, s: th * 10 + words.Count(x.Contains));
            })
            .Where(r => r.s > 0)
            .OrderByDescending(r => r.s).ThenBy(r => Array.IndexOf(AskOrder, r.e.Kind)).Select(r => (r.e, r.th));
    }

    static readonly string[] AskOrder = { "boss", "quest", "location", "character", "problem", "mechanic", "found", "item", "stage", "about" };

    static readonly HashSet<string> Stop = new() { "какой", "какая", "какие", "каком", "где", "найти", "можно", "нужно", "чтобы", "победить", "убить", "пройти", "сделать", "делать", "есть", "если", "этот", "этой", "игре", "игра",
        "нафига", "нахрена", "зачем", "нужен", "нужна", "нужны", "такое", "такой", "работает", "полезен", "полезна", "куда", "девать", "сделать", "скрафтить", "крафтить", "взять", "достать" };

    /// <summary>
    /// Spellings of the same name made alike: «Касадор» and «Казадор», «Астарион» and «Астарийон», ё and е.
    /// Foreign names are written down by ear in Russian, so search must not depend on which way.
    /// </summary>
    internal static string Norm(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (var ch in s.ToLowerInvariant())
        {
            var c = ch switch { 'ё' => 'е', 'з' => 'с', 'й' => 'и', 'ы' => 'и', 'э' => 'е', _ => ch };
            if (c is 'ь' or 'ъ') continue;
            if (sb.Length > 0 && sb[^1] == c && char.IsLetter(c)) continue;   // doubled letters: «Сзарр» = «Сзар»
            sb.Append(c);
        }
        // «-ийон» / «-ион»: the same ending heard two ways.
        return sb.ToString().Replace("иион", "ион");
    }

    internal static string Stem(string w) => w.Length > 6 ? w[..^2] : w.Length > 4 ? w[..^1] : w;
}
