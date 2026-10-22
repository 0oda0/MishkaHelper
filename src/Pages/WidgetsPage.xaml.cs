using System.Windows;
using System.Windows.Controls;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

public partial class WidgetsPage : UserControl
{
    static readonly Dictionary<string, string> Icons = new()
    {
        ["system"] = "", ["battery"] = "", ["clock"] = "", ["network"] = "",
        ["profiles"] = "", ["audio"] = "", ["launcher"] = "",
        ["notes"] = "", ["todo"] = "", ["timer"] = "", ["media"] = "", ["ping"] = "", ["mic"] = "",
        ["disks"] = "", ["calendar"] = "", ["currency"] = "", ["procs"] = "", ["fans"] = "",
        ["worldclock"] = "", ["countdown"] = "",
    };

    public WidgetsPage()
    {
        InitializeComponent();
        City.Text = Settings.Current.WeatherCity;
        foreach (var (kind, name) in Widgets.Names) List.Children.Add(Card(kind, name));
    }

    UIElement Card(string kind, string name)
    {
        var cfg = Settings.Current.Widgets.FirstOrDefault(w => w.Kind == kind) ?? new WidgetConfig { Kind = kind };
        var sp = new StackPanel { Width = 250 };
        var head = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
        var ic = UiKit.Icon(Icons.GetValueOrDefault(kind, ""), 22);
        ic.Foreground = (System.Windows.Media.Brush)FindResource("AccentBrush");
        ic.Margin = new Thickness(0, 0, 10, 0);
        head.Children.Add(ic);
        head.Children.Add(UiKit.Text(name, 13, weight: FontWeights.SemiBold));
        sp.Children.Add(head);

        var show = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Показывать", IsChecked = Widgets.IsOpen(kind) };
        show.Click += (_, _) => Widgets.Set(kind, show.IsChecked == true);
        var top = new CheckBox { Style = (Style)FindResource("Switch"), Content = "Поверх всех окон", IsChecked = cfg.Topmost };
        top.Click += (_, _) => { cfg.Topmost = top.IsChecked == true; Settings.Save(); Widgets.Refresh(kind); };
        var op = new Slider { Minimum = 20, Maximum = 100, Value = cfg.Opacity * 100, Margin = new Thickness(0, 4, 0, 0) };
        op.ValueChanged += (_, e) => Widgets.SetOpacity(kind, e.NewValue / 100);
        op.LostMouseCapture += (_, _) => Settings.Save();
        sp.Children.Add(show); sp.Children.Add(top);
        sp.Children.Add(UiKit.Text("Непрозрачность", 12, true));
        sp.Children.Add(op);
        return UiKit.Card(sp);
    }

    void City_Click(object s, RoutedEventArgs e)
    {
        Settings.Current.WeatherCity = City.Text.Trim();
        Settings.Save();
        Widgets.Refresh("clock");
    }
}
