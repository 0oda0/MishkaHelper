using System.IO;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MishkaHelper.Core;

/// <summary>
/// Reads web pages with an embedded Edge (WebView2) that keeps its own logins. The user signs in once
/// in the visible window; Jarvis later opens the page invisibly and takes its text. Passwords never touch our code.
/// </summary>
public static class WebReader
{
    static readonly string Profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MishkaHelper", "web");
    static CoreWebView2Environment? _env;

    static async Task<CoreWebView2Environment> Env() => _env ??= await CoreWebView2Environment.CreateAsync(null, Profile);

    /// <summary>Visible browser window for logging in or just browsing.</summary>
    public static async void OpenWindow(string url, string title = "Вход")
    {
        var view = new WebView2();
        var w = new Window
        {
            Title = $"Mishka Helper — {title}", Width = 1100, Height = 760, Content = view,
            WindowStartupLocation = WindowStartupLocation.CenterScreen, Background = (System.Windows.Media.Brush)Application.Current.Resources["BgBrush"],
        };
        w.SourceInitialized += (_, _) => ThemeManager.ApplyBackdrop(w, false);
        w.Show();
        Native.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(w).Handle);   // asked for from a tray app or a phone: must not open behind everything
        w.Activate();
        await view.EnsureCoreWebView2Async(await Env());
        view.CoreWebView2.Navigate(url);
    }

    /// <summary>Load the page off-screen, wait for scripts, return visible text (trimmed).</summary>
    public static async Task<string> ReadText(string url, int maxChars = 7000)
    {
        var view = new WebView2();
        var w = new Window
        {
            Content = view, Width = 1200, Height = 900, Left = -20000, Top = -20000,
            ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None,
        };
        try
        {
            w.Show();
            await view.EnsureCoreWebView2Async(await Env());
            var loaded = new TaskCompletionSource<bool>();
            view.CoreWebView2.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
            view.CoreWebView2.Navigate(url);
            await Task.WhenAny(loaded.Task, Task.Delay(20000));
            await Task.Delay(2500);   // SPA dashboards render after load
            var json = await view.CoreWebView2.ExecuteScriptAsync("document.body ? document.body.innerText : ''");
            var text = JsonSerializer.Deserialize<string>(json) ?? "";
            var finalUrl = view.CoreWebView2.Source;
            text = string.Join('\n', text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));
            if (LooksLikeLogin(text, finalUrl)) return "__LOGIN__";
            return text.Length > maxChars ? text[..maxChars] : text;
        }
        finally { w.Close(); }
    }

    public record Read(bool Login, List<string> Texts, bool Failed = false);

    // Finds the shortest visible clickable thing with this text. Only tabs, buttons and links on the page the user chose; nothing is typed or sent.
    const string ClickScript = @"(words => {
      const bad = /выйти|выход|удалить|отправить|сохранить|оплат|logout|sign out|delete|submit/i;
      const seen = el => { const r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; };
      const all = [...document.querySelectorAll('a,button,[role=tab],[role=button],li,td,th,label,.tab,[class*=tab],[class*=day]')].filter(seen);
      for (const w of words) {
        const hit = all.filter(e => { const t = (e.innerText || '').trim().toLowerCase(); return t.length > 0 && t.length < 40 && t.includes(w) && !bad.test(t); })
          .sort((a, b) => a.innerText.length - b.innerText.length)[0];
        if (hit) { hit.click(); return w; }
      }
      return '';
    })";

    const string NextScript = @"(() => {
      const seen = el => { const r = el.getBoundingClientRect(); return r.width > 0 && r.height > 0; };
      const hit = [...document.querySelectorAll('a,button,[role=button]')].filter(seen).find(e => /след|вперёд|вперед|next|›|»|→|>/i.test((e.innerText || e.getAttribute('aria-label') || e.title || '').trim()) && (e.innerText || '').trim().length < 30);
      if (hit) { hit.click(); return true; }
      return false;
    })()";

    /// <summary>
    /// Reads a site that draws itself: waits until the text stops changing, then, if asked, presses "next week" and the tab of the wanted day.
    /// Returns every state it saw, for the caller to look for the day in. Stays on the page the user remembered.
    /// </summary>
    public static async Task<Read> ReadWith(string url, string[] clickWords, bool sameWeek)
    {
        var view = new WebView2();
        var w = new Window { Content = view, Width = 1400, Height = 1000, Left = -20000, Top = -20000, ShowInTaskbar = false, ShowActivated = false, WindowStyle = WindowStyle.None };
        var texts = new List<string>();
        try
        {
            w.Show();
            await view.EnsureCoreWebView2Async(await Env());
            var core = view.CoreWebView2;
            var host = new Uri(url).Host;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            var loaded = new TaskCompletionSource<bool>();
            core.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
            core.Navigate(url);
            await Task.WhenAny(loaded.Task, Task.Delay(25000));
            if (loaded.Task.IsCompleted && !loaded.Task.Result) return new(false, texts, true);
            async Task<string> Text() => Tidy(JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync("document.body ? document.body.innerText : ''")) ?? "");
            async Task<string> Settled()
            {
                string last = "";
                for (int i = 0; i < 12; i++)
                {
                    await Task.Delay(1000);
                    var t = await Text();
                    if (t.Length > 40 && t == last) return t;
                    last = t;
                }
                return last;
            }
            var first = await Settled();
            if (LooksLikeLogin(first, core.Source)) return new(true, texts);
            texts.Add(first);
            if (!sameWeek)
            {
                await core.ExecuteScriptAsync(NextScript);
                texts.Add(await Settled());
            }
            var pressed = JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync(ClickScript + "(" + JsonSerializer.Serialize(clickWords) + ")"));
            if (!string.IsNullOrEmpty(pressed)) texts.Add(await Settled());
            texts.Reverse();   // the latest state is the most specific
            return new(false, texts);
        }
        finally { w.Close(); }
    }

    static string Tidy(string text) => string.Join('\n', text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

    static bool LooksLikeLogin(string text, string url) =>
        text.Length < 2500 && (url.Contains("login", StringComparison.OrdinalIgnoreCase) || url.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
                               text.Contains("пароль", StringComparison.OrdinalIgnoreCase) && text.Contains("вход", StringComparison.OrdinalIgnoreCase));

    public static WebSource? Match(string question)
    {
        var q = RuText.Clean(question);
        return Settings.Current.Jarvis.Sources.FirstOrDefault(s =>
            (s.Keywords + "," + s.Name).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(k => k.Length > 2 && q.Contains(RuText.Clean(k).Length > 5 ? RuText.Clean(k)[..^2] : RuText.Clean(k))));
    }
}
