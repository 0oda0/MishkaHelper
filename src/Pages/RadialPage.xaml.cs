using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

/// <summary>Wheel editor: Settings.Radial is the root ring in order (empty = default wheel).</summary>
public partial class RadialPage : UserControl
{
    static List<RadialEntry> R => Settings.Current.Radial;
    static Style S(string k) => (Style)Application.Current.Resources[k];

    readonly StackPanel _list = new();
    readonly TextBlock _count = UiKit.Text("", 12, true);
    readonly ComboBox _builtin = new() { Width = 260, Margin = new Thickness(0, 0, 8, 0) };

    public RadialPage()
    {
        InitializeComponent();
        Cards.Children.Add(ItemsCard());
        Cards.Children.Add(AddCard());
        Render();
    }

    /// <summary>The first edit turns the default wheel into an explicit list.</summary>
    static void Materialize()
    {
        if (R.Count > 0) return;
        foreach (var (key, icon, name) in RadialMenu.Catalog()) R.Add(new RadialEntry { Key = key, Label = name, Icon = icon });
    }

    static (string Icon, string Name) Look(RadialEntry e)
    {
        if (e.Key.StartsWith("act:")) return (e.Icon, e.Label);
        var b = RadialMenu.Catalog().FirstOrDefault(c => c.Key == e.Key);
        return b.Key == null ? (e.Icon, e.Label + " (недоступно)") : (b.Icon, b.Name);
    }

    void Changed() { Settings.Save(); Render(); }

    void Render()
    {
        _list.Children.Clear();
        var shown = R.Count > 0 ? R : RadialMenu.Catalog().Select(c => new RadialEntry { Key = c.Key, Label = c.Name, Icon = c.Icon }).ToList();
        for (int i = 0; i < shown.Count; i++)
        {
            int at = i;
            var (icon, name) = Look(shown[i]);
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            Button B(string glyph, string tip, Action act, bool enabled = true)
            {
                var b = new Button { Content = glyph, FontFamily = (FontFamily)FindResource("IconFont"), Style = S("GhostButton"), ToolTip = tip, IsEnabled = enabled };
                b.Click += (_, _) => { Materialize(); act(); Changed(); };
                btns.Children.Add(b);
                return b;
            }
            B("", "Выше", () => (R[at - 1], R[at]) = (R[at], R[at - 1]), at > 0);
            B("", "Ниже", () => (R[at + 1], R[at]) = (R[at], R[at + 1]), at < shown.Count - 1);
            B("", "Убрать из колеса", () => R.RemoveAt(at));
            DockPanel.SetDock(btns, Dock.Right);
            row.Children.Add(btns);
            var ic = UiKit.Icon(icon, 16); ic.Width = 30; ic.Foreground = (Brush)FindResource("AccentBrush");
            row.Children.Add(ic);
            row.Children.Add(new TextBlock { Text = $"{i + 1}. {name}", FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
            _list.Children.Add(row);
        }
        _count.Text = shown.Count > RadialMenu.RootMax
            ? $"Пунктов: {shown.Count}. Больше {RadialMenu.RootMax} — остальные уйдут на страницу «Ещё →»."
            : $"Пунктов: {shown.Count} (удобно до {RadialMenu.RootMax}).";
        _builtin.Items.Clear();
        foreach (var c in RadialMenu.Catalog().Where(c => shown.All(s => s.Key != c.Key)))
            _builtin.Items.Add(new ComboBoxItem { Content = c.Name, Tag = c.Key });
        _builtin.IsEnabled = _builtin.Items.Count > 0;
        if (_builtin.Items.Count > 0) _builtin.SelectedIndex = 0;
    }

    UIElement ItemsCard()
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "Пункты колеса", Style = S("H2") });
        sp.Children.Add(UiKit.Text("Первый — сверху, дальше по часовой стрелке.", 12, true));
        sp.Children.Add(new Border { Height = 6 });
        sp.Children.Add(_list);
        sp.Children.Add(_count);
        var wp = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        var test = new Button { Content = "Открыть колесо", Style = S("AccentButton") };
        test.Click += (_, _) => RadialMenu.Toggle();
        var reset = new Button { Content = "Вернуть как было" };
        reset.Click += (_, _) => { if (R.Count == 0 || UiKit.Confirm("Вернуть колесо по умолчанию? Твои пункты удалятся.")) { R.Clear(); Changed(); } };
        wp.Children.Add(test); wp.Children.Add(reset);
        sp.Children.Add(wp);
        return UiKit.Card(sp);
    }

    UIElement AddCard()
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = "Добавить", Style = S("H2") });

        sp.Children.Add(UiKit.Text("Встроенный раздел (с подменю)", 12, true));
        var row1 = new WrapPanel { Margin = new Thickness(0, 4, 0, 12) };
        var add1 = new Button { Content = "+ Добавить" };
        add1.Click += (_, _) =>
        {
            if (_builtin.SelectedItem is not ComboBoxItem { Tag: string key }) return;
            Materialize();
            R.Add(new RadialEntry { Key = key });
            Changed();
        };
        row1.Children.Add(_builtin); row1.Children.Add(add1);
        sp.Children.Add(row1);

        sp.Children.Add(UiKit.Text("Любое действие: функция, страница, профиль, программа, виджет, сценарий", 12, true));
        var cmds = CommandPalette.Commands();
        var action = new ComboBox { IsEditable = true, IsTextSearchEnabled = true, Width = 360, Margin = new Thickness(0, 4, 8, 0), StaysOpenOnEdit = true };
        foreach (var c in cmds) action.Items.Add(c.Name);
        var label = new TextBox { Width = 200, Margin = new Thickness(0, 4, 8, 0), ToolTip = "Подпись в колесе (необязательно)" };
        action.SelectionChanged += (_, _) =>
        {
            if (action.SelectedIndex < 0) return;
            // Suggest a short label: "Открыть: Защита" → "Защита".
            var n = cmds[action.SelectedIndex].Name;
            label.Text = n.Contains(": ") ? n[(n.IndexOf(": ") + 2)..] : n;
        };
        var add2 = new Button { Content = "+ Добавить", Margin = new Thickness(0, 4, 0, 0) };
        add2.Click += (_, _) =>
        {
            int i = action.SelectedIndex >= 0 ? action.SelectedIndex : cmds.FindIndex(c => c.Name.Equals(action.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            if (i < 0) { Osd.Show("", "Выбери действие из списка"); return; }
            var name = label.Text.Trim();
            if (name.Length == 0) name = cmds[i].Name;
            Materialize();
            R.Add(new RadialEntry { Key = "act:" + cmds[i].Action, Label = name.Length > 22 ? name[..21] + "…" : name, Icon = cmds[i].Icon });
            label.Text = ""; action.SelectedIndex = -1; action.Text = "";
            Changed();
        };
        var row2 = new WrapPanel();
        row2.Children.Add(action); row2.Children.Add(label); row2.Children.Add(add2);
        sp.Children.Add(row2);
        sp.Children.Add(UiKit.Text("Например: «Профиль: Игра» прямо в корне, «Открыть: Защита», «Виджет: Музыка», свой сценарий «Режим кино».", 11, true));
        return UiKit.Card(sp);
    }
}
