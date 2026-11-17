using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

/// <summary>«Стримдек»: the phone's button pages edited on the PC — pages, the programs that bring each up, and every key.</summary>
public class DeckEditPage : UserControl
{
    static Style S(string k) => (Style)Application.Current.Resources[k];
    static Brush B(string k) => (Brush)Application.Current.Resources[k];
    static DeckSettings D => Settings.Current.Deck;

    static readonly (string Kind, string Name)[] Kinds =
    {
        ("act", "Действие Mishka"), ("keys", "Сочетание клавиш"), ("text", "Вставить текст"), ("open", "Открыть программу, файл или сайт"), ("page", "Перейти на страницу стримдека"), ("", "Пусто"),
    };

    static readonly string[] Swatches = { "", "#FF3DA8", "#3DC8FF", "#3DFF8A", "#FFB23D", "#B03DFF", "#FF5566", "#22223A" };

    readonly ComboBox _pages = new() { Width = 220 };
    readonly TextBox _procs = new() { Width = 360, Padding = new Thickness(8, 5, 8, 5) };
    readonly UniformGrid _grid = new();
    readonly StackPanel _editor = new();
    int _slot = -1;

    Core.DeckPage? Page => _pages.SelectedIndex >= 0 && _pages.SelectedIndex < D.Pages.Count ? D.Pages[_pages.SelectedIndex] : null;

