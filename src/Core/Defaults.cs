namespace MishkaHelper.Core;

/// <summary>First-run values: hotkeys, profiles, widgets, launchers.</summary>
public static class Defaults
{
    public static readonly Dictionary<string, string> HotkeyNames = new()
    {
        ["radial"] = "Радиальное меню",
        ["quick"] = "Быстрая панель",
        ["main"] = "Открыть Mishka Helper",
        ["audioNext"] = "Следующее аудиоустройство",
        ["micMute"] = "Микрофон вкл/выкл",
        ["volUp"] = "Громкость +5",
        ["volDown"] = "Громкость −5",
        ["mute"] = "Звук вкл/выкл",
        ["brightUp"] = "Яркость +10",
        ["brightDown"] = "Яркость −10",
        ["profileNext"] = "Следующий профиль",
        ["powerNext"] = "Следующий режим питания",
        ["boost"] = "Ускорить всё",
        ["dnd"] = "Не беспокоить",
        ["widgets"] = "Показать/скрыть виджеты",
        ["fanMax"] = "Вентиляторы на максимум",
        ["palette"] = "Палитра команд (поиск по всем функциям)",
        ["topmost"] = "Активное окно поверх всех",
        ["opacity"] = "Прозрачность активного окна",
        ["killFg"] = "Убить зависшее активное окно",
        ["colorPick"] = "Пипетка: цвет под курсором",
        ["screenOff"] = "Выключить экран",
        ["keepAwake"] = "Не засыпать (вкл/выкл)",
        ["lock"] = "Заблокировать ПК",
        ["night"] = "Ночной свет",
        ["wifi"] = "Wi-Fi вкл/выкл",
        ["bt"] = "Bluetooth вкл/выкл",
        ["recycle"] = "Очистить корзину",
        ["explorer"] = "Перезапустить Проводник",
    };

    public static void Fill(AppSettings s)
    {
        var hk = new Dictionary<string, string>
        {
            ["radial"] = "Ctrl+Alt+R",   // Ctrl+Alt+Space belongs to Claude's quick entry
            ["quick"] = "Ctrl+Alt+Q",
            ["main"] = "Ctrl+Alt+H",
            ["audioNext"] = "Ctrl+Alt+A",
            ["micMute"] = "Ctrl+Alt+M",
            ["volUp"] = "Ctrl+Alt+PageUp",
            ["volDown"] = "Ctrl+Alt+PageDown",
            ["mute"] = "Ctrl+Alt+End",
            ["brightUp"] = "Ctrl+Alt+OemPlus",
            ["brightDown"] = "Ctrl+Alt+OemMinus",
            ["profileNext"] = "Ctrl+Alt+P",
            ["powerNext"] = "Ctrl+Alt+O",
            ["boost"] = "Ctrl+Alt+B",
            ["dnd"] = "Ctrl+Alt+D",
            ["widgets"] = "Ctrl+Alt+W",
            ["fanMax"] = "Ctrl+Alt+F",
            ["palette"] = "Ctrl+Alt+K",
            ["topmost"] = "Ctrl+Alt+T",
            ["opacity"] = "Ctrl+Alt+Y",
            ["killFg"] = "Ctrl+Alt+F4",
            ["colorPick"] = "Ctrl+Alt+C",
            ["screenOff"] = "", ["keepAwake"] = "", ["lock"] = "", ["night"] = "Ctrl+Alt+N",
            ["wifi"] = "", ["bt"] = "", ["recycle"] = "", ["explorer"] = "",
        };
        if (s.Hotkeys.GetValueOrDefault("radial") == "Ctrl+Alt+Space") s.Hotkeys["radial"] = hk["radial"];   // old default
        foreach (var (k, v) in hk) s.Hotkeys.TryAdd(k, v);

        if (s.Profiles.Count == 0)
            s.Profiles.AddRange(new[]
            {
                new Profile { Name = "Игра", Icon = "", PowerMode = "Performance", AcerMode = 4, DoNotDisturb = true, NightLight = false, CleanOnApply = true },
                new Profile { Name = "Учёба", Icon = "", PowerMode = "Balanced", AcerMode = 1, DoNotDisturb = false, Volume = 40 },
                new Profile { Name = "Батарея", Icon = "", PowerMode = "Saver", AcerMode = 0, Brightness = 40, RefreshRate = 60 },
                new Profile { Name = "Ночь", Icon = "", PowerMode = "Balanced", AcerMode = 0, Brightness = 25, NightLight = true, Volume = 20, DoNotDisturb = true },
            });

        foreach (var kind in new[] { "system", "battery", "clock", "network", "profiles", "audio",
                     "notes", "todo", "timer", "media", "ping", "mic", "disks", "calendar", "currency", "procs", "fans", "worldclock", "countdown", "launcher" })
            if (!s.Widgets.Any(w => w.Kind == kind))
                s.Widgets.Add(new WidgetConfig { Kind = kind, Left = 60, Top = 60 + s.Widgets.Count * 150 });

        if (s.Launchers.Count == 0)
            s.Launchers.AddRange(new[]
            {
                new Launcher { Name = "Проводник", Path = "explorer.exe" },
                new Launcher { Name = "Диспетчер задач", Path = "taskmgr.exe" },
                new Launcher { Name = "Загрузки", Path = "explorer.exe", Args = "shell:Downloads" },
                new Launcher { Name = "Параметры", Path = "ms-settings:" },
            });

        if (s.CleanTargets.Count == 0)
            s.CleanTargets.AddRange(new[] { "usertemp", "wintemp", "recycle", "thumbs", "dumps", "browsers", "wu" });
    }
}
