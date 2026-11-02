using System.Windows;
using System.Windows.Controls;
using MishkaHelper.Pages;

namespace MishkaHelper.Core;

/// <summary>
/// First-run setup, and again whenever wanted (palette, Settings): which sections to keep, hotkeys, the secretary.
/// Skippable at every step; nothing here is required.
/// </summary>
public static class SetupWizard
{
    static Window? _w;

    static readonly (string Key, string Name, string Hint)[] Sections =
    {
        ("Jarvis", "Джарвис", "голосовой помощник"), ("Scenarios", "Сценарии", "цепочки действий"), ("Secretary", "Секретарша", "заметки, сводки, почта"),
        ("Controls", "Управление", "звук, яркость, сеть"), ("System", "Система", "датчики, диски, история"), ("Devices", "Устройства", "мышь, наушники, Bluetooth"),
        ("Phone", "Телефон", "приложение для Android"), ("Boost", "Ускорение", "очистка и автозагрузка"), ("Security", "Защита", "Defender, проверка файлов"),
        ("Tools", "Инструменты", "окна, таймеры, пульт"), ("Profiles", "Профили", "режимы одной кнопкой"), ("Hotkeys", "Горячие клавиши", ""),
        ("Radial", "Колесо", "меню у курсора"), ("Widgets", "Виджеты", "на рабочем столе"), ("Support", "Поддержка", "чат с разработчиком"),
    };

    internal static bool Applies(string key) => key switch
    {
        "Jarvis" or "Scenarios" => Edition.HasJarvis,
        "Secretary" => Edition.HasSecretary,
        _ => true,
    };

    public static void Open()
    {
        if (_w != null) { _w.Activate(); return; }
        var w = new Window
        {
            Title = "Настройка Mishka Helper", Width = 760, Height = Math.Min(720, SystemParameters.WorkArea.Height - 40), WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = (System.Windows.Media.Brush)Application.Current.Resources["CardBrush"], Foreground = (System.Windows.Media.Brush)Application.Current.Resources["TextBrush"],
            FontFamily = (System.Windows.Media.FontFamily)Application.Current.Resources["MainFont"],
        };
        w.Closed += (_, _) => { _w = null; Settings.Current.SetupDone = true; Settings.Save(); Hotkeys.Reload(); };
        _w = w;

        var host = new ContentControl();
        var back = new Button { Content = "Назад", Margin = new Thickness(0, 0, 8, 0) };
        var next = new Button { Content = "Дальше", Style = (Style)Application.Current.Resources["AccentButton"] };
        var skip = new Button { Content = "Пропустить настройку", Style = (Style)Application.Current.Resources["GhostButton"], HorizontalAlignment = HorizontalAlignment.Left };
        skip.Click += (_, _) => w.Close();

        var steps = new List<(string Title, Func<UIElement> Build)>
        {
            ("Какие разделы тебе нужны?", SectionsStep),
            ("Горячие клавиши", () => new HotkeysPage()),
        };
#if SECRETARY
        steps.Add(("Секретарша", SecretaryStep));
#endif
        int i = 0;
        void Show()
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = $"Шаг {i + 1} из {steps.Count}", Style = (Style)Application.Current.Resources["Dim"] });
            sp.Children.Add(new TextBlock { Text = steps[i].Title, Style = (Style)Application.Current.Resources["H1"], Margin = new Thickness(0, 0, 0, 12) });
            sp.Children.Add(steps[i].Build());
            host.Content = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            back.IsEnabled = i > 0;
            next.Content = i == steps.Count - 1 ? "Готово" : "Дальше";
        }
        back.Click += (_, _) => { i--; Show(); };
        next.Click += (_, _) => { if (i == steps.Count - 1) w.Close(); else { i++; Show(); } };

        var bar = new DockPanel { Margin = new Thickness(0, 12, 0, 0) };
        var right = new StackPanel { Orientation = Orientation.Horizontal };
        right.Children.Add(back); right.Children.Add(next);
        DockPanel.SetDock(right, Dock.Right);
        bar.Children.Add(right); bar.Children.Add(skip);
        var root = new DockPanel { Margin = new Thickness(20) };
        DockPanel.SetDock(bar, Dock.Bottom);
        root.Children.Add(bar); root.Children.Add(host);
        w.Content = root;
        Show();
        w.SourceInitialized += (_, _) => ThemeManager.ApplyBackdrop(w);
        w.Show();
    }

    static UIElement SectionsStep()
    {
        var sp = new StackPanel();
        sp.Children.Add(UiKit.Text("Лишнее можно убрать из меню. Ничего не удаляется: вернуть можно здесь же, настройка открывается из палитры (Ctrl+Alt+K) и в «Настройках».", 13, true));
        foreach (var (key, name, hint) in Sections.Where(s => Applies(s.Key)))
        {
            var cb = new CheckBox { Style = (Style)Application.Current.Resources["Switch"], Content = hint.Length > 0 ? $"{name} — {hint}" : name, IsChecked = !Settings.Current.HiddenSections.Contains(key) };
            cb.Click += (_, _) =>
            {
                Settings.Current.HiddenSections.Remove(key);
                if (cb.IsChecked != true) Settings.Current.HiddenSections.Add(key);
                Settings.Save();
                (Application.Current.Windows.OfType<MainWindow>().FirstOrDefault())?.ApplySections();
            };
            sp.Children.Add(cb);
        }
        return sp;
    }

#if SECRETARY
    static UIElement SecretaryStep()
    {
        var s = Settings.Current.Secretary;
        var sp = new StackPanel();
        sp.Children.Add(UiKit.Text("Помнит заметки к программам, адрес расписания, сроки из писем и даёт сводку на день. Всё хранится на этом ПК.", 13, true));
        var on = new CheckBox { Style = (Style)Application.Current.Resources["Switch"], Content = "Включить секретаршу", IsChecked = s.Enabled, Margin = new Thickness(0, 8, 0, 8) };
        on.Click += (_, _) => { s.Enabled = on.IsChecked == true; Settings.Save(); Secretary.Apply(); };
        sp.Children.Add(on);
        foreach (var (name, hint, get, set) in SecretaryPage.Features)
        {
            var cb = new CheckBox { Style = (Style)Application.Current.Resources["Switch"], Content = name, IsChecked = get(), ToolTip = hint };
            cb.Click += (_, _) => { set(cb.IsChecked == true); Settings.Save(); Secretary.Apply(); };
            sp.Children.Add(cb);
        }
        return sp;
    }
#endif
}
