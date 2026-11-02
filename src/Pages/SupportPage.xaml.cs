using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

/// <summary>Chat with the developer. Refreshes every 5 s while open; attaching PC details is opt-in and shown before sending.</summary>
public partial class SupportPage : UserControl
{
    readonly StackPanel _chat = new();
    readonly TextBlock _status = UiKit.Text("", 12, true);
    readonly Button _github = new() { Content = "Написать на GitHub", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
    readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(5) };
    long _shown;

    public SupportPage()
    {
        InitializeComponent();
        if (!Support.Configured)
        {
            Body.Children.Add(UiKit.Text("Чат поддержки ещё не подключён. Пока можно написать на GitHub.", 13));
            var gh = new Button { Content = "Написать на GitHub", Style = (Style)FindResource("AccentButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) };
            gh.Click += (_, _) => Shell.Open(Support.IssuesUrl);
            Body.Children.Add(gh);
            return;
        }
        Body.Children.Add(new ScrollViewer { Content = _chat, MaxHeight = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        var box = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 70, MaxLength = 4000, Padding = new Thickness(8), Margin = new Thickness(0, 10, 0, 6) };
        var attach = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Приложить сведения о ПК (версия, Windows, железо)" };
        var send = new Button { Content = "Отправить", Style = (Style)FindResource("AccentButton"), HorizontalAlignment = HorizontalAlignment.Left };
        send.Click += async (_, _) =>
        {
            var text = box.Text.Trim();
            if (text.Length == 0) return;
            if (attach.IsChecked == true)
            {
                var report = $"{Edition.Display} {Edition.Version}\n{Toolbox.SystemReport()}";
                if (!UiKit.Confirm("Будет отправлено:\n\n" + report)) return;
                text += "\n\n---\n" + report;
            }
            send.IsEnabled = false; _status.Text = "Отправляю…";
            try
            {
                await Support.Send(text);
                box.Clear(); attach.IsChecked = false; _status.Text = "";
                await Refresh();
            }
            catch (Exception e) { _status.Text = "Не отправилось: " + e.Message; _github.Visibility = Visibility.Visible; }
            send.IsEnabled = true;
        };
        Body.Children.Add(UiKit.WithHint(box, "Твоё сообщение"));
        Body.Children.Add(attach);
        Body.Children.Add(send);
        Body.Children.Add(_status);
        _github.Click += (_, _) => Shell.Open(Support.IssuesUrl);
        Body.Children.Add(_github);

        _poll.Tick += async (_, _) => await Refresh();
        Loaded += async (_, _) => { await Refresh(); _poll.Start(); };
        Unloaded += (_, _) => _poll.Stop();
    }

    async Task Refresh()
    {
        try
        {
            var list = await Support.Messages();
            foreach (var m in list.Where(m => m.Id > _shown)) { _chat.Children.Add(Bubble(m)); _shown = m.Id; }
            if (list.Count > 0 && _chat.Parent is ScrollViewer sv) sv.ScrollToEnd();
            if (_status.Text.StartsWith("Нет связи")) _status.Text = "";
            _github.Visibility = Visibility.Collapsed;
        }
        catch (Exception e) { _status.Text = "Нет связи с поддержкой: " + e.Message; _github.Visibility = Visibility.Visible; }
    }

    UIElement Bubble(Support.Message m)
    {
        var sp = new StackPanel();
        var t = UiKit.Text(m.Body, 13); t.TextWrapping = TextWrapping.Wrap;
        sp.Children.Add(t);
        sp.Children.Add(UiKit.Text((m.FromAdmin ? "Поддержка · " : "") + m.At.ToString("dd.MM HH:mm"), 10, true));
        return new Border
        {
            Child = sp, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 3, 0, 3), MaxWidth = 520,
            HorizontalAlignment = m.FromAdmin ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            Background = (Brush)FindResource(m.FromAdmin ? "ControlBrush" : "AccentSoftBrush"),
        };
    }
}
