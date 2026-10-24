using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using NAudio.CoreAudioApi;
using Windows.Devices.Radios;

namespace MishkaHelper.Core;

/// <summary>Small builders shared by pages, quick panel and widgets.</summary>
public static class UiKit
{
    public static ToggleButton Tile(string icon, string label, Func<Task<bool>> get, Func<bool, Task> set)
    {
        var sp = new StackPanel();
        sp.Children.Add(new TextBlock { Text = icon, FontFamily = (FontFamily)Application.Current.Resources["IconFont"], FontSize = 20 });
        sp.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 8, 0, 0), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis });
        var t = new ToggleButton { Style = (Style)Application.Current.Resources["Tile"], Content = sp };
        bool busy = false;
        async void Refresh() { busy = true; try { t.IsChecked = await get(); } catch { t.IsEnabled = false; } busy = false; }
        t.Checked += async (_, _) => { if (!busy) await Safe(() => set(true)); };
        t.Unchecked += async (_, _) => { if (!busy) await Safe(() => set(false)); };
        t.Loaded += (_, _) => Refresh();
        return t;
    }

    static async Task Safe(Func<Task> f) { try { await f(); } catch (Exception e) { Log.Error(e); Osd.Show("", e.Message); } }

    /// <summary>Standard tiles used in the quick panel and Controls page.</summary>
    public static IEnumerable<ToggleButton> StandardTiles()
    {
        yield return Tile("", "Wi-Fi", async () => await Radios.Get(RadioKind.WiFi) ?? false, on => Radios.Set(RadioKind.WiFi, on));
        yield return Tile("", "Bluetooth", async () => await Radios.Get(RadioKind.Bluetooth) ?? false, on => Radios.Set(RadioKind.Bluetooth, on));
        yield return Tile("", "В самолёте", Radios.Airplane, Radios.SetAirplane);
        yield return Tile("", "Не беспокоить", () => Task.FromResult(Tweaks.DoNotDisturb), on => { Tweaks.DoNotDisturb = on; return Task.CompletedTask; });
        yield return Tile("", "Ночной свет", () => Task.FromResult(Display.NightLight ?? false), on => { Display.NightLight = on; return Task.CompletedTask; });
        yield return Tile("", "Микрофон", () => Task.FromResult(!Audio.MicMuted), on => { Audio.MicMuted = !on; return Task.CompletedTask; });
    }

    public static WrapPanel PowerModes(Action? after = null)
    {
        var wp = new WrapPanel();
        if (Acer.Available)
        {
            int cur = Acer.Mode;
            foreach (var (id, name) in Acer.SupportedModes())
            {
                var rb = new RadioButton { Style = (Style)Application.Current.Resources["Segment"], Content = name, GroupName = "acer" + wp.GetHashCode(), IsChecked = id == cur };
                rb.Checked += (_, _) => { Acer.Mode = id; Osd.Show("", "Режим: " + name); after?.Invoke(); };
                wp.Children.Add(rb);
            }
        }
        else
        {
            var cur = Power.Mode;
            foreach (var (key, name, _) in Power.Modes)
            {
                var rb = new RadioButton { Style = (Style)Application.Current.Resources["Segment"], Content = name, GroupName = "pm" + wp.GetHashCode(), IsChecked = key == cur };
                rb.Checked += (_, _) => { Power.Mode = key; after?.Invoke(); };
                wp.Children.Add(rb);
            }
        }
        return wp;
    }

    public static WrapPanel ProfileButtons()
    {
        var wp = new WrapPanel();
        foreach (var p in Settings.Current.Profiles)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(new TextBlock { Text = p.Icon, FontFamily = (FontFamily)Application.Current.Resources["IconFont"], Margin = new Thickness(0, 0, 6, 0), VerticalAlignment = VerticalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = p.Name });
            var b = new Button { Content = sp, ToolTip = "Применить профиль" };
            if (Profiles.Active == p.Name) b.BorderBrush = (Brush)Application.Current.Resources["AccentBrush"];
            b.Click += (_, _) => Profiles.Apply(p);
            wp.Children.Add(b);
        }
        return wp;
    }

    public static void FillOutputs(ComboBox cb)
    {
        var list = Audio.Devices(DataFlow.Render);
        cb.Tag = "busy";
        cb.ItemsSource = list;
        cb.SelectedItem = list.FirstOrDefault(d => d.IsDefault);
        cb.Tag = null;
    }

    public static TextBlock Text(string s, double size = 13, bool dim = false, FontWeight? weight = null) => new()
    {
        Text = s, FontSize = size, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap,
        Foreground = (Brush)Application.Current.Resources[dim ? "DimBrush" : "TextBrush"],
    };

    public static TextBlock Icon(string glyph, double size = 18) => new()
    {
        Text = glyph, FontSize = size, VerticalAlignment = VerticalAlignment.Center,
        FontFamily = (FontFamily)Application.Current.Resources["IconFont"],
    };

    public static Border Card(UIElement child) => new() { Style = (Style)Application.Current.Resources["Card"], Child = child };

    /// <summary>A grey hint inside an empty text box ("Найди функцию…"), gone as soon as you type.</summary>
    public static Grid WithHint(TextBox box, string hint)
    {
        var h = new TextBlock
        {
            Text = hint, IsHitTestVisible = false, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.55,
            Margin = new Thickness(box.Padding.Left + 4, 0, 0, 0), FontSize = box.FontSize,
            Foreground = (Brush)Application.Current.Resources["DimBrush"], TextTrimming = TextTrimming.CharacterEllipsis,
        };
        box.TextChanged += (_, _) => h.Visibility = box.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        return new Grid { Children = { box, h } };
    }

    /// <summary>
    /// Search-as-you-type list of every command (functions, pages, profiles, programs, widgets, scenarios):
    /// all typed words must appear anywhere in the name ("защ" finds «Открыть: Защита»).
    /// </summary>
    public static (FrameworkElement View, Func<(string Icon, string Name, string Action)?> Selected, Action Reset) ActionPicker(string hint = "Начни печатать: защита, профиль, громкость…")
    {
        var all = CommandPalette.Commands();
        var box = new TextBox { Padding = new Thickness(8, 6, 8, 6) };
        var list = new ListBox { MaxHeight = 190, Margin = new Thickness(0, 4, 0, 0) };
        void Filter()
        {
            var words = box.Text.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            list.Items.Clear();
            foreach (var c in all.Where(c => words.All(w => c.Name.ToLowerInvariant().Contains(w))).Take(60))
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Tag = c };
                var ic = Icon(c.Icon, 14); ic.Width = 26; ic.Foreground = (Brush)Application.Current.Resources["AccentBrush"];
                row.Children.Add(ic);
                row.Children.Add(new TextBlock { Text = c.Name, VerticalAlignment = VerticalAlignment.Center });
                list.Items.Add(row);
            }
            if (list.Items.Count > 0 && words.Length > 0) list.SelectedIndex = 0;
        }
        box.TextChanged += (_, _) => Filter();
        Filter();
        var sp = new StackPanel();
        sp.Children.Add(WithHint(box, hint));
        sp.Children.Add(list);
        return (sp,
            () => (list.SelectedItem as FrameworkElement)?.Tag is ValueTuple<string, string, string> t ? t : null,
            () => { box.Text = ""; list.SelectedIndex = -1; });
    }

    public static string Ask(string title, string prompt, string value = "")
    {
        var tb = new TextBox { Text = value, Margin = new Thickness(0, 10, 0, 12), MinWidth = 300 };
        var ok = new Button { Content = "OK", Style = (Style)Application.Current.Resources["AccentButton"], IsDefault = true };
        var cancel = new Button { Content = "Отмена", IsCancel = true };
        var sp = new StackPanel { Margin = new Thickness(20) };
        sp.Children.Add(Text(title, 16, weight: FontWeights.SemiBold));
        sp.Children.Add(Text(prompt, 12, true));
        sp.Children.Add(tb);
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        row.Children.Add(cancel); row.Children.Add(ok);
        sp.Children.Add(row);
        var w = new Window
        {
            Content = sp, SizeToContent = SizeToContent.WidthAndHeight, WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = (Brush)Application.Current.Resources["CardBrush"], Foreground = (Brush)Application.Current.Resources["TextBrush"],
            ResizeMode = ResizeMode.NoResize, Title = title, Topmost = true, WindowStyle = WindowStyle.ToolWindow,
        };
        w.SourceInitialized += (_, _) => ThemeManager.ApplyBackdrop(w, false);
        string? result = null;
        ok.Click += (_, _) => { result = tb.Text; w.Close(); };
        w.Loaded += (_, _) => { tb.Focus(); tb.SelectAll(); };
        w.ShowDialog();
        return result ?? "";
    }

    public static bool Confirm(string text) =>
        MessageBox.Show(text, "Mishka Helper", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
}
