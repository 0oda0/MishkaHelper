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
        ["jarvis"] = "Джарвис: слушать команду",
        ["jarvisChat"] = "Джарвис: чат",
        ["palette"] = "Палитра команд (поиск по всем функциям)",
        ["clipboard"] = "История буфера обмена",
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

    /// <summary>Jarvis actions don't exist in the Lite edition.</summary>
    public static bool Available(string action) => Edition.HasJarvis || !action.StartsWith("jarvis");

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
            ["jarvis"] = "Ctrl+Alt+J",
            ["jarvisChat"] = "Ctrl+Alt+I",
            ["clipboard"] = "Ctrl+Alt+V",
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

        // v2: Vosk-small/Windows voice/8B proved weak in practice → Whisper turbo, Piper voice, 4B-2507.
        if (s.Jarvis.Version < 2)
        {
            var j = s.Jarvis;
            j.Engine = "whisper"; j.WhisperModel = "turbo"; j.AiModel = "qwen3-4b-2507";
            j.Tts = "piper"; j.PiperVoice = "dmitri"; j.VoiceFx = true; j.Rate = 1.0;
            j.Version = 2;
        }
        // v3: Piper voice was rejected → Microsoft neural voices (online, Piper stays as offline fallback).
        if (s.Jarvis.Version < 3)
        {
            s.Jarvis.Tts = "edge"; s.Jarvis.EdgeVoice = "ru-RU-DmitryNeural"; s.Jarvis.VoiceFx = false;
            s.Jarvis.Version = 3;
        }

        if (s.Scenarios.Count == 0)
            s.Scenarios.AddRange(new[]
            {
                new Scenario
                {
                    Name = "Режим кино",
                    Triggers = { new() { Kind = "voice", Value = "режим кино" } },
                    Steps = { new() { Kind = "action", Value = "dnd" }, new() { Kind = "brightness", Value = "40" }, new() { Kind = "volume", Value = "60" }, new() { Kind = "say", Value = "Приятного просмотра" } },
                },
                new Scenario
                {
                    Name = "Ухожу",
                    Triggers = { new() { Kind = "voice", Value = "я ухожу" } },
                    Steps = { new() { Kind = "say", Value = "Пока! Блокирую компьютер" }, new() { Kind = "wait", Value = "2" }, new() { Kind = "action", Value = "lock" } },
                },
                new Scenario
                {
                    Name = "Наушники",
                    Triggers = { new() { Kind = "event", Value = "audio_new" } },
                    Steps = { new() { Kind = "notify", Value = "Подключено новое аудиоустройство" } },
                    Enabled = false,
                },
            });

        if (s.CleanTargets.Count == 0)
            s.CleanTargets.AddRange(new[] { "usertemp", "wintemp", "recycle", "thumbs", "dumps", "browsers", "wu" });
    }
}
