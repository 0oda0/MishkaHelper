using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using MishkaHelper.Core;
using MishkaHelper.Pages;

namespace MishkaHelper;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => ThemeManager.ApplyBackdrop(this);
        // Maximized WindowChrome windows overhang the screen by the resize border.
        StateChanged += (_, _) => Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        if (Acer.Available) NavNitro.Visibility = Visibility.Visible;
        NavHome.IsChecked = true;
    }

    public void Navigate(string page)
    {
        if (FindName("Nav" + page) is RadioButton rb) { rb.IsChecked = false; rb.IsChecked = true; }
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        var name = ((FrameworkElement)sender).Name[3..];
        FrameworkElement content = name switch
        {
            "Home" => new HomePage(),
            "Controls" => new ControlsPage(),
            "System" => new SystemPage(),
            "Devices" => new DevicesPage(),
            "Nitro" => new NitroPage(),
            "Boost" => new BoostPage(),
            "Security" => new SecurityPage(),
            "Profiles" => new ProfilesPage(),
            "Hotkeys" => new HotkeysPage(),
            "Widgets" => new WidgetsPage(),
            "Settings" => new SettingsPage(),
            _ => new TextBlock { Text = "?" },
        };
        Page.Content = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 12, 16),
        };
    }

    void Min_Click(object s, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    void Max_Click(object s, RoutedEventArgs e) => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    void Close_Click(object s, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        Page.Content = null;   // stop page timers
        if (!Settings.Current.CloseToTray) App.Current.Quit();
        base.OnClosing(e);
    }
}
