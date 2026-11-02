using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MishkaHelper.Core;

/// <summary>
/// Support chat over Supabase (REST). Each install signs in anonymously; row-level security in supabase/schema.sql lets it see
/// only its own ticket. Only the public anon key is in the app: it gives nothing without those rules. Nothing is sent until the user writes.
/// </summary>
public static class Support
{
    // Public by design (Supabase "anon" key): it opens nothing without the rules in supabase/schema.sql. The service key never goes here.
    const string Url = "https://xifkllxvzqaudmxuzcgb.supabase.co";
    const string AnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InhpZmtsbHh2enFhdWRteHV6Y2diIiwicm9sZSI6ImFub24iLCJpYXQiOjE3OTM2MTU3NzMsImV4cCI6MjEwOTE5MTc3M30.pZVtbOeCoSDV0ZFp_nAEQ1WRuNDDpq0IKCaLeQtGFls";

    public static bool Configured => Url.Length > 0 && AnonKey.Length > 0;
    public const string IssuesUrl = "https://github.com/0oda0/MishkaHelper/issues/new";

    public record Message(long Id, bool FromAdmin, string Body, DateTime At);

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    static string _access = "", _userId = "", _ticket = "";
    static DateTime _accessUntil;
    static long _lastSeen;
    public static event Action? NewReply;
    static readonly SemaphoreSlim Gate = new(1, 1);

    static HttpRequestMessage Req(HttpMethod m, string path, string? json = null, string? bearer = null)
    {
        var r = new HttpRequestMessage(m, Url + path);
        r.Headers.TryAddWithoutValidation("apikey", AnonKey);
        r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", bearer ?? _access);
        if (json != null) r.Content = new StringContent(json, Encoding.UTF8, "application/json");
        return r;
    }

    static async Task<JsonNode?> Send(HttpRequestMessage r)
    {
        using var resp = await Http.SendAsync(r);
        var text = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException(Friendly((int)resp.StatusCode, text));
        return text.Length > 0 ? JsonNode.Parse(text) : null;
    }

    internal static string Friendly(int code, string body) =>
        body.Contains("rate_limit") || code == 429 ? "Слишком много сообщений подряд. Подожди минуту."
        : body.Contains("too_long") ? "Сообщение слишком длинное."
        : $"Сервер поддержки ответил ошибкой {code}.";

    /// <summary>Anonymous account for this install: the refresh token stays encrypted in the settings, so the chat survives restarts.</summary>
    static async Task Login()
    {
        if (_access.Length > 0 && DateTime.UtcNow < _accessUntil) return;
        JsonNode? j = null;
        var saved = Settings.Current.SupportSession;
        if (saved.Length > 0)
            try
            {
                j = await Send(Req(HttpMethod.Post, "/auth/v1/token?grant_type=refresh_token", new JsonObject { ["refresh_token"] = Secrets.Unprotect(saved) }.ToJsonString(), AnonKey));
            }
            catch (Exception e) { Log.Info("support: session expired, " + e.Message); }
        j ??= await Send(Req(HttpMethod.Post, "/auth/v1/signup", "{\"data\":{}}", AnonKey));
        _access = j!["access_token"]!.GetValue<string>();
        _userId = j["user"]!["id"]!.GetValue<string>();
        _accessUntil = DateTime.UtcNow.AddSeconds(j["expires_in"]?.GetValue<int>() - 60 ?? 3000);
        Settings.Current.SupportSession = Secrets.Protect(j["refresh_token"]!.GetValue<string>());
        Settings.Save();
    }

    static async Task<string> Ticket(bool create)
    {
        if (_ticket.Length > 0) return _ticket;
        var found = await Send(Req(HttpMethod.Get, "/rest/v1/tickets?select=id&order=created_at.desc&limit=1"));
        if (found is JsonArray { Count: > 0 } a) return _ticket = a[0]!["id"]!.GetValue<string>();
        if (!create) return "";
        var r = Req(HttpMethod.Post, "/rest/v1/tickets", new JsonObject { ["app_version"] = Edition.Version.ToString(), ["edition"] = Edition.Name }.ToJsonString());
        r.Headers.TryAddWithoutValidation("Prefer", "return=representation");
        var made = await Send(r);
        return _ticket = made![0]!["id"]!.GetValue<string>();
    }

    public static async Task<List<Message>> Messages()
    {
        if (!Configured) return new();
        await Gate.WaitAsync();
        try
        {
            await Login();
            var t = await Ticket(false);
            if (t.Length == 0) return new();
            var j = await Send(Req(HttpMethod.Get, $"/rest/v1/messages?ticket_id=eq.{t}&select=id,from_admin,body,created_at&order=id.asc&limit=500"));
            var list = j!.AsArray().Select(m => new Message(m!["id"]!.GetValue<long>(), m["from_admin"]!.GetValue<bool>(), m["body"]!.GetValue<string>(), m["created_at"]!.GetValue<DateTime>().ToLocalTime())).ToList();
            if (list.Count > 0) _lastSeen = Math.Max(_lastSeen, list[^1].Id);
            return list;
        }
        finally { Gate.Release(); }
    }

    public static async Task Send(string body)
    {
        await Gate.WaitAsync();
        try
        {
            await Login();
            var t = await Ticket(true);
            await Send(Req(HttpMethod.Post, "/rest/v1/messages", new JsonObject { ["ticket_id"] = t, ["body"] = body }.ToJsonString()));
        }
        finally { Gate.Release(); }
    }

    /// <summary>Every 5 min in the background, only once the user has written: a toast when the developer has answered.</summary>
    public static async Task CheckReplies()
    {
        if (!Configured || Settings.Current.SupportSession.Length == 0) return;
        try
        {
            var before = _lastSeen;
            var list = await Messages();
            var fresh = list.Where(m => m.FromAdmin && m.Id > before).ToList();
            if (before == 0 || fresh.Count == 0) return;   // the first look after a start only sets the mark
            Toast.Show("", "Ответ поддержки", fresh[^1].Body.Length > 160 ? fresh[^1].Body[..160] + "…" : fresh[^1].Body, () => App.Current.Navigate("Support"));
            NewReply?.Invoke();
        }
        catch (Exception e) { Log.Info("support: " + e.Message); }
    }
}
