using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

/// <summary>Visual scenario editor: list on the left, triggers + steps of the selected one on the right. Saves on every change.</summary>
public partial class ScenariosPage : UserControl
{
    Scenario? _sel;
    static List<Scenario> All => Settings.Current.Scenarios;
    static Style S(string k) => (Style)Application.Current.Resources[k];
    static Brush Br(string k) => (Brush)Application.Current.Resources[k];

    static readonly (string Key, string Name)[] TriggerKinds = { ("voice", "Голосом"), ("hotkey", "Горячей клавишей"), ("time", "По времени"), ("event", "По событию") };
    static readonly string[] Days = { "Пн", "Вт", "Ср", "Чт", "Пт", "Сб", "Вс" };

    public ScenariosPage()
    {
        InitializeComponent();
        _sel = All.FirstOrDefault();
        RenderList();
        RenderEditor();
    }

    static void Save(bool hotkeys = false) { Settings.Save(); if (hotkeys) Hotkeys.Reload(); }

    void RenderList()
    {
        List.Children.Clear();
        foreach (var s in All)
        {
            var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Cursor = Cursors.Hand };
            var sw = new CheckBox { Style = S("Switch"), IsChecked = s.Enabled, Margin = new Thickness(0), ToolTip = "Включён" };
            sw.Click += (_, _) => { s.Enabled = sw.IsChecked == true; Save(true); };
            DockPanel.SetDock(sw, Dock.Right);
            row.Children.Add(sw);
            var name = new Border
            {
                CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 7, 10, 7),
                Background = s == _sel ? Br("AccentSoftBrush") : Brushes.Transparent,
                Child = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = s.Name, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis },
                        new TextBlock { Text = Describe(s), FontSize = 11, Foreground = Br("DimBrush"), TextTrimming = TextTrimming.CharacterEllipsis },
                    },
                },
            };
            name.MouseLeftButtonUp += (_, _) => { _sel = s; RenderList(); RenderEditor(); };
            row.Children.Add(name);
            List.Children.Add(row);
        }
        if (All.Count == 0) List.Children.Add(UiKit.Text("Сценариев пока нет — нажми «Новый сценарий».", 12, true));
    }

    static string Describe(Scenario s) =>
        (s.Triggers.Count == 0 ? "без запуска" : string.Join(", ", s.Triggers.Select(t => t.Kind switch
        {
            "voice" => $"«{t.Value}»", "hotkey" => Hotkeys.Pretty(t.Value), "time" => t.Value,
            _ => Scenarios.Events.FirstOrDefault(e => e.Key == t.Value).Name ?? t.Value,
        }))) + $" · {s.Steps.Count} шаг.";

    void New_Click(object s, RoutedEventArgs e)
    {
        var sc = new Scenario { Name = "Новый сценарий", Triggers = { new() { Kind = "voice", Value = "" } }, Steps = { new() { Kind = "say", Value = "Готово" } } };
        All.Add(sc);
        _sel = sc;
        Save();
        RenderList(); RenderEditor();
    }

    void RenderEditor()
    {
        Editor.Children.Clear();
        if (_sel is not { } sc) return;

        // Header: name + actions
        var head = new StackPanel();
        var name = new TextBox { Text = sc.Name, FontSize = 18, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 10) };
        name.LostFocus += (_, _) => { sc.Name = name.Text.Trim().Length > 0 ? name.Text.Trim() : sc.Name; Save(); RenderList(); };
        head.Children.Add(name);
        var bar = new WrapPanel();
        Btn(bar, "▶ Запустить", async () => await Scenarios.Run(sc), accent: true);
        Btn(bar, "Копия", () =>
        {
            var copy = System.Text.Json.JsonSerializer.Deserialize<Scenario>(System.Text.Json.JsonSerializer.Serialize(sc))!;
            copy.Id = Guid.NewGuid().ToString("N")[..8]; copy.Name += " (копия)";
            All.Add(copy); _sel = copy; Save(); RenderList(); RenderEditor();
        });
        Btn(bar, "Удалить", () =>
        {
            if (!UiKit.Confirm($"Удалить сценарий «{sc.Name}»?")) return;
            All.Remove(sc); _sel = All.FirstOrDefault(); Save(true); RenderList(); RenderEditor();
        });
        head.Children.Add(bar);
        Editor.Children.Add(UiKit.Card(head));

        // Triggers
        var trig = new StackPanel();
        trig.Children.Add(new TextBlock { Text = "Когда запускать", Style = S("H2") });
        foreach (var t in sc.Triggers.ToList()) trig.Children.Add(TriggerRow(sc, t));
        var addT = new WrapPanel();
        foreach (var (k, n) in TriggerKinds)
            Btn(addT, "+ " + n.ToLower(), () => { sc.Triggers.Add(new ScenarioTrigger { Kind = k, Value = k == "time" ? "09:00" : k == "event" ? "startup" : "" }); Save(); RenderEditor(); RenderList(); });
        trig.Children.Add(addT);
        Editor.Children.Add(UiKit.Card(trig));

        // Steps
        var steps = new StackPanel();
        steps.Children.Add(new TextBlock { Text = "Что делать (по порядку)", Style = S("H2") });
        for (int i = 0; i < sc.Steps.Count; i++) steps.Children.Add(StepRow(sc, i));
        Btn(steps, "+ Шаг", () => { sc.Steps.Add(new ScenarioStep { Kind = "action", Value = "dnd" }); Save(); RenderEditor(); RenderList(); }, accent: true);
        steps.Children.Add(UiKit.Text("«Условие»: если не выполнено — дальше шаги не идут. Например «battery» — только от батареи, «time:22:00-07:00» — только ночью, «process:steam» — если запущен Steam.", 11, true));
        Editor.Children.Add(UiKit.Card(steps));
    }

    UIElement TriggerRow(Scenario sc, ScenarioTrigger t)
    {
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var del = Icon("", "Убрать", () => { sc.Triggers.Remove(t); Save(t.Kind == "hotkey"); RenderEditor(); RenderList(); });
        DockPanel.SetDock(del, Dock.Right);
        row.Children.Add(del);
        var label = UiKit.Text(TriggerKinds.First(k => k.Key == t.Kind).Name, 13, true);
        label.Width = 140; label.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(label);

        switch (t.Kind)
        {
            case "voice":
                var tb = new TextBox { Text = t.Value, ToolTip = "Фраза без слова «Джарвис», например «режим кино»" };
                tb.LostFocus += (_, _) => { t.Value = tb.Text.Trim(); Save(); RenderList(); };
                row.Children.Add(tb);
                break;
            case "hotkey":
                var hb = new Button { Content = t.Value.Length > 0 ? Hotkeys.Pretty(t.Value) : "Нажми, чтобы записать", Margin = new Thickness(0), FontFamily = new FontFamily("Consolas, Segoe UI") };
                hb.Click += (_, _) => RecordHotkey(hb, t);
                row.Children.Add(hb);
                break;
            case "time":
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var time = new TextBox { Text = t.Value, Width = 70, Margin = new Thickness(0, 0, 10, 0) };
                time.LostFocus += (_, _) => { if (TimeSpan.TryParse(time.Text, out var ts)) { t.Value = ts.ToString(@"hh\:mm"); Save(); RenderList(); } else time.Text = t.Value; };
                sp.Children.Add(time);
                for (int d = 1; d <= 7; d++)
                {
                    int day = d;
                    var cb = new CheckBox { Content = Days[d - 1], IsChecked = t.Arg.Length == 0 || t.Arg.Contains(day.ToString()), Margin = new Thickness(0, 0, 8, 0) };
                    cb.Click += (_, _) =>
                    {
                        var set = (t.Arg.Length == 0 ? "1234567" : t.Arg).ToHashSet();
                        if (cb.IsChecked == true) set.Add(day.ToString()[0]); else set.Remove(day.ToString()[0]);
                        t.Arg = set.Count == 7 ? "" : new string(set.OrderBy(c => c).ToArray());
                        Save();
                    };
                    sp.Children.Add(cb);
                }
                row.Children.Add(sp);
                break;
            default:
                var ev = new ComboBox { Width = 230 };
                foreach (var (_, n) in Scenarios.Events) ev.Items.Add(n);
                ev.SelectedIndex = Math.Max(0, Array.FindIndex(Scenarios.Events, e => e.Key == t.Value));
                var arg = new TextBox { Text = t.Arg, Margin = new Thickness(8, 0, 0, 0), MinWidth = 120 };
                void ArgHint() => arg.ToolTip = t.Value switch
                {
                    "battery_low" => "Процент, например 20", "process_start" or "process_stop" => "Имя процесса, например cs2 (пусто — любая)", _ => "Не нужно",
                };
                ev.SelectionChanged += (_, _) => { t.Value = Scenarios.Events[ev.SelectedIndex].Key; ArgHint(); Save(); RenderList(); };
                arg.LostFocus += (_, _) => { t.Arg = arg.Text.Trim(); Save(); };
                ArgHint();
                row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Children = { ev, arg } });
                break;
        }
        return row;
    }

    UIElement StepRow(Scenario sc, int i)
    {
        var st = sc.Steps[i];
        var row = new DockPanel { Margin = new Thickness(0, 3, 0, 3) };
        var tools = new StackPanel { Orientation = Orientation.Horizontal };
        tools.Children.Add(Icon("", "Проверить этот шаг", async () => { var ok = await Scenarios.RunStep(st); if (st.Kind == "if") Osd.Show("", ok ? "Условие выполнено" : "Условие НЕ выполнено"); }));
        tools.Children.Add(Icon("", "Выше", () => { if (i == 0) return; (sc.Steps[i - 1], sc.Steps[i]) = (sc.Steps[i], sc.Steps[i - 1]); Save(); RenderEditor(); }));
        tools.Children.Add(Icon("", "Ниже", () => { if (i == sc.Steps.Count - 1) return; (sc.Steps[i + 1], sc.Steps[i]) = (sc.Steps[i], sc.Steps[i + 1]); Save(); RenderEditor(); }));
        tools.Children.Add(Icon("", "Удалить шаг", () => { sc.Steps.RemoveAt(i); Save(); RenderEditor(); RenderList(); }));
        DockPanel.SetDock(tools, Dock.Right);
        row.Children.Add(tools);

        row.Children.Add(new TextBlock { Text = $"{i + 1}.", Width = 26, VerticalAlignment = VerticalAlignment.Center, Foreground = Br("DimBrush") });
        var kind = new ComboBox { Width = 230, Margin = new Thickness(0, 0, 8, 0) };
        foreach (var (_, n, _) in Scenarios.StepKinds) kind.Items.Add(n);
        kind.SelectedIndex = Math.Max(0, Array.FindIndex(Scenarios.StepKinds, k => k.Key == st.Kind));
        kind.SelectionChanged += (_, _) => { st.Kind = Scenarios.StepKinds[kind.SelectedIndex].Key; st.Value = ""; Save(); RenderEditor(); };
        row.Children.Add(kind);

        var hint = Scenarios.StepKinds.First(k => k.Key == st.Kind).Hint;
        if (st.Kind is "action" or "profile")
        {
            var options = st.Kind == "action"
                ? Defaults.HotkeyNames.Where(kv => Defaults.Available(kv.Key)).Select(kv => (kv.Key, kv.Value)).Concat(CommandPalette.Extra.Select(kv => (kv.Key, kv.Value))).ToList()
                : Settings.Current.Profiles.Select(p => (p.Name, p.Name)).ToList();
            var cb = new ComboBox();
            foreach (var (_, n) in options) cb.Items.Add(n);
            cb.SelectedIndex = options.FindIndex(o => o.Item1 == st.Value);
            cb.SelectionChanged += (_, _) => { if (cb.SelectedIndex >= 0) { st.Value = options[cb.SelectedIndex].Item1; Save(); } };
            row.Children.Add(cb);
        }
        else
        {
            var tb = new TextBox { Text = st.Value, ToolTip = hint };
            tb.LostFocus += (_, _) => { st.Value = tb.Text; Save(); };
            row.Children.Add(tb);
        }
        return row;
    }

    void RecordHotkey(Button btn, ScenarioTrigger t)
    {
        Hotkeys.Suspend();
        btn.Content = "Нажми сочетание… (Esc — отмена)";
        var win = Window.GetWindow(this)!;
        KeyEventHandler? h = null;
        h = (_, e) =>
        {
            e.Handled = true;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { Done(); return; }
            var combo = Hotkeys.Format(e);
            if (combo == null) return;
            t.Value = combo;
            Done();
        };
        void Done()
        {
            win.PreviewKeyDown -= h;
            btn.Content = t.Value.Length > 0 ? Hotkeys.Pretty(t.Value) : "Нажми, чтобы записать";
            Save(true);
            RenderList();
        }
        win.PreviewKeyDown += h;
    }

    static Button Btn(Panel p, string text, Action click, bool accent = false)
    {
        var b = new Button { Content = text };
        if (accent) b.Style = S("AccentButton");
        b.Click += (_, _) => { try { click(); } catch (Exception e) { Osd.Show("", e.Message); } };
        p.Children.Add(b);
        return b;
    }

    static Button Icon(string glyph, string tip, Action click)
    {
        var b = new Button { Content = glyph, FontFamily = (FontFamily)Application.Current.Resources["IconFont"], Style = S("GhostButton"), ToolTip = tip };
        b.Click += (_, _) => { try { click(); } catch (Exception e) { Osd.Show("", e.Message); } };
        return b;
    }
}
