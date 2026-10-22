using NAudio.CoreAudioApi;

namespace MishkaHelper.Core;

/// <summary>Every action reachable from hotkeys, tray, radial menu and widgets.</summary>
public static class Actions
{
    public static async void Run(string action)
    {
        try
        {
            switch (action)
            {
                case "radial": RadialMenu.Toggle(); break;
                case "quick": QuickPanel.Toggle(); break;
                case "main": App.Current.ShowMain(); break;
                case "audioNext": Osd.Show("", Audio.NextOutput()); break;
                case "micMute":
                    Audio.MicMuted = !Audio.MicMuted;
                    Osd.Show(Audio.MicMuted ? "" : "", Audio.MicMuted ? "Микрофон выключен" : "Микрофон включён");
                    break;
                case "volUp": Audio.Volume += 5; Osd.Show("", "Громкость", Audio.Volume); break;
                case "volDown": Audio.Volume -= 5; Osd.Show("", "Громкость", Audio.Volume); break;
                case "mute":
                    Audio.Muted = !Audio.Muted;
                    Osd.Show(Audio.Muted ? "" : "", Audio.Muted ? "Звук выключен" : "Звук включён");
                    break;
                case "brightUp": Display.Brightness = Math.Min(100, Display.Brightness + 10); Osd.Show("", "Яркость", Display.Brightness); break;
                case "brightDown": Display.Brightness = Math.Max(0, Display.Brightness - 10); Osd.Show("", "Яркость", Display.Brightness); break;
                case "profileNext": Profiles.Next(); break;
                case "powerNext": Osd.Show("", "Питание: " + Power.NextMode()); break;
                case "dnd":
                    Tweaks.DoNotDisturb = !Tweaks.DoNotDisturb;
                    Osd.Show("", Tweaks.DoNotDisturb ? "Не беспокоить: вкл" : "Не беспокоить: выкл");
                    break;
                case "night":
                    Display.NightLight = !(Display.NightLight ?? false);
                    Osd.Show("", Display.NightLight == true ? "Ночной свет: вкл" : "Ночной свет: выкл");
                    break;
                case "widgets": Widgets.ToggleAll(); break;
                case "boost":
                    Osd.Show("", "Ускоряю…");
                    var freed = await Cleaner.BoostAll();
                    Osd.Show("", $"Готово! Освобождено {Cleaner.Size(freed)}");
                    break;
                case "fanMax":
                    if (!Acer.Available) { Osd.Show("", "Нет управления вентиляторами"); break; }
                    var max = Acer.Fans != Acer.FanMode.Max;
                    Acer.Fans = max ? Acer.FanMode.Max : Acer.FanMode.Auto;
                    Osd.Show("", max ? "Вентиляторы: максимум" : "Вентиляторы: авто");
                    break;
                case "wifi":
                    var w = await Radios.Get(Windows.Devices.Radios.RadioKind.WiFi) ?? false;
                    await Radios.Set(Windows.Devices.Radios.RadioKind.WiFi, !w);
                    Osd.Show("", !w ? "Wi-Fi включён" : "Wi-Fi выключен");
                    break;
                case "bt":
                    var b = await Radios.Get(Windows.Devices.Radios.RadioKind.Bluetooth) ?? false;
                    await Radios.Set(Windows.Devices.Radios.RadioKind.Bluetooth, !b);
                    Osd.Show("", !b ? "Bluetooth включён" : "Bluetooth выключен");
                    break;
                case "screenshot": Shell.Open("ms-screenclip:"); break;
                case "taskmgr": Shell.Open("taskmgr.exe"); break;
                case "lock": Toolbox.Lock(); break;
                case "palette": CommandPalette.Toggle(); break;
                case "clipboard": ClipHistory.Toggle(); break;
                case "features": Features.Open(); break;
#if !LITE
                case "jarvis": Jarvis.Listen(); break;
                case "jarvisChat": JarvisChat.Open(); break;
#endif
                case "topmost": Osd.Show("", Toolbox.ToggleTopmost()); break;
                case "opacity": Osd.Show("", "Прозрачность окна", Toolbox.CycleOpacity()); break;
                case "killFg": Osd.Show("", Toolbox.KillForeground()); break;
                case "colorPick":
                    var (hex, _) = Toolbox.PickColor();
                    Osd.Show("", $"{hex} — скопировано");
                    break;
                case "screenOff": await Task.Delay(400); Toolbox.ScreenOff(); break;   // let the key-up pass first, or the screen wakes again
                case "keepAwake":
                    Toolbox.KeepAwake = !Toolbox.KeepAwake;
                    Osd.Show("", Toolbox.KeepAwake ? "Не засыпать: вкл" : "Не засыпать: выкл");
                    break;
                case "recycle": Toolbox.EmptyRecycleBin(); Osd.Show("", "Корзина очищена"); break;
                case "explorer": Toolbox.RestartExplorer(); Osd.Show("", "Проводник перезапущен"); break;
                case "desktopIcons": Toolbox.ToggleDesktopIcons(); break;
                case "taskbar": Toolbox.TaskbarAutoHide = !Toolbox.TaskbarAutoHide; Osd.Show("", Toolbox.TaskbarAutoHide ? "Панель задач скрывается" : "Панель задач видна"); break;
                case "dark": Tweaks.DarkTheme = !Tweaks.DarkTheme; Osd.Show("", Tweaks.DarkTheme ? "Тёмная тема" : "Светлая тема"); break;
                case "sysinfo": System.Windows.Clipboard.SetText(Toolbox.SystemReport()); Osd.Show("", "Сведения о ПК скопированы"); break;
                case "ping":
                    var ms = await Toolbox.Ping(Settings.Current.PingHost);
                    Osd.Show("", ms < 0 ? "Нет ответа" : $"Пинг {ms} мс");
                    break;
                case "password":
                    System.Windows.Clipboard.SetText(Toolbox.Password(16, true));
                    Osd.Show("", "Пароль скопирован в буфер");
                    break;
                default:
                    if (action.StartsWith("profile:")) Profiles.Apply(action[8..]);
                    else if (action.StartsWith("launch:") && int.TryParse(action[7..], out var i) && i < Settings.Current.Launchers.Count)
                    {
                        var l = Settings.Current.Launchers[i];
                        Shell.Open(l.Path, l.Args);
                    }
                    else if (action.StartsWith("audio:")) { Audio.SetDefault(action[6..]); Osd.Show("", Audio.DefaultName(DataFlow.Render)); }
                    else if (action.StartsWith("page:")) App.Current.Navigate(action[5..]);
#if !LITE
                    else if (action.StartsWith("scenario:") && Settings.Current.Scenarios.FirstOrDefault(s => s.Id == action[9..]) is { } sc) _ = Scenarios.Run(sc);
#endif
                    break;
            }
        }
        catch (Exception e)
        {
            Log.Error(e);
            Osd.Show("", "Ошибка: " + e.Message);
        }
    }
}
