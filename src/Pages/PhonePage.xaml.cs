using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MishkaHelper.Core;

namespace MishkaHelper.Pages;

/// <summary>The phone app's side on the PC: status, ring, send files/clipboard, its notifications, how to install it.</summary>
public partial class PhonePage : UserControl
{
    public const string ApkUrl = "https://github.com/0oda0/MishkaHelper/releases/latest/download/Mishka-Phone.apk";
    static Style S(string k) => (Style)Application.Current.Resources[k];

    public PhonePage()
    {
        InitializeComponent();
        Render();
        Phone.Changed += Render;
        Unloaded += (_, _) => Phone.Changed -= Render;
    }

    static (Border Card, StackPanel Body) Card(string title, string? hint = null, double width = 420)
    {
        var sp = new StackPanel { Width = width };
        sp.Children.Add(new TextBlock { Text = title, Style = S("H2") });
        if (hint != null) { var h = UiKit.Text(hint, 12, true); h.Margin = new Thickness(0, 0, 0, 8); sp.Children.Add(h); }
        return (UiKit.Card(sp), sp);
    }

    static Button Btn(Panel p, string text, Action click, bool accent = false)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 8) };
        if (accent) b.Style = S("AccentButton");
        b.Click += (_, _) => { try { click(); } catch (Exception e) { Osd.Show("", e.Message); } };
        p.Children.Add(b);
        return b;
    }

    void Render()
    {
        Cards.Children.Clear();
        Cards.Children.Add(StatusCard());
        if (Phone.Connected) Cards.Children.Add(ActionsCard());
        Cards.Children.Add(NotificationsCard());
        if (!Phone.Connected) Cards.Children.Add(InstallCard());
    }

    UIElement StatusCard()
    {
        var s = Settings.Current;
        var (c, b) = Card(Phone.Connected ? "Подключён" : "Не подключён");
        if (Phone.Connected && Phone.Last is { } st)
        {
            b.Children.Add(UiKit.Text(st.Name, 16, weight: FontWeights.SemiBold));
            b.Children.Add(UiKit.Text($"Заряд: {(st.Battery >= 0 ? st.Battery + "%" : "?")}{(st.Charging ? " ⚡ заряжается" : "")}", 14));
            if (st.StorageTotal > 0)
                b.Children.Add(UiKit.Text($"Память: свободно {Cleaner.Size(st.StorageFree)} из {Cleaner.Size(st.StorageTotal)}", 14));
            b.Children.Add(UiKit.Text($"Обновлено в {st.At:HH:mm}", 11, true));
        }
        else if (Phone.Connected) b.Children.Add(UiKit.Text("Телефон на связи, жду данные о заряде…", 13, true));
        else if (!Remote.Running)
        {
            b.Children.Add(UiKit.Text("Телефон подключается через пульт по Wi-Fi — сейчас он выключен.", 13, true));
            var wp = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
            Btn(wp, "Включить", () => { s.RemoteEnabled = true; Settings.Save(); Remote.Apply(); Render(); }, accent: true);
            b.Children.Add(wp);
        }
        else
        {
            Remote.EnsurePin();
            b.Children.Add(UiKit.Text("В приложении Mishka на телефоне: вкладка «ПК» → выбери этот компьютер и введи PIN:", 13, true));
            b.Children.Add(new TextBlock { Text = s.RemotePin, FontSize = 30, FontWeight = FontWeights.Bold, Foreground = (Brush)FindResource("AccentBrush"), Margin = new Thickness(0, 6, 0, 0) });
            b.Children.Add(UiKit.Text($"Компьютер: {Environment.MachineName} · {Remote.LocalIp()}", 12, true));
        }
        return c;
    }

    UIElement ActionsCard()
    {
        var (c, b) = Card("Действия");
        var wp = new WrapPanel();
        Btn(wp, "Найти телефон (звонок)", () => { _ = Phone.Ring(); Osd.Show("", "Телефон звонит"); }, accent: true);
        Btn(wp, "Тишина", () => _ = Phone.Ring(false));
        Btn(wp, "Отправить файлы…", () =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Title = "Файлы на телефон" };
            if (d.ShowDialog() != true) return;
            foreach (var f in d.FileNames) _ = Phone.SendFile(f);
            Osd.Show("", $"На телефон: {d.FileNames.Length} шт. — в «Загрузки/Mishka»");
        });
        Btn(wp, "Буфер → телефон", () =>
        {
            var text = Clipboard.ContainsText() ? Clipboard.GetText() : "";
            if (text.Length == 0) { Osd.Show("", "В буфере нет текста"); return; }
            _ = Phone.SendClipboard(text);
            Osd.Show("", "Текст на телефоне");
        });
        b.Children.Add(wp);
        b.Children.Add(UiKit.Text("С телефона сюда: в приложении «Файлы → ПК» / «Буфер → ПК» или «Поделиться → На ПК (Mishka)». Файлы падают в «Загрузки\\С телефона».", 11, true));
        return c;
    }

    UIElement NotificationsCard()
    {
        var (c, b) = Card("Уведомления телефона", "Приходят сюда, если в приложении включён «Доступ к уведомлениям».");
        var sw = new CheckBox { Style = S("Switch"), Content = "Показывать всплывающими на ПК", IsChecked = Settings.Current.PhoneNotifications };
        sw.Click += (_, _) => { Settings.Current.PhoneNotifications = sw.IsChecked == true; Settings.Save(); };
        b.Children.Add(sw);
        List<Phone.Notification> list;
        lock (Phone.Notifications) list = Phone.Notifications.Take(12).ToList();
        if (list.Count == 0) b.Children.Add(UiKit.Text("Пока пусто.", 12, true));
        foreach (var n in list)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 6, 0, 0) };
            sp.Children.Add(UiKit.Text($"{n.App} · {n.At:HH:mm}", 11, true));
            sp.Children.Add(UiKit.Text(n.Title, 13, weight: FontWeights.SemiBold));
            if (n.Text.Length > 0) sp.Children.Add(UiKit.Text(n.Text.Length > 200 ? n.Text[..200] + "…" : n.Text, 12));
            b.Children.Add(sp);
        }
        return c;
    }

    UIElement InstallCard()
    {
        var (c, b) = Card("Поставить приложение", "Android 8 и новее. Наведи камеру телефона на код — скачается установщик Mishka.");
        var png = new QRCoder.PngByteQRCode(new QRCoder.QRCodeGenerator().CreateQrCode(ApkUrl, QRCoder.QRCodeGenerator.ECCLevel.M)).GetGraphic(8);
        var img = new System.Windows.Media.Imaging.BitmapImage();
        img.BeginInit(); img.StreamSource = new MemoryStream(png); img.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad; img.EndInit(); img.Freeze();
        b.Children.Add(new Border { Background = Brushes.White, CornerRadius = new CornerRadius(10), Padding = new Thickness(8), HorizontalAlignment = HorizontalAlignment.Left,
            Child = new Image { Source = img, Width = 170, Height = 170 } });
        b.Children.Add(UiKit.Text("1) Скачай и открой Mishka-Phone.apk → «Установить» (Android спросит разрешение ставить из браузера — разреши).\n" +
                                  "2) В приложении: вкладка «ПК» → выбери этот компьютер → введи PIN.\n" +
                                  "3) Разреши «Доступ к уведомлениям» и «Работу в фоне» — там же есть кнопки.", 12, true));
        var wp = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        Btn(wp, "Скопировать ссылку", () => { Clipboard.SetText(ApkUrl); Osd.Show("", "Ссылка скопирована"); });
        b.Children.Add(wp);
        return c;
    }
}
