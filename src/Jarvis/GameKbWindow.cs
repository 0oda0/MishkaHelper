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
        var toggled = new HashSet<string>();   // groups folded (or, in «Всё», unfolded) by hand
        bool quiet = false;
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

        // ---- links, like a wiki: [[names]] in answers, and names of other entries in any text ----
        System.Text.RegularExpressions.Regex? names = null;
        // Keyed by the name without its last vowel: a name is also met with a Russian ending («Ульма» → «Ульме»).
        Dictionary<string, KbEntry> byStem = new(StringComparer.OrdinalIgnoreCase);
        void Names()
        {
            byStem.Clear();
            foreach (var x in b.Entries.Where(x => x.Kind is not ("stage" or "about" or "problem" or "arc") && !Hidden(x)))
            {
                var c = System.Text.RegularExpressions.Regex.Replace(x.Title, @"\s*[(:].*$", "").Trim();
                if (c.Length >= 5) byStem.TryAdd(System.Text.RegularExpressions.Regex.Replace(c, "[аяоеиыуюйь]$", ""), x);
            }
            var alts = byStem.Keys.OrderByDescending(k => k.Length).Select(System.Text.RegularExpressions.Regex.Escape);
            names = byStem.Count == 0 ? null : new(@"(?<!\p{L})(" + string.Join("|", alts) + @")\p{L}{0,3}(?!\p{L})", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }

        void Linked(TextBlock tb, string text, KbEntry? self)
        {
            tb.Inlines.Clear();
            var used = new HashSet<KbEntry>();
            if (self != null) used.Add(self);
            int at = 0;
            void Plain(string s)
            {
                int i = 0;
                if (names != null)
                    foreach (System.Text.RegularExpressions.Match m in names.Matches(s))
                    {
                        if (!byStem.TryGetValue(m.Groups[1].Value, out var to) || !used.Add(to)) continue;   // each name linked once
                        tb.Inlines.Add(new System.Windows.Documents.Run(s[i..m.Index]));
                        tb.Inlines.Add(Link(m.Value, to, null));
                        i = m.Index + m.Length;
                    }
                tb.Inlines.Add(new System.Windows.Documents.Run(s[i..]));
            }
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"\[\[([^\]|]+)(?:\|([^\]]+))?\]\]"))
            {
                Plain(text[at..m.Index]);
                var target = m.Groups[1].Value.Trim();
                var to = GameBuilder.Find(b, target);
                if (to != null) used.Add(to);
                tb.Inlines.Add(Link(m.Groups[2].Success ? m.Groups[2].Value : target, to, target));
                at = m.Index + m.Length;
            }
            Plain(text[at..]);
        }

        System.Windows.Documents.Hyperlink Link(string label, KbEntry? to, string? lookup)
        {
            var h = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run(label)) { Foreground = R("AccentBrush"), TextDecorations = null, Cursor = Cursors.Hand, ToolTip = to?.Title ?? "Поискать на вики" };
            h.Click += async (_, _) =>
            {
                if (to != null) { Show(to); return; }
                h.IsEnabled = false;
                // Not in the base yet: looked up on the wiki and kept.
                try { if (await GameBuilder.Lookup(b, lookup ?? label) is { } f) { Names(); Show(f); } else h.ToolTip = "Не нашлось"; }
                catch (Exception ex) { h.ToolTip = ex.Message; }
                h.IsEnabled = true;
            };
            return h;
        }

        void Show(KbEntry e)
        {
            article.Children.Clear();
            bool secret = Hidden(e) || (e.Spoiler && b.Progress == 0);
            var head = new TextBlock { Text = secret && e.Spoiler ? $"Спойлер · этап {e.Stage}" : e.Title, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap };
            article.Children.Add(head);
            var meta = GameKb.KindName(e.Kind) + (e.Stage > 0 && b.Stages.FirstOrDefault(x => x.N == e.Stage) is { } st ? $" · этап {st.N}" + (Named(st) ? ": " + st.Name : "") : "") + (e.TitleEn.Length > 0 && e.TitleEn != e.Title ? $" · {e.TitleEn}" : "");
            article.Children.Add(new TextBlock { Text = meta, Foreground = R("DimBrush"), FontSize = 12, Margin = new Thickness(0, 2, 0, 10), TextWrapping = TextWrapping.Wrap });
            var body = new TextBlock { FontSize = 14, TextWrapping = TextWrapping.Wrap, LineHeight = 21 };
            if (e.Text.Length > 0) Linked(body, e.Text, e); else body.Text = "Загружаю описание…";
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
            var keep = list.SelectedItem is ListBoxItem { Tag: KbEntry was } ? was : null;
            list.Items.Clear();
            void Row(KbEntry e, string title, double indent)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(new TextBlock { Text = GameKb.Kinds.FirstOrDefault(k => k.Key == e.Kind).Icon ?? "", FontFamily = (FontFamily)Application.Current.Resources["IconFont"], Foreground = R("AccentBrush"), Width = 24, VerticalAlignment = VerticalAlignment.Center });
                if (Hidden(e) && e.Spoiler) title = $"Спойлер · этап {e.Stage}";
                row.Children.Add(new TextBlock { Text = title, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 280 - indent, VerticalAlignment = VerticalAlignment.Center });
                list.Items.Add(new ListBoxItem { Content = row, Tag = e, Padding = new Thickness(6 + indent, 5, 6, 5), Foreground = R("TextBrush") });
            }
            if (q.Length > 0)
                foreach (var e in found.Take(400)) Row(e, e.Title, 0);
            else
            {
                // Like the game's journal: groups that fold. Storylines by character, «Всё» by kind, the rest by stage.
                var groups = found.GroupBy(e => Group(e)).OrderBy(g => g.Min(e => Order(e))).ToList();
                foreach (var g in groups)
                {
                    var items = g.OrderBy(e => e.Stage).ThenBy(e => e.Kind == "arc" ? "" : e.Title).ToList();
                    if (groups.Count == 1) { foreach (var e in items) Row(e, e.Title, 0); break; }
                    var key = kind + "|" + g.Key;
                    bool open = kind.Length == 0 ? toggled.Contains(key) : !toggled.Contains(key);
                    var head = new ListBoxItem
                    {
                        Content = new TextBlock { Text = (open ? "▾  " : "▸  ") + g.Key + $"  ({items.Count})", FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                        Tag = key, Padding = new Thickness(4, 7, 6, 5), Foreground = R("TextBrush"), Focusable = false,
                    };
                    head.PreviewMouseLeftButtonDown += (_, a) => { a.Handled = true; if (!toggled.Remove(key)) toggled.Add(key); Fill(); };
                    list.Items.Add(head);
                    if (open) foreach (var e in items) Row(e, e.Kind == "arc" ? e.Title[(e.Title.IndexOf(':') + 1)..].Trim() : e.Title, 14);
                }
            }
            // The entry being read stays selected (a fold doesn't throw the article away).
            if (keep != null && list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag == keep) is { } again) { quiet = true; list.SelectedItem = again; quiet = false; return; }
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
            else if (list.SelectedIndex < 0 && keep == null) list.SelectedItem = list.Items.OfType<ListBoxItem>().FirstOrDefault(i => i.Tag is KbEntry);
        }

        string Group(KbEntry e)
        {
            if (kind.Length == 0) return GameKb.KindName(e.Kind);
            if (e.Kind == "arc") return e.Title.Split(':')[0].Trim();
            if (e.Stage <= 0 || b.Stages.FirstOrDefault(s => s.N == e.Stage) is not { } st) return "Без этапа";
            return $"Этап {st.N}" + (Named(st) ? ": " + st.Name : "");
        }
        // Groups in the journal's order: kinds as the filters list them, stages by number, «Без этапа» last.
        int Order(KbEntry e) => kind.Length == 0 ? Array.FindIndex(GameKb.Kinds, k => k.Key == e.Kind) : e.Kind == "arc" ? 0 : e.Stage <= 0 ? 99 : e.Stage;

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

        void Refresh() => w.Dispatcher.BeginInvoke(() => { Names(); Chips(); Fill(); });

        list.SelectionChanged += (_, _) => { if (!quiet && list.SelectedItem is ListBoxItem { Tag: KbEntry e }) Show(e); };
        search.TextChanged += (_, _) => Fill();
        stageFilter.SelectionChanged += (_, _) => Fill();
        stageBox.SelectionChanged += (_, _) =>
        {
            b.Progress = stageBox.SelectedIndex == 0 ? b.Stages.Count + 1 : stageBox.SelectedIndex;
            GameKb.Save(b);
            Names(); Fill();
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
            try { var a = await GameBuilder.Answer(b, q); answer.Text = ""; Linked(answer, a, null); }
            catch (Exception ex) { answer.Text = "Не получилось: " + ex.Message; }
            askBtn.IsEnabled = true;
        }
        askBtn.Click += (_, _) => Ask();
        ask.KeyDown += (_, e) => { if (e.Key == Key.Enter) Ask(); };
        GameKb.Changed += Refresh;
        w.Closed += (_, _) => GameKb.Changed -= Refresh;

        Names(); Chips(); Fill();
        _w = w;
        w.Show();
        w.Activate();
        search.Focus();
    }
}
