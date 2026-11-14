using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

/// <summary>«Игры»: the game helper's bases — build, open, rebuild, delete; the brain it needs; adding a game by name or by its window.</summary>
public class GamesPage : UserControl
{
    readonly StackPanel _bases = new();
    static Style S(string k) => (Style)Application.Current.Resources[k];

    public GamesPage()
    {
        var root = new StackPanel();
        root.Children.Add(new TextBlock { Text = "Игры", Style = S("H1") });
        var hk = Hotkeys.Pretty(Settings.Current.Hotkeys.GetValueOrDefault("gameKb", ""));
        root.Children.Add(new TextBlock
        {
            Text = $"База знаний по каждой игре на русском: этапы и сюжет (без спойлеров дальше твоего этапа), локации, боссы, задания, предметы, проблемы и решения. " +
                   $"Открывается в игре по {(hk.Length > 0 ? hk : "горячей клавише «Игровой помощник» (задай на странице «Горячие клавиши»)")} — только в той игре, к которой собрана.",
            Style = S("Dim"), FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18),
        });

        var (setup, sb) = Card("Как собирается");
        sb.Children.Add(UiKit.Text("Mishka читает Steam, Википедию, вики игры и PCGamingWiki, а нейросеть раскладывает это по-русски. " +
            "Первая сборка — 5–15 минут на игру, играть можно. Не нашлось в базе — поиск в окне базы сам поищет на вики и допишет.", 12, true));
        var offer = new CheckBox { Style = S("Switch"), Content = "Предлагать собрать базу, когда запускаю новую игру", IsChecked = Settings.Current.GameOffers, Margin = new Thickness(0, 8, 0, 0) };
        offer.Click += (_, _) => { Settings.Current.GameOffers = offer.IsChecked == true; Settings.Save(); };
        sb.Children.Add(offer);
        sb.Children.Add(UiKit.Text(GameBuilder.BrainReady
            ? (Online.Active ? $"Нейросеть: {new Uri(Online.Url).Host} ✓" : "Нейросеть: локальная модель Джарвиса ✓ (онлайн-ключ даст русский текст получше)")
            : "Нужна нейросеть: подключи бесплатный ключ ниже — это 2 минуты.", 12, !GameBuilder.BrainReady));
        root.Children.Add(setup);
        if (!Online.Active) root.Children.Add(JarvisPage.OnlineCard());

        var (add, ab) = Card("Добавить игру");
        var name = new TextBox { Width = 320, Padding = new Thickness(8, 6, 8, 6), Margin = new Thickness(0, 0, 8, 0) };
        var byName = new Button { Content = "Собрать базу", Style = S("AccentButton") };
        byName.Click += async (_, _) =>
        {
            var n = name.Text.Trim();
            if (n.Length < 2) return;
            byName.IsEnabled = false;
            // A Steam id makes it the same base the hotkey finds when the game runs from Steam.
            var id = await GameSources.SteamFind(n);
            byName.IsEnabled = true;
            _ = GameBuilder.Build(id > 0 ? new GameKb.Game("steam-" + id, n, "", id, "") : GameKb.Manual("", n) with { Key = GameKb.Key(n) });
            name.Text = "";
        };
        var row = new WrapPanel();
        row.Children.Add(UiKit.WithHint(name, "Название игры: Baldur's Gate 3")); row.Children.Add(byName);
        ab.Children.Add(row);
        ab.Children.Add(UiKit.Text("Игра не из Steam, Epic или GOG? Запусти её и выбери её окно:", 12, true));
        var procs = new ComboBox { Width = 320, Margin = new Thickness(0, 4, 8, 0) };
        void FillProcs()
        {
            procs.Items.Clear();
            foreach (var p in Process.GetProcesses().Where(p => { try { return p.MainWindowHandle != IntPtr.Zero && p.MainWindowTitle.Length > 0 && p.Id != Environment.ProcessId; } catch { return false; } })
                         .OrderBy(p => p.ProcessName))
                procs.Items.Add(new ComboBoxItem { Content = $"{p.MainWindowTitle} ({p.ProcessName}.exe)", Tag = (p.ProcessName, p.MainWindowTitle) });
        }
        procs.DropDownOpened += (_, _) => FillProcs();
        var byWin = new Button { Content = "Собрать для этого окна", Margin = new Thickness(0, 4, 0, 0) };
        byWin.Click += (_, _) =>
        {
            if (procs.SelectedItem is not ComboBoxItem { Tag: ValueTuple<string, string> t }) return;
            _ = GameBuilder.Build(GameKb.Manual(t.Item1, t.Item2));
        };
        var row2 = new WrapPanel();
        row2.Children.Add(procs); row2.Children.Add(byWin);
        ab.Children.Add(row2);
        root.Children.Add(add);

        var (bases, bb) = Card("Базы");
        bb.Children.Add(_bases);
        root.Children.Add(bases);
        Content = root;
        Render();
        GameKb.Changed += Render;
        Unloaded += (_, _) => GameKb.Changed -= Render;
    }

    static (Border, StackPanel) Card(string title)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = title, Style = S("H2") });
        return (UiKit.Card(sp), sp);
    }

    void Render()
    {
        _bases.Children.Clear();
        var all = GameKb.All();
        if (all.Count == 0) { _bases.Children.Add(UiKit.Text("Пока нет ни одной базы. Добавь игру выше или просто запусти её.", 13, true)); return; }
        foreach (var b in all)
        {
            var row = new DockPanel { Margin = new Thickness(0, 6, 0, 6) };
            var btns = new StackPanel { Orientation = Orientation.Horizontal };
            Button Btn(string text, Action a, bool accent = false)
            {
                var x = new Button { Content = text, Margin = new Thickness(6, 0, 0, 0) };
                if (accent) x.Style = S("AccentButton");
                x.Click += (_, _) => a();
                btns.Children.Add(x);
                return x;
            }
            bool running = GameBuilder.IsRunning(b.Key);
            Btn("Открыть", () => GameKbWindow.Open(b, IntPtr.Zero), true);
            if (!running) Btn(b.Built == default ? "Дособрать" : "Обновить", () => _ = GameBuilder.Build(new GameKb.Game(b.Key, b.Name, b.Exe.FirstOrDefault() ?? "", b.AppId, "")));
            if (!running) Btn("Удалить", () => { if (UiKit.Confirm($"Удалить базу «{b.Name}»?")) GameKb.Delete(b); });
            DockPanel.SetDock(btns, Dock.Right);
            row.Children.Add(btns);
            var info = new StackPanel();
            info.Children.Add(UiKit.Text(b.Name, 14, weight: FontWeights.SemiBold));
            var counts = string.Join(", ", GameKb.Kinds.Select(k => (k.Name, N: b.Entries.Count(e => e.Kind == k.Key))).Where(x => x.N > 0).Select(x => $"{x.Name.ToLowerInvariant()} {x.N}"));
            info.Children.Add(UiKit.Text(running || b.Status.Length > 0 ? b.Status : $"{counts} · собрана {b.Built:dd.MM.yyyy}", 11, true));
            if (running) info.Children.Add(new ProgressBar { Maximum = 1, Value = b.Done, Height = 4, Margin = new Thickness(0, 4, 0, 0), Width = 300, HorizontalAlignment = HorizontalAlignment.Left });
            row.Children.Add(info);
            _bases.Children.Add(row);
        }
    }
}
