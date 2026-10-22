using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace MishkaHelper.Core;

/// <summary>Ctrl+Alt+K: type to find any action, page, profile or program, Enter to run.</summary>
public static class CommandPalette
{
    record Cmd(string Icon, string Name, string Action, string Hint);

    static Window? _w;

    public static readonly Dictionary<string, string> Extra = new()
    {
        ["desktopIcons"] = "Скрыть/показать значки рабочего стола",
        ["taskbar"] = "Автоскрытие панели задач",
        ["dark"] = "Светлая/тёмная тема Windows",
        ["sysinfo"] = "Скопировать сведения о ПК",
        ["ping"] = "Проверить пинг",
        ["password"] = "Сгенерировать пароль в буфер",
        ["screenshot"] = "Снимок экрана",
        ["taskmgr"] = "Диспетчер задач",
        ["features"] = "Что можно сделать (все функции)",
    };

    static readonly (string Page, string Name)[] Pages =
    {
        ("Home", "Главная"), ("Controls", "Управление"), ("System", "Система"), ("Devices", "Устройства"), ("Nitro", "Nitro"),
        ("Boost", "Ускорение"), ("Security", "Защита"), ("Tools", "Инструменты"), ("Profiles", "Профили"), ("Hotkeys", "Горячие клавиши"),
        ("Widgets", "Виджеты"), ("Settings", "Настройки"),
    };

    static List<Cmd> All()
    {
        var hk = Settings.Current.Hotkeys;
        var l = Defaults.HotkeyNames.Where(kv => Defaults.Available(kv.Key)).Select(kv => new Cmd("", kv.Value, kv.Key, Hotkeys.Pretty(hk.GetValueOrDefault(kv.Key, "")))).ToList();
        l.AddRange(Extra.Select(kv => new Cmd("", kv.Value, kv.Key, "")));
        l.AddRange(Pages.Select(p => new Cmd("", "Открыть: " + p.Name, "page:" + p.Page, "")));
        l.AddRange(Settings.Current.Profiles.Select(p => new Cmd(p.Icon, "Профиль: " + p.Name, "profile:" + p.Name, "")));
        l.AddRange(Settings.Current.Launchers.Select((x, i) => new Cmd("", "Запустить: " + x.Name, "launch:" + i, "")));
        l.AddRange(Widgets.Names.Select(kv => new Cmd("", "Виджет: " + kv.Value, "widget:" + kv.Key, "")));
        return l;
    }

    public static void Toggle()
    {
        if (_w != null) { _w.Close(); return; }
        var w = Floating.Create(clickThrough: false, noActivate: false);
        var box = new TextBox { FontSize = 18, MinWidth = 560, Padding = new Thickness(12, 10, 12, 10), BorderThickness = new Thickness(0) };
        var list = new ListBox { MaxHeight = 380, Margin = new Thickness(0, 8, 0, 0) };
        var sp = new StackPanel();
        sp.Children.Add(box); sp.Children.Add(list);
        var panel = Floating.Panel(sp, 16);
        panel.Padding = new Thickness(12);
        w.Content = panel;
        var all = All();

        void Filter()
        {
            var words = box.Text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            list.Items.Clear();
            foreach (var c in all.Where(c => words.All(wd => c.Name.ToLowerInvariant().Contains(wd))).Take(40))
            {
                var row = new DockPanel { Tag = c };
                var hint = UiKit.Text(c.Hint, 11, true); DockPanel.SetDock(hint, Dock.Right); hint.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(hint);
                var ic = UiKit.Icon(c.Icon, 15); ic.Width = 28; ic.Foreground = (Brush)Application.Current.Resources["AccentBrush"];
                row.Children.Add(ic);
                row.Children.Add(new TextBlock { Text = c.Name, FontSize = 14, VerticalAlignment = VerticalAlignment.Center });
                list.Items.Add(row);
            }
            list.SelectedIndex = 0;
        }

        void Run()
        {
            if (list.SelectedItem is not FrameworkElement { Tag: Cmd c }) return;
            w.Close();
            if (c.Action.StartsWith("widget:")) Widgets.Set(c.Action[7..], !Widgets.IsOpen(c.Action[7..]));
            else Actions.Run(c.Action);
        }

        box.TextChanged += (_, _) => Filter();
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down) { list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1); list.ScrollIntoView(list.SelectedItem); e.Handled = true; }
            else if (e.Key == Key.Up) { list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); list.ScrollIntoView(list.SelectedItem); e.Handled = true; }
            else if (e.Key == Key.Enter) { Run(); e.Handled = true; }
            else if (e.Key == Key.Escape) w.Close();
        };
        list.MouseDoubleClick += (_, _) => Run();
        bool closing = false;
        w.Closing += (_, _) => closing = true;
        w.Deactivated += (_, _) => { if (!closing) w.Close(); };
        w.Closed += (_, _) => _w = null;
        Filter();
        _w = w;
        w.Show();
        var wa = SystemParameters.WorkArea;
        w.Left = wa.Left + (wa.Width - w.ActualWidth) / 2;
        w.Top = wa.Top + wa.Height * 0.18;
        Native.SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(w).Handle);
        w.Activate();
        box.Focus();
    }
}
