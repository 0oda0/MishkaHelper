using System.Net;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MishkaHelper.Core;

/// <summary>
/// Where game knowledge comes from: Steam (description in Russian), Wikipedia (plot), the game's own wiki (MediaWiki API: bg3.wiki, Fandom…)
/// and PCGamingWiki (known problems and fixes). Read-only public APIs; pages are summarised, not copied.
/// </summary>
public static class GameSources
{
    static readonly HttpClient Http = MakeHttp();
    static HttpClient MakeHttp()
    {
        var h = new HttpClient(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = TimeSpan.FromSeconds(30) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("MishkaHelper/2 (game knowledge base; github.com/0oda0/MishkaHelper)");
        return h;
    }

    static async Task<JsonNode?> Get(string url)
    {
        try { return JsonNode.Parse(await Http.GetStringAsync(url)); }
        catch (Exception e) { Log.Info($"game source {url}: {e.Message}"); return null; }
    }

    static string E(string s) => Uri.EscapeDataString(s);

    // ---------------- Steam ----------------
    public record SteamInfo(int AppId, string Name, string Short, string About, string Genres);

    public static async Task<int> SteamFind(string name)
    {
        var j = await Get($"https://store.steampowered.com/api/storesearch/?term={E(name)}&l=russian&cc=RU");
        return j?["items"]?[0]?["id"]?.GetValue<int>() ?? 0;
    }

    public static async Task<SteamInfo?> Steam(int appId)
    {
        var d = (await Get($"https://store.steampowered.com/api/appdetails?appids={appId}&l=russian"))?[appId.ToString()]?["data"];
        if (d == null) return null;
        string S(string k) => d[k]?.GetValue<string>() ?? "";
        var genres = d["genres"] is JsonArray g ? string.Join(", ", g.Select(x => x?["description"]?.GetValue<string>())) : "";
        return new(appId, S("name"), Plain(S("short_description")), Plain(S("about_the_game")), genres);
    }

    /// <summary>HTML to readable text: block tags become line breaks, the rest is dropped.</summary>
    internal static string Plain(string html)
    {
        var t = Regex.Replace(html, @"<(br|/p|/li|/h\d|/div)[^>]*>", "\n", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"<(script|style)[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, "<[^>]+>", "");
        t = WebUtility.HtmlDecode(t);
        t = Regex.Replace(t, @"\[\s*(edit|править|править код)\s*\]", "", RegexOptions.IgnoreCase);
        t = Regex.Replace(t, @"[ \t ]+", " ");
        return Regex.Replace(t, @"\s*\n\s*(\n\s*)+", "\n").Trim();
    }

    // ---------------- MediaWiki (Wikipedia, the game's wiki, PCGamingWiki) ----------------
    /// <summary>Wikis known to be the main one for a game, by Steam app id; others are looked for on Fandom.</summary>
    static readonly Dictionary<int, string> KnownWikis = new() { [1086940] = "https://bg3.wiki/w/api.php" };

    /// <summary>The game's wiki: a known one, or a Fandom wiki named after the game (Russian first, then English).</summary>
    public static async Task<(string Api, string Lang)?> FindWiki(int appId, string name)
    {
        if (KnownWikis.TryGetValue(appId, out var known)) return (known, "en");
        var core = Regex.Replace(name.Split(new[] { '–', '—', ':', '(' }, 2)[0].ToLowerInvariant(), "[^a-z0-9]", "");
        var slugs = new List<string> { core };
        if (Regex.Replace(core, @"\d+$", "") is { Length: > 2 } noNum && noNum != core) slugs.Add(noNum);
        foreach (var slug in slugs.Where(s => s.Length > 2))
            foreach (var (api, lang) in new[] { ($"https://{slug}.fandom.com/ru/api.php", "ru"), ($"https://{slug}.fandom.com/api.php", "en") })
            {
                var j = await Get(api + "?action=query&meta=siteinfo&siprop=statistics&format=json");
                if (j?["query"]?["statistics"]?["articles"]?.GetValue<int>() is > 20) return (api, lang);
            }
        return null;
    }

    public record Category(string Name, int Pages);

    public static async Task<List<Category>> Categories(string api)
    {
        var res = new List<Category>();
        string? cont = null;
        for (int i = 0; i < 4; i++)
        {
            var j = await Get($"{api}?action=query&list=allcategories&acprop=size&aclimit=500&acmin=3&format=json" + (cont != null ? "&accontinue=" + E(cont) : ""));
            if (j?["query"]?["allcategories"] is not JsonArray a) break;
            foreach (var c in a) res.Add(new(c?["*"]?.GetValue<string>() ?? "", c?["pages"]?.GetValue<int>() ?? 0));
            cont = j["continue"]?["accontinue"]?.GetValue<string>();
            if (cont == null) break;
        }
        return res.Where(c => c.Name.Length > 0).ToList();
    }

    /// <summary>Articles of a category, longest first: on a wiki the long pages are the important ones.</summary>
    public static async Task<List<string>> Members(string api, string category, int max)
    {
        var res = new List<(string Title, int Len)>();
        string? cont = null;
        for (int i = 0; i < 6 && res.Count < 1500; i++)
        {
            var j = await Get($"{api}?action=query&generator=categorymembers&gcmtitle={E("Category:" + category)}&gcmnamespace=0&gcmlimit=500&prop=info&format=json" + (cont != null ? "&gcmcontinue=" + E(cont) : ""));
            if (j?["query"]?["pages"] is JsonObject pages)
                foreach (var (_, p) in pages) res.Add((p?["title"]?.GetValue<string>() ?? "", p?["length"]?.GetValue<int>() ?? 0));
            cont = j?["continue"]?["gcmcontinue"]?.GetValue<string>();
            if (cont == null) break;
        }
        return res.Where(r => r.Title.Length > 0).OrderByDescending(r => r.Len).Take(max).Select(r => r.Title).ToList();
    }

    /// <summary>A page as plain text, cut to max: the extract when the wiki has TextExtracts, else its HTML stripped.</summary>
    public static async Task<string> Text(string api, string title, int max = 2500, bool full = false)
    {
        var j = await Get($"{api}?action=query&prop=extracts&explaintext=1&exsectionformat=wiki&redirects=1&titles={E(title)}&format=json" + (full ? "" : $"&exchars={max}"));
        if (j?["query"]?["pages"] is JsonObject pages && pages.FirstOrDefault().Value?["extract"]?.GetValue<string>() is { Length: > 40 } ex) return full ? ex : Cut(ex, max);
        var h = await Get($"{api}?action=parse&page={E(title)}&prop=text&redirects=1&disablelimitreport=1&format=json");
        var html = h?["parse"]?["text"]?["*"]?.GetValue<string>() ?? "";
        html = Regex.Replace(html, @"<(table|aside|figure)[^>]*class=""[^""]*(infobox|navbox|portable-infobox|toc)[\s\S]*?</\1>", "", RegexOptions.IgnoreCase);
        return Cut(Plain(html), max);
    }

    static string Cut(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    public static async Task<string?> Search(string api, string query)
    {
        var j = await Get($"{api}?action=query&list=search&srsearch={E(query)}&srlimit=1&format=json");
        return j?["query"]?["search"]?[0]?["title"]?.GetValue<string>();
    }

    public static string PageUrl(string api, string title) => api.Replace("api.php", "index.php") + "?title=" + E(title.Replace(' ', '_'));

    // ---------------- Wikipedia: plot ----------------
    /// <summary>The article about the game (Russian first) and its plot section, if it has one.</summary>
    public static async Task<(string Url, string Intro, string Plot)?> Wikipedia(string name)
    {
        foreach (var lang in new[] { "ru", "en" })
        {
            var api = $"https://{lang}.wikipedia.org/w/api.php";
            var title = await Search(api, name + (lang == "ru" ? " компьютерная игра" : " video game"));
            if (title == null || !Similar(title, name)) continue;
            var text = await Text(api, title, full: true);
            if (text.Length < 200) continue;
            var intro = Cut(text.Split("\n==")[0], 1500);
            var plot = Section(text, lang == "ru" ? new[] { "Сюжет", "Синопсис", "Сеттинг" } : new[] { "Plot", "Synopsis", "Story" });
            return ($"https://{lang}.wikipedia.org/wiki/{E(title.Replace(' ', '_'))}", intro, Cut(plot, 9000));
        }
        return null;
    }

    /// <summary>"Baldur’s Gate 3" vs "Baldur's Gate 3": same game. Guards against an article about something else.</summary>
    internal static bool Similar(string a, string b)
    {
        string N(string s) => Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}]", "");
        var x = N(a); var y = N(b.Split(new[] { '–', '—', ':' }, 2)[0]);
        return y.Length > 2 && (x.Contains(y) || y.Contains(x));
    }

    /// <summary>A "== Heading ==" section of an extract with its subsections.</summary>
    internal static string Section(string text, string[] names)
    {
        foreach (var n in names)
        {
            var m = Regex.Match(text, @"(?m)^==\s*" + Regex.Escape(n) + @"[^=\n]*==\s*$");
            if (!m.Success) continue;
            var rest = text[(m.Index + m.Length)..];
            var end = Regex.Match(rest, @"(?m)^==[^=].*==\s*$");
            return (end.Success ? rest[..end.Index] : rest).Trim();
        }
        return "";
    }

    // ---------------- PCGamingWiki: problems ----------------
    public static async Task<(string Url, string Text)?> Problems(string name)
    {
        const string api = "https://www.pcgamingwiki.com/w/api.php";
        var title = await Search(api, name);
        if (title == null || !Similar(title, name)) return null;
        var h = await Get($"{api}?action=parse&page={E(title)}&prop=text&disablelimitreport=1&format=json");
        var html = h?["parse"]?["text"]?["*"]?.GetValue<string>() ?? "";
        // Only the sections about fixes: the rest is settings tables.
        var parts = Regex.Split(html, @"(?=<h2)");
        var keep = parts.Where(p => Regex.IsMatch(p, @"<h2[^>]*>.*?(Issues|Essential improvements)", RegexOptions.IgnoreCase | RegexOptions.Singleline));
        var text = Plain(string.Join("\n", keep));
        return text.Length < 80 ? null : (PageUrl(api, title), Cut(text, 7000));
    }
}
