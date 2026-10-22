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

    static bool LooksLikeLogin(string text, string url) =>
        text.Length < 2500 && (url.Contains("login", StringComparison.OrdinalIgnoreCase) || url.Contains("auth", StringComparison.OrdinalIgnoreCase) ||
                               text.Contains("пароль", StringComparison.OrdinalIgnoreCase) && text.Contains("вход", StringComparison.OrdinalIgnoreCase));

    public static WebSource? Match(string question)
    {
        var q = Ears.Clean(question);
        return Settings.Current.Jarvis.Sources.FirstOrDefault(s =>
            (s.Keywords + "," + s.Name).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Any(k => k.Length > 2 && q.Contains(Ears.Clean(k).Length > 5 ? Ears.Clean(k)[..^2] : Ears.Clean(k))));
    }
}
