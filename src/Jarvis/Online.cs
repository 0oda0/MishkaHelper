using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;

namespace MishkaHelper.Core;

/// <summary>
/// Jarvis's brain in the cloud: any OpenAI-compatible service with the user's own key. The local model stays as the fallback
/// when there's no network or the free limit is used up. The key is stored encrypted and goes only to the chosen address.
/// </summary>
public static class Online
{
    public record Provider(string Key, string Name, string Url, string Model, string KeysPage, string Note);

    /// <summary>Free tiers that call tools well (gpt-oss-120b), checked November 2026. Limits change: the note says what to expect.</summary>
    public static readonly Provider[] Providers =
    {
        new("groq", "Groq (бесплатно, очень быстро — рекомендую)", "https://api.groq.com/openai/v1", "openai/gpt-oss-120b", "https://console.groq.com/keys",
            "Бесплатно: около 1000 запросов в день. Ключ — после входа через Google или почту."),
        new("cerebras", "Cerebras (бесплатно, быстро)", "https://api.cerebras.ai/v1", "gpt-oss-120b", "https://cloud.cerebras.ai/",
            "Бесплатно: около миллиона токенов в день."),
        new("openrouter", "OpenRouter (много моделей)", "https://openrouter.ai/api/v1", "openai/gpt-oss-120b:free", "https://openrouter.ai/keys",
            "Бесплатные модели с «:free» в названии, 50 запросов в день. Список бесплатных моделей меняется."),
        new("custom", "Свой адрес (любой OpenAI-совместимый)", "", "", "", "Например LM Studio или Ollama на другом ПК: http://адрес:порт/v1"),
    };

    static JarvisSettings J => Settings.Current.Jarvis;
    static Provider? P => Providers.FirstOrDefault(p => p.Key == J.OnlineProvider);

    public static string Url => (J.OnlineProvider == "custom" ? J.OnlineUrl : P?.Url ?? "").Trim().TrimEnd('/');
    public static string Model => J.OnlineModel.Trim().Length > 0 ? J.OnlineModel.Trim() : P?.Model ?? "";
    public static bool Active => J.OnlineProvider.Length > 0 && Key.Length > 0 && SafeUrl(Url) && Model.Length > 0;
    public static string LastError { get; private set; } = "";

    public static string Key
    {
        get { try { return J.OnlineKey.Length == 0 ? "" : Secrets.Unprotect(J.OnlineKey); } catch { return ""; } }
        set => J.OnlineKey = value.Trim().Length == 0 ? "" : Secrets.Protect(value.Trim());
    }

    /// <summary>The key travels only over HTTPS; plain HTTP only to this PC or the home network (a local model server).</summary>
    internal static bool SafeUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == "https" || (u.Scheme == "http" && (u.IsLoopback || IsPrivate(u.Host))));

    static bool IsPrivate(string host) => IPAddress.TryParse(host, out var ip) && ip.GetAddressBytes() is var b && b.Length == 4
        && (b[0] == 10 || (b[0] == 192 && b[1] == 168) || (b[0] == 172 && b[1] is >= 16 and <= 31));

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(2) };

    /// <summary>The local server's request turned into the provider's: its model, no llama.cpp-only fields.</summary>
    internal static JsonObject Body(JsonObject local, string model)
    {
        var b = (JsonObject)local.DeepClone();
        b.Remove("chat_template_kwargs");
        b["model"] = model;
        if (model.Contains("gpt-oss")) b["reasoning_effort"] = "low";   // it thinks before answering; "low" keeps Jarvis quick
        return b;
    }

    /// <summary>Sends the request; null means "use the local model instead" (no network, limit reached) when that one is there.</summary>
    public static async Task<HttpResponseMessage?> Send(JsonObject localBody, bool stream, bool localFallback)
    {
        try
        {
            await Vpn.EnsureFor(Url);
            HttpResponseMessage resp;
            string err;
            HttpStatusCode code;
            for (int attempt = 0; ; attempt++)
            {
                var req = new HttpRequestMessage(HttpMethod.Post, Url + "/chat/completions")
                { Content = new StringContent(Body(localBody, Model).ToJsonString(), Encoding.UTF8, "application/json") };
                req.Headers.Authorization = new("Bearer", Key);
                resp = await Http.SendAsync(req, stream ? HttpCompletionOption.ResponseHeadersRead : HttpCompletionOption.ResponseContentRead);
                if (resp.IsSuccessStatusCode) { LastError = ""; return resp; }
                err = await resp.Content.ReadAsStringAsync();
                code = resp.StatusCode;
                resp.Dispose();
                // The per-minute limit says when to come back: a short wait is worth it, the daily one isn't.
                if (code == HttpStatusCode.TooManyRequests && attempt < 4 && RetryAfter(err) is { } wait && wait <= TimeSpan.FromSeconds(90))
                { Log.Info($"online brain: per-minute limit, waiting {wait.TotalSeconds:0} s"); await Task.Delay(wait + TimeSpan.FromSeconds(1)); continue; }
                break;
            }
            LastError = Explain(code, err);
            Log.Info($"online brain {(int)code}: {(err.Length > 300 ? err[..300] : err)}");
            if (localFallback && code is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError) return null;
            throw new InvalidOperationException(LastError);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException)
        {
            LastError = $"Нет связи с {new Uri(Url).Host}. Если сервис не открывается из твоей страны, включи VPN.";
            if (localFallback) return null;
            throw new InvalidOperationException(LastError);
        }
    }

    /// <summary>"Please try again in 12.5s" / "in 1m3.2s" from the provider's limit message.</summary>
    internal static TimeSpan? RetryAfter(string body)
    {
        var m = System.Text.RegularExpressions.Regex.Match(body, @"try again in (?:(\d+)h)?(?:(\d+)m)?(?:([\d.]+)s)?", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success || m.Length < 14) return null;
        double S(int g) => double.TryParse(m.Groups[g].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
        return TimeSpan.FromSeconds(S(1) * 3600 + S(2) * 60 + S(3));
    }

    internal static string Explain(HttpStatusCode code, string body) => code switch
    {
        HttpStatusCode.TooManyRequests when RetryAfter(body) is { } w && w < TimeSpan.FromMinutes(5) => "Сервис просит подождать минуту: слишком много запросов подряд.",
        HttpStatusCode.Unauthorized => "Ключ не подошёл. Скопируй его заново со страницы ключей.",
        HttpStatusCode.Forbidden => "Сервис не пускает: возможно, он недоступен из твоей страны. Включи VPN.",
        HttpStatusCode.TooManyRequests => "Бесплатный лимит на сегодня закончился. Завтра снова заработает.",
        HttpStatusCode.NotFound => "Такой модели у сервиса нет. Проверь название модели.",
        _ when body.Contains("model", StringComparison.OrdinalIgnoreCase) && body.Contains("not", StringComparison.OrdinalIgnoreCase) => "Сервис не знает такую модель. Проверь название.",
        _ => $"Сервис ответил ошибкой {(int)code}.",
    };

    /// <summary>One short question to see that the key, address and model work.</summary>
    public static async Task<string> Test()
    {
        var msgs = new JsonArray(new JsonObject { ["role"] = "user", ["content"] = "Ответь одним словом: работает?" });
        var m = await LlamaServer.Chat(msgs, maxTokens: 400);
        return m["content"]?.GetValue<string>() is { Length: > 0 } t ? t : "(пустой ответ)";
    }
}
