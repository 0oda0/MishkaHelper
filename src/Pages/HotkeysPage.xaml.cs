using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

public partial class HotkeysPage : UserControl
{
    public HotkeysPage()
    {
        InitializeComponent();
        Render();
        Unloaded += (_, _) => Hotkeys.Reload();
    }

    void Render()
    {
        Rows.Children.Clear();
        foreach (var (key, name) in Defaults.HotkeyNames.Where(k => Defaults.Available(k.Key))) Rows.Children.Add(Row(key, name));
        ProfileRows.Children.Clear();
        foreach (var p in Settings.Current.Profiles) ProfileRows.Children.Add(Row("profile:" + p.Name, "Профиль «" + p.Name + "»"));
        RenderLaunchers();
        RenderMouse();
    }

    void RenderMouse()
    {
        while (MouseBox.Children.Count > 1) MouseBox.Children.RemoveAt(1);
        var m = Settings.Current.Mouse;
        CheckBox Sw(string text, bool on, Action<bool> set)
        {
            var c = new CheckBox { Style = (Style)FindResource("Switch"), Content = text, IsChecked = on, Margin = new Thickness(0, 4, 0, 0) };
            c.Click += (_, _) => { set(c.IsChecked == true); Settings.Save(); Hotkeys.Reload(); RenderMouse(); };
            MouseBox.Children.Add(c);
            return c;
        }
        Sw("Колёсико над панелью задач меняет громкость", m.TaskbarVolume, v => m.TaskbarVolume = v);
        Sw("Удержать колёсико — радиальное меню (короткий клик работает как обычно)", m.HoldWheelRadial, v => m.HoldWheelRadial = v);
        Sw("Жесты: держи боковую кнопку и веди мышь в сторону", m.Gestures, v => m.Gestures = v);
        if (!m.Gestures) return;
        var which = new ComboBox { Width = 220, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 6, 0, 4) };
        which.Items.Add(new ComboBoxItem { Content = "Кнопка «вперёд»", Tag = MouseHotkeys.Forward });
        which.Items.Add(new ComboBoxItem { Content = "Кнопка «назад»", Tag = MouseHotkeys.Back });
        which.SelectedIndex = m.GestureButton == MouseHotkeys.Back ? 1 : 0;
        which.SelectionChanged += (_, _) => { m.GestureButton = (string)((ComboBoxItem)which.SelectedItem).Tag; Settings.Save(); Hotkeys.Reload(); };
        MouseBox.Children.Add(which);
        var names = Defaults.HotkeyNames.Where(k => Defaults.Available(k.Key)).ToList();
        foreach (var (dir, label) in new[] { ("up", "Вверх ↑"), ("down", "Вниз ↓"), ("left", "Влево ←"), ("right", "Вправо →") })
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var t = UiKit.Text(label, 13); t.Width = 90; t.VerticalAlignment = VerticalAlignment.Center;
            var pick = new ComboBox { Width = 380, HorizontalAlignment = HorizontalAlignment.Left };
            pick.Items.Add(new ComboBoxItem { Content = "ничего", Tag = "" });
            foreach (var (key, name) in names) pick.Items.Add(new ComboBoxItem { Content = name, Tag = key });
            var cur = m.GestureActions.GetValueOrDefault(dir, "");
            pick.SelectedItem = pick.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == cur) ?? pick.Items[0];
            pick.SelectionChanged += (_, _) => { m.GestureActions[dir] = (string)((ComboBoxItem)pick.SelectedItem).Tag; Settings.Save(); };
            row.Children.Add(t); row.Children.Add(pick);
            MouseBox.Children.Add(row);
        }
        MouseBox.Children.Add(UiKit.Text("Короткое нажатие кнопки без движения работает как обычно («назад» / «вперёд» в браузере).", 11, true));
    }

    UIElement Row(string action, string name)
    {
        var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        g.ColumnDefinitions.Add(new ColumnDefinition());
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var label = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        label.Children.Add(UiKit.Text(name, 13));
        if (Hotkeys.Failed.Contains(action))
        {
            var warn = UiKit.Text("⚠ Занято другой программой — выбери другое сочетание", 11);
            warn.Foreground = (Brush)FindResource("WarnBrush");
            label.Children.Add(warn);
        }
        g.Children.Add(label);

        Settings.Current.Hotkeys.TryGetValue(action, out var combo);
        var btn = new Button { Content = string.IsNullOrEmpty(combo) ? "не задано" : Hotkeys.Pretty(combo), Margin = new Thickness(0), MinWidth = 200, FontFamily = new FontFamily("Consolas, Segoe UI") };
        btn.Click += (_, _) => Record(btn, action);
        Grid.SetColumn(btn, 1);
        var clear = new Button { Content = "", FontFamily = (FontFamily)FindResource("IconFont"), Style = (Style)FindResource("GhostButton"), ToolTip = "Убрать" };
        clear.Click += (_, _) => { Settings.Current.Hotkeys[action] = ""; Settings.Save(); Hotkeys.Reload(); btn.Content = "не задано"; };
        Grid.SetColumn(clear, 2);
        g.Children.Add(btn); g.Children.Add(clear);
        return g;
    }

    void Record(Button btn, string action)
    {
        // Free our own combos so they can be re-recorded instead of firing.
        Hotkeys.Suspend();
        btn.Content = "Нажми сочетание или кнопку мыши (колёсико, боковые)… Esc — отмена";
        btn.BorderBrush = (Brush)FindResource("AccentBrush");
        var win = Window.GetWindow(this)!;
        KeyEventHandler? h = null;
        MouseButtonEventHandler? mh = null;
        void Take(string combo)
        {
            var clash = Settings.Current.Hotkeys.FirstOrDefault(kv => kv.Key != action && kv.Value == combo);
            if (clash.Key != null) Settings.Current.Hotkeys[clash.Key] = "";
            Settings.Current.Hotkeys[action] = combo;
            Settings.Save();
            Done(combo);
            if (clash.Key != null) Render();
        }
        h = (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { Done(Settings.Current.Hotkeys.GetValueOrDefault(action, "")); return; }
            var combo = Hotkeys.Format(e, out var why);
            if (why != null) { btn.Content = why; return; }
            if (combo == null) return;   // waiting for a non-modifier key
            Take(combo);
        };
        mh = (_, e) =>
        {
            if (Hotkeys.Format(e.ChangedButton) is not { } combo) return;   // left and right clicks keep working in the window
            e.Handled = true;
            Take(combo);
        };
        void Done(string text)
        {
            win.PreviewKeyDown -= h;
            win.PreviewMouseDown -= mh;
            btn.Content = text.Length == 0 ? "не задано" : Hotkeys.Pretty(text);
            btn.ClearValue(Button.BorderBrushProperty);
            Hotkeys.Reload();
        }
        win.PreviewKeyDown += h;
        win.PreviewMouseDown += mh;
    }

    void Reset_Click(object s, RoutedEventArgs e)
    {
        Settings.Current.Hotkeys.Clear();
        Defaults.Fill(Settings.Current);
        Settings.Save();
        Hotkeys.Reload();
        Render();
    }

    void RenderLaunchers()
    {
        Launchers.Children.Clear();
        var list = Settings.Current.Launchers;
        for (int i = 0; i < list.Count; i++)
        {
            var l = list[i];
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2) };
            var del = new Button { Content = "", FontFamily = (FontFamily)FindResource("IconFont"), Style = (Style)FindResource("GhostButton"), ToolTip = "Удалить" };
            del.Click += (_, _) => { list.Remove(l); Settings.Save(); RenderLaunchers(); };
            var ren = new Button { Content = "", FontFamily = (FontFamily)FindResource("IconFont"), Style = (Style)FindResource("GhostButton"), ToolTip = "Переименовать" };
            ren.Click += (_, _) => { var n = UiKit.Ask("Название", "Как подписать кнопку?", l.Name); if (n.Length > 0) { l.Name = n; Settings.Save(); RenderLaunchers(); } };
            var run = new Button { Content = "", FontFamily = (FontFamily)FindResource("IconFont"), Style = (Style)FindResource("GhostButton"), ToolTip = "Запустить" };
            int idx = i;
            run.Click += (_, _) => Actions.Run("launch:" + idx);
            foreach (var b in new[] { del, ren, run }) { DockPanel.SetDock(b, Dock.Right); row.Children.Add(b); }
            var info = new StackPanel();
            info.Children.Add(UiKit.Text(l.Name, 13, weight: FontWeights.SemiBold));
            info.Children.Add(UiKit.Text(l.Path + (l.Args.Length > 0 ? " " + l.Args : ""), 11, true));
            row.Children.Add(info);
            Launchers.Children.Add(row);
        }
    }

    void Add(string name, string path, string args = "")
    {
        Settings.Current.Launchers.Add(new Launcher { Name = name, Path = path, Args = args });
        Settings.Save();
        RenderLaunchers();
    }

    void AddApp_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFileDialog { Filter = "Программы и ярлыки|*.exe;*.lnk;*.bat;*.cmd;*.url|Все файлы|*.*", DereferenceLinks = false };
        if (d.ShowDialog() == true) Add(Path.GetFileNameWithoutExtension(d.FileName), d.FileName);
    }

    void AddFolder_Click(object s, RoutedEventArgs e)
    {
        var d = new Microsoft.Win32.OpenFolderDialog();
        if (d.ShowDialog() == true) Add(Path.GetFileName(d.FolderName.TrimEnd('\\')) is { Length: > 0 } n ? n : d.FolderName, "explorer.exe", $"\"{d.FolderName}\"");
    }

    void AddUrl_Click(object s, RoutedEventArgs e)
    {
        var url = UiKit.Ask("Сайт", "Адрес сайта", "https://");
        if (url.Length < 9) return;
        if (!url.StartsWith("http")) url = "https://" + url;
        Add(new Uri(url).Host.Replace("www.", ""), url);
    }
}