    public DeckEditPage()
    {
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = "Стримдек", Style = S("H1") });
        root.Children.Add(new TextBlock
        {
            Text = "Телефон или планшет становится панелью больших кнопок: открой вкладку «Деск» в приложении Mishka. Кнопка может нажать любое действие Mishka, сочетание клавиш, " +
                   "вставить текст, открыть программу или сайт и показывать живые цифры: загрузку, температуру, FPS, громкость. Страница сама меняется под программу на ПК. " +
                   "Настраивать можно и здесь, и на телефоне (долгое нажатие на кнопку). Сколько кнопок на экране, каждое устройство решает само (⚙ в приложении): " +
                   "кнопки идут по порядку, телефон показывает первые, планшет — больше.",
            Style = S("Dim"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18),
        });

        // ---- pages ----
        var pc = new StackPanel();
        pc.Children.Add(new TextBlock { Text = "Страницы", Style = S("H2") });
        var row = new WrapPanel();
        row.Children.Add(_pages);
        Btn(row, "Добавить", () => { var n = UiKit.Ask("Новая страница", "Название страницы"); if (n.Length == 0) return; D.Pages.Add(new Core.DeckPage { Name = n }); Save(); FillPages(D.Pages.Count - 1); });
        Btn(row, "Переименовать", () => { if (Page is not { } p) return; var n = UiKit.Ask("Страница", "Название", p.Name); if (n.Length == 0) return; p.Name = n; Save(); FillPages(_pages.SelectedIndex); });
        Btn(row, "Удалить", () => { if (Page is not { } p || D.Pages.Count < 2 || !UiKit.Confirm($"Удалить страницу «{p.Name}»?")) return; D.Pages.Remove(p); Save(); FillPages(0); });
        pc.Children.Add(row);
        pc.Children.Add(UiKit.Text("Сама открывается, когда на ПК впереди эти программы (имена процессов через запятую: photoshop, winword; game — любая игра в игровом режиме):", 12, true));
        var prow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
        prow.Children.Add(_procs);
        Btn(prow, "Сохранить", () => { if (Page is not { } p) return; p.Processes = _procs.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(x => x.Replace(".exe", "", StringComparison.OrdinalIgnoreCase)).ToList(); Save(); });
        pc.Children.Add(prow);

        var opts = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var auto = new CheckBox { Style = S("Switch"), Content = "Менять страницу под программу", IsChecked = D.AutoPage, Margin = new Thickness(0, 0, 18, 0), VerticalAlignment = VerticalAlignment.Center };
        auto.Click += (_, _) => { D.AutoPage = auto.IsChecked == true; Save(); };
        opts.Children.Add(auto);
        opts.Children.Add(UiKit.Text("Сетка для правки:", 13));
        var size = new ComboBox { Width = 90, Margin = new Thickness(6, 0, 18, 0) };
        foreach (var (c, r) in new[] { (3, 2), (4, 3), (5, 3), (5, 4), (6, 4), (6, 5), (8, 4), (8, 5), (8, 6) }) size.Items.Add(new ComboBoxItem { Content = $"{c} × {r}", Tag = (c, r) });
        size.SelectedItem = size.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is (int c, int r) && c == D.Cols && r == D.Rows);
        size.SelectionChanged += (_, _) => { if (size.SelectedItem is ComboBoxItem { Tag: (int c, int r) }) { D.Cols = c; D.Rows = r; Save(); FillGrid(); } };
        opts.Children.Add(size);
        Btn(opts, "Вернуть стандартные", () => { if (!UiKit.Confirm("Вернуть стандартные страницы? Свои кнопки пропадут.")) return; Settings.Current.Deck = Core.Deck.Default(); Save(); FillPages(0); });
        pc.Children.Add(opts);
        root.Children.Add(UiKit.Card(pc));

        // ---- grid and the key editor ----
        var gc = new Grid();
        gc.ColumnDefinitions.Add(new ColumnDefinition());
        gc.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
        gc.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(340) });
        var gridCard = UiKit.Card(_grid);
        gc.Children.Add(gridCard);
        var ed = UiKit.Card(_editor);
        Grid.SetColumn(ed, 2); gc.Children.Add(ed);
        root.Children.Add(gc);

        Content = root;
        _pages.SelectionChanged += (_, _) => { _procs.Text = string.Join(", ", Page?.Processes ?? new()); _slot = -1; FillGrid(); Edit(); };
        FillPages(0);
        Core.Deck.Changed += OnPhoneEdit;
        Unloaded += (_, _) => Core.Deck.Changed -= OnPhoneEdit;
    }

    void OnPhoneEdit() => Dispatcher.BeginInvoke(() => FillPages(Math.Min(_pages.SelectedIndex, D.Pages.Count - 1)));

    static void Btn(Panel p, string text, Action a)
    {
        var b = new Button { Content = text, Margin = new Thickness(8, 0, 0, 4) };
        b.Click += (_, _) => a();
        p.Children.Add(b);
    }

    void Save() => Core.Deck.Saved();

    void FillPages(int select)
    {
        _pages.Items.Clear();
        foreach (var p in D.Pages) _pages.Items.Add(p.Name);
        _pages.SelectedIndex = Math.Max(0, select);
    }

    void FillGrid()
    {
        _grid.Children.Clear();
        _grid.Columns = D.Cols; _grid.Rows = D.Rows;
        for (int i = 0; i < D.Cols * D.Rows; i++)
        {
            var k = Page?.Keys.FirstOrDefault(x => x.Slot == i);
            int slot = i;
            var face = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            if (Picture(k?.Img) is { } pic) face.Children.Add(new Image { Source = pic, Width = 34, Height = 34, HorizontalAlignment = HorizontalAlignment.Center });
            else face.Children.Add(new TextBlock { Text = k?.Emoji.Length > 0 ? k.Emoji : k?.Kind == "open" ? "🗔" : "", FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center, FontFamily = new FontFamily("Segoe UI Emoji") });
            face.Children.Add(new TextBlock { Text = k?.Label ?? "+", FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = k == null ? B("DimBrush") : B("TextBrush") });
            if (k is { W: > 1 } or { H: > 1 }) face.Children.Add(new TextBlock { Text = $"{k.W} × {k.H}", FontSize = 10, HorizontalAlignment = HorizontalAlignment.Center, Foreground = B("DimBrush") });
            if (k?.Live.Length > 0) face.Children.Add(new TextBlock { Text = Core.Deck.LiveText(k.Live), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center, Foreground = B("AccentBrush") });
            var bg = k?.Color.Length > 0 ? (Brush)new BrushConverter().ConvertFrom(k.Color)! : B("CardBrush");
            var cell = new Button
            {
                Content = face, Height = 86, Margin = new Thickness(4), Background = bg, ToolTip = k == null ? "Пусто — нажми, чтобы настроить" : $"{Kinds.FirstOrDefault(x => x.Kind == k.Kind).Name}: {k.Value}",
                BorderBrush = slot == _slot ? B("AccentBrush") : B("CardBorderBrush"), BorderThickness = new Thickness(slot == _slot ? 2 : 1),
            };
            cell.Click += (_, _) => { _slot = slot; FillGrid(); Edit(); };
            _grid.Children.Add(cell);
        }
    }

    static System.Windows.Media.Imaging.BitmapSource? Picture(string? b64)
    {
        if (string.IsNullOrEmpty(b64)) return null;
        try
        {
            var bi = new System.Windows.Media.Imaging.BitmapImage();
            bi.BeginInit(); bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; bi.StreamSource = new System.IO.MemoryStream(Convert.FromBase64String(b64)); bi.EndInit(); bi.Freeze();
            return bi;
        }
        catch { return null; }
    }

    /// <summary>A picked picture made phone-sized: 128 px on the long side, PNG (keeps transparency).</summary>
    static string Shrink(string path)
    {
        var src = new System.Windows.Media.Imaging.BitmapImage();
        src.BeginInit(); src.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; src.UriSource = new Uri(path); src.EndInit();
        double s = 128.0 / Math.Max(src.PixelWidth, src.PixelHeight);
        System.Windows.Media.Imaging.BitmapSource small = s < 1 ? new System.Windows.Media.Imaging.TransformedBitmap(src, new ScaleTransform(s, s)) : src;
        var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
        enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(small));
        using var ms = new System.IO.MemoryStream();
        enc.Save(ms);
        return Convert.ToBase64String(ms.ToArray());
    }

    /// <summary>The editor of the chosen key: what it does, how it looks, what live number it shows.</summary>
    void Edit()
    {
        _editor.Children.Clear();
        if (_slot < 0 || Page is not { } page) { _editor.Children.Add(UiKit.Text("Выбери кнопку слева.", 13, true)); return; }
        var k = page.Keys.FirstOrDefault(x => x.Slot == _slot) ?? new DeckKey { Slot = _slot, Kind = "" };
        _editor.Children.Add(new TextBlock { Text = $"Кнопка {_slot % D.Cols + 1}·{_slot / D.Cols + 1}", Style = S("H2") });

        TextBox Box(string label, string value, bool multi = false)
        {
            _editor.Children.Add(UiKit.Text(label, 12, true));
            var t = new TextBox { Text = value, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 2, 0, 8), AcceptsReturn = multi, TextWrapping = multi ? TextWrapping.Wrap : TextWrapping.NoWrap, MinHeight = multi ? 60 : 0 };
            _editor.Children.Add(t);
            return t;
        }

        _editor.Children.Add(UiKit.Text("Что делает", 12, true));
        var kind = new ComboBox { Margin = new Thickness(0, 2, 0, 8) };
        foreach (var (kk, n) in Kinds) kind.Items.Add(new ComboBoxItem { Content = n, Tag = kk });
        kind.SelectedItem = kind.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == k.Kind);
        _editor.Children.Add(kind);
        var valueHost = new StackPanel();
        _editor.Children.Add(valueHost);
        Func<string> value = () => "";

        void ValueEditor()
        {
            valueHost.Children.Clear();
            var kk = (string)((ComboBoxItem)kind.SelectedItem).Tag;
            switch (kk)
            {
                case "act":
                {
                    var cmds = CommandPalette.Commands();
                    var c = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, Margin = new Thickness(0, 2, 0, 8) };
                    foreach (var x in cmds) c.Items.Add(new ComboBoxItem { Content = x.Name, Tag = x.Action });
                    c.SelectedItem = c.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == k.Value);
                    valueHost.Children.Add(c);
                    value = () => (c.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                    break;
                }
                case "keys":
                {
                    var t = new TextBox { Text = k.Kind == "keys" ? k.Value : "", Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 2, 0, 2) };
                    valueHost.Children.Add(t);
                    valueHost.Children.Add(UiKit.Text("Например ctrl+shift+m, f13, play. Несколько по очереди — через пробел: alt w f f", 11, true));
                    value = () => t.Text.Trim().ToLowerInvariant();
                    break;
                }
                case "text":
                {
                    var t = new TextBox { Text = k.Kind == "text" ? k.Value : "", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60, Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 2, 0, 8) };
                    valueHost.Children.Add(t);
                    value = () => t.Text;
                    break;
                }
                case "open":
                {
                    var t = new TextBox { Text = k.Kind == "open" ? k.Value : "", Padding = new Thickness(8, 5, 8, 5), Margin = new Thickness(0, 2, 0, 4) };
                    valueHost.Children.Add(t);
                    var browse = new Button { Content = "Выбрать программу или файл…", HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 8) };
                    browse.Click += (_, _) => { var dlg = new Microsoft.Win32.OpenFileDialog(); if (dlg.ShowDialog() == true) t.Text = dlg.FileName; };
                    valueHost.Children.Add(browse);
                    value = () => t.Text.Trim();
                    break;
                }
                case "page":
                {
                    var c = new ComboBox { Margin = new Thickness(0, 2, 0, 8) };
                    foreach (var p in D.Pages) c.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
                    c.SelectedItem = c.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == k.Value) ?? c.Items.OfType<ComboBoxItem>().FirstOrDefault();
                    valueHost.Children.Add(c);
                    value = () => (c.SelectedItem as ComboBoxItem)?.Tag as string ?? "";
                    break;
                }
                default: value = () => ""; break;
            }
        }
        ValueEditor();
        kind.SelectionChanged += (_, _) => ValueEditor();

        var label = Box("Подпись", k.Label);
        var emoji = Box("Значок (эмодзи; у программы пусто — будет её иконка)", k.Emoji);
        _editor.Children.Add(UiKit.Text("Цвет", 12, true));
        string color = k.Color;
        var sw = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
        foreach (var hex in Swatches)
        {
            var b = new Button { Width = 26, Height = 26, Margin = new Thickness(0, 0, 6, 0), ToolTip = hex.Length == 0 ? "Как у темы" : hex,
                Background = hex.Length == 0 ? B("CardBrush") : (Brush)new BrushConverter().ConvertFrom(hex)!, BorderThickness = new Thickness(hex == color ? 2 : 1), BorderBrush = B("AccentBrush") };
            b.Click += (_, _) => { color = hex; foreach (var x in sw.Children.OfType<Button>()) x.BorderThickness = new Thickness(x == b ? 2 : 1); };
            sw.Children.Add(b);
        }
        _editor.Children.Add(sw);
        _editor.Children.Add(UiKit.Text("Показывать на кнопке", 12, true));
        var live = new ComboBox { Margin = new Thickness(0, 2, 0, 10) };
        foreach (var (lk, ln) in Core.Deck.Lives) live.Items.Add(new ComboBoxItem { Content = ln, Tag = lk });
        live.SelectedItem = live.Items.OfType<ComboBoxItem>().First(i => (string)i.Tag == k.Live || ((string)i.Tag == "" && !Core.Deck.Lives.Any(l => l.Key == k.Live)));
        _editor.Children.Add(live);
        _editor.Children.Add(UiKit.Text("Как показывать", 12, true));
        var style = new ComboBox { Margin = new Thickness(0, 2, 0, 10) };
        foreach (var (sk, sn) in Core.Deck.Styles) style.Items.Add(new ComboBoxItem { Content = sn, Tag = sk });
        style.SelectedItem = style.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == k.Style) ?? style.Items[0];
        _editor.Children.Add(style);
        _editor.Children.Add(UiKit.Text("Размер на телефоне (клеток): графику лучше 2 × 1 и больше", 12, true));
        var sizeRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 10) };
        var kw = new ComboBox { Width = 60 }; var kh = new ComboBox { Width = 60, Margin = new Thickness(8, 0, 0, 0) };
        for (int n = 1; n <= 4; n++) { kw.Items.Add(n); kh.Items.Add(n); }
        kw.SelectedItem = Math.Clamp(k.W, 1, 4); kh.SelectedItem = Math.Clamp(k.H, 1, 4);
        sizeRow.Children.Add(UiKit.Text("ширина", 12)); sizeRow.Children.Add(kw); sizeRow.Children.Add(UiKit.Text("  высота", 12)); sizeRow.Children.Add(kh);
        _editor.Children.Add(sizeRow);
        _editor.Children.Add(UiKit.Text("Свайп по кнопке влево / вправо", 12, true));
        var swipe = new ComboBox { Margin = new Thickness(0, 2, 0, 10) };
        foreach (var (wk, wn) in Core.Deck.Swipes) swipe.Items.Add(new ComboBoxItem { Content = wn, Tag = wk });
        swipe.SelectedItem = swipe.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == k.Swipe) ?? swipe.Items[0];
        _editor.Children.Add(swipe);
        _editor.Children.Add(UiKit.Text("Картинка вместо эмодзи", 12, true));
        string img = k.Img;
        var imgRow = new WrapPanel { Margin = new Thickness(0, 2, 0, 10) };
        var preview = new Image { Width = 40, Height = 40, Margin = new Thickness(0, 0, 8, 0), Source = Picture(img) };
        imgRow.Children.Add(preview);
        Btn(imgRow, "Выбрать…", () =>
        {
            var dlg = new Microsoft.Win32.OpenFileDialog { Filter = "Картинки|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.webp" };
            if (dlg.ShowDialog() != true) return;
            try { img = Shrink(dlg.FileName); preview.Source = Picture(img); } catch (Exception ex) { UiKit.Text("Не открылась: " + ex.Message, 12); }
        });
        Btn(imgRow, "Убрать", () => { img = ""; preview.Source = null; });
        _editor.Children.Add(imgRow);

        var msg = UiKit.Text("", 12, true);
        var btns = new WrapPanel();
        var save = new Button { Content = "Сохранить кнопку", Style = S("AccentButton") };
        save.Click += (_, _) =>
        {
            var kk = (string)((ComboBoxItem)kind.SelectedItem).Tag;
            page.Keys.RemoveAll(x => x.Slot == _slot);
            if (kk.Length > 0)
            {
                var v = value();
                if (kk == "keys" && !AppWheels.Valid(v)) { msg.Text = "Не понял клавиши: напиши как ctrl+shift+m или alt w f f"; return; }
                if (kk is "act" or "open" or "page" && v.Length == 0) { msg.Text = "Выбери, что открыть или нажать"; return; }
                page.Keys.Add(new DeckKey { Slot = _slot, Kind = kk, Value = v, Label = label.Text.Trim(), Emoji = emoji.Text.Trim(), Color = color, Live = (string)((ComboBoxItem)live.SelectedItem).Tag,
                    Style = (string)((ComboBoxItem)style.SelectedItem).Tag, W = (int)kw.SelectedItem, H = (int)kh.SelectedItem,
                    Swipe = (string)((ComboBoxItem)swipe.SelectedItem).Tag, Img = img });
            }
            Save(); FillGrid(); msg.Text = "Сохранено — на телефоне уже обновилось";
        };
        btns.Children.Add(save);
        Btn(btns, "Очистить", () => { page.Keys.RemoveAll(x => x.Slot == _slot); Save(); FillGrid(); Edit(); });
        _editor.Children.Add(btns);
        _editor.Children.Add(msg);
    }
}
