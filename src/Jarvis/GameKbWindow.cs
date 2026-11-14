using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace MishkaHelper.Core;

/// <summary>
/// The base of one game: search, filters by kind and stage, spoilers of later stages blurred until asked for, and a question box.
/// Over the game (borderless/windowed) or as a normal window for a second monitor. The hotkey hides it and gives the game its focus back.
/// </summary>
public static class GameKbWindow
{
    static Window? _w;
    static IntPtr _game;
    public static bool Shown => _w != null;

    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);

    public static void Close()
    {
        _w?.Close();
        if (_game != IntPtr.Zero) SetForegroundWindow(_game);   // straight back into the game
    }

    static Brush R(string k) => (Brush)Application.Current.Resources[k];
    static Style St(string k) => (Style)Application.Current.Resources[k];

    public static void Open(GameBase b, IntPtr game)
    {
        _w?.Close();
        _game = game;
        var s = Settings.Current;
        var w = new Window
        {
            Title = b.Name + " — база знаний", Width = s.GameKbWidth, Height = s.GameKbHeight, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Topmost = s.GameKbOnTop, Background = R("BgBrush"), Foreground = R("TextBrush"), FontFamily = (FontFamily)Application.Current.Resources["MainFont"],
            ShowInTaskbar = true,
        };
        w.SourceInitialized += (_, _) => ThemeManager.ApplyBackdrop(w);
        if (s.GameKbLeft is double l && s.GameKbTop is double t) { w.WindowStartupLocation = WindowStartupLocation.Manual; w.Left = l; w.Top = t; }
        w.Closed += (_, _) =>
        {
            s.GameKbWidth = w.Width; s.GameKbHeight = w.Height; s.GameKbLeft = w.Left; s.GameKbTop = w.Top; Settings.Save();
            _w = null;
        };
        w.PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };

        var root = new DockPanel { Margin = new Thickness(14) };
        w.Content = root;

        // ---- top: title, stage reached, on top ----
        var top = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var pin = new CheckBox { Style = St("Switch"), Content = "Поверх игры", IsChecked = s.GameKbOnTop, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        pin.Click += (_, _) => { s.GameKbOnTop = w.Topmost = pin.IsChecked == true; Settings.Save(); };
        DockPanel.SetDock(pin, Dock.Right); top.Children.Add(pin);
        var stageBox = new ComboBox { Width = 230, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Сюжет и спойлеры дальше этого этапа скрыты" };
        stageBox.Items.Add("Я прошёл всё — без скрытия");
        foreach (var st in b.Stages) stageBox.Items.Add($"Я на этапе {st.N}" + (Named(st) ? ": " + st.Name : ""));
        stageBox.SelectedIndex = b.Progress > b.Stages.Count ? 0 : Math.Max(0, b.Progress);
        DockPanel.SetDock(stageBox, Dock.Right); top.Children.Add(stageBox);
        top.Children.Add(new TextBlock { Text = b.Name, FontSize = 20, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);

        // ---- search and filters ----
        var search = new TextBox { Padding = new Thickness(10, 7, 10, 7), FontSize = 14 };
        var searchRow = UiKit.WithHint(search, "Поиск: босс, предмет, локация, задание…");
        searchRow.Margin = new Thickness(0, 0, 0, 8);
        DockPanel.SetDock(searchRow, Dock.Top); root.Children.Add(searchRow);
        var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        DockPanel.SetDock(chips, Dock.Top); root.Children.Add(chips);
        string kind = "";
        var stageFilter = new ComboBox { Width = 170, Margin = new Thickness(0, 0, 8, 6) };

        // ---- question box ----
        var ask = new TextBox { Padding = new Thickness(10, 7, 10, 7), FontSize = 13 };
        var askRow = new DockPanel { Margin = new Thickness(0, 8, 0, 0) };
        var askBtn = new Button { Content = "Спросить", Style = St("AccentButton"), Margin = new Thickness(8, 0, 0, 0) };
        DockPanel.SetDock(askBtn, Dock.Right); askRow.Children.Add(askBtn);
        askRow.Children.Add(UiKit.WithHint(ask, "Спросить про игру: «как победить…», «где найти…», «что делать если…»"));
        DockPanel.SetDock(askRow, Dock.Bottom); root.Children.Add(askRow);

        // ---- list and article ----
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(330) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var list = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = R("TextBrush") };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        grid.Children.Add(list);
        var article = new StackPanel();
        var scroller = new ScrollViewer { Content = article, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var card = new Border { Background = R("CardBrush"), BorderBrush = R("CardBorderBrush"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(16), Child = scroller };
        Grid.SetColumn(card, 2); grid.Children.Add(card);
        root.Children.Add(grid);

        // Names of acts past the player's own are spoilers too ("Падение Абсолюта"): only numbers for those.
        bool Named(KbStage st) => b.Progress > b.Stages.Count || st.N <= b.Progress;
        bool Hidden(KbEntry e) => b.Progress > 0 && b.Progress <= b.Stages.Count && e.Stage > b.Progress;

        void Show(KbEntry e)
        {
            article.Children.Clear();
            bool secret = Hidden(e) || (e.Spoiler && b.Progress == 0);
            var head = new TextBlock { Text = secret && e.Spoiler ? $"Спойлер · этап {e.Stage}" : e.Title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            article.Children.Add(head);
            var meta = GameKb.KindName(e.Kind) + (e.Stage > 0 && b.Stages.FirstOrDefault(x => x.N == e.Stage) is { } st ? $" · этап {st.N}" + (Named(st) ? ": " + st.Name : "") : "") + (e.TitleEn.Length > 0 && e.TitleEn != e.Title ? $" · {e.TitleEn}" : "");
            article.Children.Add(new TextBlock { Text = meta, Foreground = R("DimBrush"), FontSize = 12, Margin = new Thickness(0, 2, 0, 10), TextWrapping = TextWrapping.Wrap });
            var body = new TextBlock { Text = e.Text.Length > 0 ? e.Text : "Загружаю описание…", FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 21 };
            article.Children.Add(body);
            if (secret)
            {
                body.Effect = new BlurEffect { Radius = 14 };
                var reveal = new Button { Content = e.Stage > 0 ? $"Спойлер этапа {e.Stage} — показать" : "Спойлер — показать", Margin = new Thickness(0, 10, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
                reveal.Click += (_, _) => { body.Effect = null; head.Text = e.Title; reveal.Visibility = Visibility.Collapsed; };
                article.Children.Add(reveal);
            }
            if (e.Source.StartsWith("http"))
            {
                var src = new Button { Content = "Источник", Style = St("GhostButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0) };
                src.Click += (_, _) => Shell.Open(e.Source);
                article.Children.Add(src);
            }
            if (e.Text.Length == 0)
                _ = GameBuilder.Expand(b, e).ContinueWith(_ => w.Dispatcher.BeginInvoke(() => { if (list.SelectedItem is ListBoxItem { Tag: KbEntry cur } && cur == e) Show(e); }));
        }

        void Fill()
        {
            var q = search.Text.Trim();
            var found = GameBuilder.Search(b, q).Where(e => kind.Length == 0 || e.Kind == kind);
            if (stageFilter.SelectedIndex > 0) found = found.Where(e => e.Stage == stageFilter.SelectedIndex || e.Stage == 0);
            if (q.Length == 0) found = found.OrderBy(e => Array.FindIndex(GameKb.Kinds, k => k.Key == e.Kind)).ThenBy(e => e.Stage).ThenBy(e => e.Title);
            list.Items.Clear();
            foreach (var e in found.Take(400))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = GameKb.Kinds.FirstOrDefault(k => k.Key == e.Kind).Icon ?? "", FontFamily = (FontFamily)Application.Current.Resources["IconFont"], Foreground = R("AccentBrush"), Width = 24, VerticalAlignment = VerticalAlignment.Center });
                var title = Hidden(e) && e.Spoiler ? $"Спойлер · этап {e.Stage}" : e.Title;
                row.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 280, VerticalAlignment = VerticalAlignment.Center });
                list.Items.Add(new ListBoxItem { Content = row, Tag = e, Padding = new Thickness(6, 5, 6, 5), Foreground = R("TextBrush") });
            }
            if (list.Items.Count == 0)
            {
                article.Children.Clear();
                article.Children.Add(UiKit.Text(q.Length > 0 ? $"В базе нет «{q}»." : "Пусто.", 14));
                if (q.Length > 0 && b.Wiki.Length > 0)
                {
                    var look = new Button { Content = $"Поискать «{q}» на вики и добавить", Style = St("AccentButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
                    look.Click += async (_, _) =>
                    {
                        look.IsEnabled = false; look.Content = "Ищу и перевожу…";
                        try { if (await GameBuilder.Lookup(b, q) is { } found2) { Fill(); Show(found2); } else look.Content = "Не нашлось"; }
                        catch (Exception ex) { look.Content = "Ошибка: " + ex.Message; }
                    };
                    article.Children.Add(look);
                }
            }
            else if (list.SelectedIndex < 0) list.SelectedIndex = 0;
        }

        void Chips()
        {
            chips.Children.Clear();
            RadioButton Chip(string key, string label, int count)
            {
                var c = new RadioButton { Style = St("Segment"), Content = $"{label} {count}", GroupName = "kbkind", IsChecked = kind == key, Margin = new Thickness(0, 0, 6, 6) };
                c.Checked += (_, _) => { kind = key; Fill(); };
                return c;
            }
            chips.Children.Add(Chip("", "Всё", b.Entries.Count));
            foreach (var (k, name, _) in GameKb.Kinds)
                if (b.Entries.Count(e => e.Kind == k) is > 0 and var n) chips.Children.Add(Chip(k, name, n));
            if (b.Stages.Count > 1)
            {
                stageFilter.Items.Clear();
                stageFilter.Items.Add("Все этапы");
                foreach (var st in b.Stages) stageFilter.Items.Add($"Этап {st.N}" + (Named(st) ? ": " + st.Name : ""));
                stageFilter.SelectedIndex = 0;
                chips.Children.Add(stageFilter);
            }
        }

        void Refresh() => w.Dispatcher.BeginInvoke(() => { Chips(); Fill(); });

        list.SelectionChanged += (_, _) => { if (list.SelectedItem is ListBoxItem { Tag: KbEntry e }) Show(e); };
        search.TextChanged += (_, _) => Fill();
        stageFilter.SelectionChanged += (_, _) => Fill();
        stageBox.SelectionChanged += (_, _) =>
        {
            b.Progress = stageBox.SelectedIndex == 0 ? b.Stages.Count + 1 : stageBox.SelectedIndex;
            GameKb.Save(b);
            Fill();
        };
        async void Ask()
        {
            var q = ask.Text.Trim();
            if (q.Length == 0) return;
            askBtn.IsEnabled = false;
            article.Children.Clear();
            article.Children.Add(new TextBlock { Text = q, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            var answer = new TextBlock { Text = "Думаю…", FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0), LineHeight = 21 };
            article.Children.Add(answer);
            try { answer.Text = await GameBuilder.Answer(b, q); }
            catch (Exception ex) { answer.Text = "Не получилось: " + ex.Message; }
            askBtn.IsEnabled = true;
        }
        askBtn.Click += (_, _) => Ask();
        ask.KeyDown += (_, e) => { if (e.Key == Key.Enter) Ask(); };
        GameKb.Changed += Refresh;
        w.Closed += (_, _) => GameKb.Changed -= Refresh;

        Chips(); Fill();
        _w = w;
        w.Show();
        w.Activate();
        search.Focus();
    }
}
