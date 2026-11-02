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
        Height = Math.Min(840, SystemParameters.WorkArea.Height - 24);   // tall enough for the whole menu, never taller than the screen
        // Maximized WindowChrome windows overhang the screen by the resize border.
        StateChanged += (_, _) => Root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
        ApplySections();
        NavHome.IsChecked = true;
        Loaded += (_, _) => { if (!Settings.Current.SetupDone && !App.DevRun) SetupWizard.Open(); };
    }

    /// <summary>What this edition has, minus what the user hid in the setup. Home and Settings always stay.</summary>
    public void ApplySections()
    {
        foreach (var rb in NavPanel.Children.OfType<RadioButton>())
        {
            var name = rb.Name[3..];
            bool has = name switch
            {
                "Jarvis" or "Scenarios" => Edition.HasJarvis,
                "Secretary" => Edition.HasSecretary,
                "Nitro" => Acer.Available,
                _ => true,
            };
            rb.Visibility = has && !Settings.Current.HiddenSections.Contains(name) ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    public void Navigate(string page)
    {
        if (FindName("Nav" + page) is RadioButton rb) { rb.IsChecked = false; rb.IsChecked = true; }
    }

    void Nav_Checked(object sender, RoutedEventArgs e)
    {
        var name = ((FrameworkElement)sender).Name[3..];
        FrameworkElement content;
        try
        {
        content = name switch
        {
            "Home" => new HomePage(),
            "Controls" => new ControlsPage(),
            "System" => new SystemPage(),
            "Devices" => new DevicesPage(),
            "Nitro" => new NitroPage(),
            "Boost" => new BoostPage(),
            "Security" => new SecurityPage(),
            "Tools" => new ToolsPage(),
#if JARVIS
            "Jarvis" => new JarvisPage(),
            "Scenarios" => new ScenariosPage(),
#endif
            "Profiles" => new ProfilesPage(),
            "Hotkeys" => new HotkeysPage(),
            "Radial" => new RadialPage(),
            "Phone" => new PhonePage(),
            "Support" => new SupportPage(),
#if SECRETARY
            "Secretary" => new SecretaryPage(),
#endif
            "Widgets" => new WidgetsPage(),
            "Settings" => new SettingsPage(),
            _ => new TextBlock { Text = "?" },
        };
        }
        catch (Exception ex)
        {
            // A missing Windows component must not look like a dead button.
            Log.Error(ex);
            content = new TextBlock { Text = "Этот раздел не открылся на этом компьютере.\nПодробности в журнале: Настройки → «Лог ошибок». Его можно прислать на странице «Поддержка».", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 20, 0, 0), Foreground = (System.Windows.Media.Brush)FindResource("TextBrush") };
        }
        Page.Content = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 12, 16),
        };
    }

    void Features_Click(object s, RoutedEventArgs e) => Features.Open();
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
