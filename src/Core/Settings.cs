using System.IO;
using System.Text.Json;

namespace MishkaHelper.Core;

public class Profile
{
    public string Name { get; set; } = "";
    public string Icon { get; set; } = "";
    public int? Volume { get; set; }
    public int? Brightness { get; set; }
    public string? PowerMode { get; set; }      // Windows overlay: Saver/Balanced/Performance
    public int? AcerMode { get; set; }          // NitroSense thermal profile id
    public int? RefreshRate { get; set; }
    public bool? DoNotDisturb { get; set; }
    public bool? NightLight { get; set; }
    public string? AudioDevice { get; set; }    // endpoint id
    public bool CleanOnApply { get; set; }
}

public class AppRule
{
    public string Process { get; set; } = "";   // e.g. "cs2"
    public string Profile { get; set; } = "";
    public string Priority { get; set; } = "";  // "", "AboveNormal", "High"
}

public class TodoItem
{
    public string Text { get; set; } = "";
    public bool Done { get; set; }
}

public class JarvisSettings
{
    public bool Enabled { get; set; }
    public bool WakeWordOn { get; set; } = true;
    public string WakeWord { get; set; } = "джарвис";
    public string Engine { get; set; } = "whisper";       // whisper | vosk
    public string WhisperModel { get; set; } = "turbo";   // turbo | base | small
    public string Microphone { get; set; } = "";          // device name, "" = default
    public bool Speak { get; set; } = true;
    public string Tts { get; set; } = "edge";             // edge (online neural) | piper (offline) | windows
    public string EdgeVoice { get; set; } = "ru-RU-DmitryNeural";
    public int EdgePitch { get; set; }                     // percent, e.g. -5
    public bool Movie { get; set; }                        // "как в фильме": British English voice, answers in English
    public bool FollowUp { get; set; } = true;             // after an answer, listen ~6 s without the wake word
    public bool BargeIn { get; set; } = true;              // "стоп" interrupts speech (ears stay open while talking)
    public bool Stream { get; set; } = true;               // speak AI answers sentence by sentence while generating
    public string MovieVoice { get; set; } = "en-GB-RyanNeural";
    public string PiperVoice { get; set; } = "dmitri";
    public bool VoiceFx { get; set; } = true;              // "AI butler" effect
    public string Voice { get; set; } = "";               // Windows voice name
    public double Rate { get; set; } = 1.0;
    public bool ShowOrb { get; set; } = true;
    public double OrbLeft { get; set; } = -1;
    public double OrbTop { get; set; } = -1;
    public bool Proactive { get; set; } = true;
    public bool AiEnabled { get; set; }
    public string AiModel { get; set; } = "qwen3-4b-2507";
    public bool AiGpu { get; set; } = true;
    public string OnlineProvider { get; set; } = "";   // "" = only the local model; groq / cerebras / openrouter / custom (Online.Providers)
    public string OnlineKey { get; set; } = "";        // encrypted (Secrets)
    public string OnlineModel { get; set; } = "";      // "" = the provider's default
    public string OnlineUrl { get; set; } = "";        // for "custom"
    public string UserName { get; set; } = "";
    public List<string> Memory { get; set; } = new();
    public List<WebSource> Sources { get; set; } = new();
    public List<string> Calendars { get; set; } = new();   // ICS / webcal links
    /// <summary>User phrases handled instantly, without the AI.</summary>
    public List<VoiceCommand> Commands { get; set; } = new();
    public bool StartWithApp { get; set; } = true;   // false: Jarvis stays off at launch even if it was on last time
    public bool WakeSound { get; set; } = true;   // short chime when the wake word is heard
    public bool Duck { get; set; } = true;       // quieter other apps while Jarvis listens/answers
    public int DuckPercent { get; set; } = 20;
    public int Version { get; set; }   // bumps migrate old defaults
}

/// <summary>A page Jarvis may read to answer questions (e.g. the schedule in a student account).</summary>
public class WebSource
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string Keywords { get; set; } = "";   // comma-separated: "пары, расписание, занятия"
}

/// <summary>"When [trigger] → do [steps]". Steps run top to bottom; an "if" step that fails stops the run.</summary>
public class Scenario
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Name { get; set; } = "Новый сценарий";
    public bool Enabled { get; set; } = true;
    public List<ScenarioTrigger> Triggers { get; set; } = new();
    public List<ScenarioStep> Steps { get; set; } = new();
}

public class ScenarioTrigger
{
    public string Kind { get; set; } = "voice";   // voice | hotkey | time | event
    public string Value { get; set; } = "";        // phrase | combo | "HH:mm" | event name
    public string Arg { get; set; } = "";          // days "1234567" | process name | battery % ...
}

public class ScenarioStep
{
    public string Kind { get; set; } = "action";
    public string Value { get; set; } = "";
    public string Arg { get; set; } = "";
}

public class MediaLink
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public bool Channel { get; set; }   // added from the phone; services are the built-in ones
}

/// <summary>The secretary: off until the user turns it on; every feature has its own switch.</summary>
public class SecretarySettings
{
    public bool Enabled { get; set; }
    public bool AppNotes { get; set; } = true;
    public bool AppHints { get; set; } = true;
    public bool Capture { get; set; } = true;
    public bool Voice { get; set; } = true;
    public bool Remember { get; set; } = true;
    public bool Mail { get; set; }
    public int MailMinutes { get; set; } = 10;
    public List<string> MailImportant { get; set; } = new();   // empty = tell about every letter
    public bool Deadlines { get; set; } = true;
    public bool Morning { get; set; } = true;
    public bool Evening { get; set; } = true;
    public int EveningHour { get; set; } = 20;
    public bool Workspace { get; set; } = true;
    public bool Downloads { get; set; } = true;
    public bool Meetings { get; set; } = true;
    public bool Breaks { get; set; }
    public int BreakMinutes { get; set; } = 90;
    public bool ToPhone { get; set; }
    public bool QuietInGame { get; set; } = true;
    public bool Tasks { get; set; } = true;
    public bool Repeats { get; set; } = true;
    public bool AppTriggers { get; set; } = true;
    public bool ScheduleRead { get; set; } = true;
    public bool DayLog { get; set; } = true;
    public bool Selection { get; set; } = true;
    public bool DownloadRules { get; set; } = true;
    public bool FileMemory { get; set; } = true;
    public bool Dates { get; set; } = true;
    public bool ShutdownCheck { get; set; } = true;
    public bool PhoneTab { get; set; } = true;
    public bool Bubble { get; set; } = true;
    public double BubbleX { get; set; } = -1;   // right edge of the button; -1 = default place
    public double BubbleY { get; set; } = -1;
    public bool ContextHints { get; set; } = true;
    public bool Prep { get; set; } = true;
    public int PrepMinutes { get; set; } = 15;
    public bool Milestones { get; set; } = true;
    public bool Habits { get; set; } = true;
    public bool LongSession { get; set; } = true;
    public bool CheckIns { get; set; } = true;          // asks about the day's plans a few times a day
    public List<int> CheckInHours { get; set; } = new() { 12, 17 };
    public string Name { get; set; } = "Секретарша";
    public string Tone { get; set; } = "work";   // work, friend, short
    public bool SpeakMorning { get; set; }       // the morning summary aloud (needs Jarvis' voice); off: no sound unless asked for
}

public class Launcher
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Args { get; set; } = "";
}

/// <summary>Root item of the wheel: a built-in key ("profiles", "music"…) or "act:&lt;action&gt;" with its own label/icon.</summary>
/// <summary>"Phrase(s) → action": phrases split by ";", action = any palette command key.</summary>
public class VoiceCommand
{
    public string Phrases { get; set; } = "";
    public string Action { get; set; } = "";
    public string ActionName { get; set; } = "";
    public string Reply { get; set; } = "";
}

/// <summary>A reminder that survives restarts. Date-only ones ring at 10:00 that day and the evening before.</summary>
public class Reminder
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];
    public string Text { get; set; } = "";
    public DateTime At { get; set; }
    public bool HasTime { get; set; }
    public bool Done { get; set; }
    public bool EveWarned { get; set; }
    public string Repeat { get; set; } = "";   // "", daily, weekly, monthly, yearly
    public int RepeatDay { get; set; }         // monthly: the 31st stays the 31st after a short month
    public int LeadDays { get; set; }          // warn this many days ahead (birthdays)
}

public class RadialEntry
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public string Icon { get; set; } = "";
}

public class WidgetConfig
{
    public string Kind { get; set; } = "";
    public double Left { get; set; } = 100;
    public double Top { get; set; } = 100;
    public double Opacity { get; set; } = 0.92;
    public bool Topmost { get; set; }
    public bool Visible { get; set; }
    /// <summary>Dragged by the user → stays where put; otherwise stacked in the top-right column.</summary>
    public bool Manual { get; set; }
}

public class AlertSettings
{
    public bool Enabled { get; set; } = true;
    public int CpuTemp { get; set; } = 92;
    public int GpuTemp { get; set; } = 87;
    public int DiskFreeGb { get; set; } = 10;
    public int HeadsetBattery { get; set; } = 15;
    public int RamPercent { get; set; } = 92;
}

public class AppSettings
{
    public string Accent { get; set; } = "Purple";
    public bool Glass { get; set; }
    public string CustomAccent { get; set; } = "";   // #RRGGBB over the preset; "" = the preset
    public string BgColor { get; set; } = "";        // "" = the default dark
    public string CardColor { get; set; } = "";      // "" = derived from the background
    public string BgImage { get; set; } = "";        // a copy in the data folder (ThemeManager.KeepImage)
    public int BgImageDim { get; set; } = 70;        // % of the background colour over the picture
    public double UiScale { get; set; } = 1.0;
    public bool StartWithWindows { get; set; } = true;
    public bool StartMinimized { get; set; } = true;
    public bool CloseToTray { get; set; } = true;
    public bool Osd { get; set; } = true;
    public bool AutoSwitchAudio { get; set; } = true;
    public int HistoryDays { get; set; } = 7;

    public Dictionary<string, string> Hotkeys { get; set; } = new();
    public List<Profile> Profiles { get; set; } = new();
    public bool AutoProfiles { get; set; }
    public string? OnBatteryProfile { get; set; }
    public string? OnAcProfile { get; set; }
    public List<AppRule> AppRules { get; set; } = new();
    public List<Launcher> Launchers { get; set; } = new();
    public List<WidgetConfig> Widgets { get; set; } = new();
    public AlertSettings Alerts { get; set; } = new();

    public string VirusTotalKey { get; set; } = "";
    public int ScanIntervalDays { get; set; }
    public DateTime LastScan { get; set; }
    public int CleanIntervalDays { get; set; }
    public DateTime LastClean { get; set; }
    public List<string> CleanTargets { get; set; } = new();

    public string WeatherCity { get; set; } = "Москва";

    /// <summary>Last keyboard zone colours we set (#RRGGBB ×4). The firmware's read-back doesn't reflect changes.</summary>
    public List<string> KeyboardZones { get; set; } = new();

    // Widgets data
    public string Notes { get; set; } = "";
    public List<TodoItem> Todos { get; set; } = new();
    public List<string> WorldClocks { get; set; } = new() { "Europe/Moscow", "Asia/Tokyo", "America/New_York" };
    public string PingHost { get; set; } = "1.1.1.1";
    public DateTime CountdownTo { get; set; } = new(DateTime.Now.Year + 1, 1, 1);
    public string CountdownName { get; set; } = "Новый год";

    // Background helpers
    public int VolumeCap { get; set; }               // 0 = off
    public bool AutoTheme { get; set; }
    public string LightAt { get; set; } = "07:00";
    public string DarkAt { get; set; } = "21:00";
    public bool LockKeysOsd { get; set; } = true;
    public bool CheckUpdates { get; set; } = true;
    public SecretarySettings Secretary { get; set; } = new();
    public bool SetupDone { get; set; }                  // first-run setup was finished or skipped
    public List<string> HiddenSections { get; set; } = new();   // sidebar sections the user doesn't need
    public string SupportSession { get; set; } = "";     // support chat login, encrypted (Secrets)
    public bool Onboarded { get; set; }   // «С чего начать» on Home was dismissed
    public List<Reminder> Reminders { get; set; } = new();
    public bool GoogleCalendar { get; set; } = true;   // open a pre-filled Google Calendar event for dated reminders
    public bool RemoteEnabled { get; set; }
    public bool BleRemoteEnabled { get; set; }
    public bool PhoneNotifications { get; set; } = true;   // show the phone app's notifications as toasts
    /// <summary>Game mode turns itself on when a full-screen game is in front.</summary>
    public bool FocusAuto { get; set; } = true;
    /// <summary>FPS and temperatures in the corner while game mode is on.</summary>
    public bool GameOverlay { get; set; } = true;
    /// <summary>Game mode never mutes these (process names): voice chat must keep working.</summary>
    public List<string> FocusKeepSound { get; set; } = new() { "discord", "teamspeak", "ts3client_win64", "mumble", "telegram", "zoom" };
    /// <summary>Couch remote: streaming services and favourite channels, opened in the default browser.</summary>
    public List<MediaLink> MediaLinks { get; set; } = new()
    {
        new() { Name = "YouTube", Url = "https://www.youtube.com/" },
        new() { Name = "Подписки YouTube", Url = "https://www.youtube.com/feed/subscriptions" },
        new() { Name = "Кинопоиск", Url = "https://hd.kinopoisk.ru/" },
        new() { Name = "VK Видео", Url = "https://vkvideo.ru/" },
        new() { Name = "Rutube", Url = "https://rutube.ru/" },
        new() { Name = "Twitch", Url = "https://www.twitch.tv/" },
    };
    /// <summary>Sites that open only through the VPN: before them the VPN app is started and awaited.</summary>
    public List<string> VpnSites { get; set; } = new() { "youtube.com", "youtu.be" };
    /// <summary>VPN program; empty = find Happ / v2rayN / Hiddify / AmneziaVPN in Program Files.</summary>
    public string VpnApp { get; set; } = "";
    public bool AutoLayout { get; set; }   // fix wrong-layout words while typing; a keyboard hook, so off unless asked for
    public int RemotePort { get; set; } = 8770;
    public string RemotePin { get; set; } = "";
    public bool ScreenTime { get; set; } = true;
    /// <summary>Process name → minutes per day; a toast warns 5 min before and at the limit.</summary>
    public Dictionary<string, int> AppLimits { get; set; } = new();
    public bool ClipboardHistory { get; set; } = true;
    /// <summary>Wheel root, in order. Empty = the default wheel.</summary>
    public List<RadialEntry> Radial { get; set; } = new();
    /// <summary>Wheels for work programs (Word, Excel, Photoshop…): shown instead of the usual one while that program is in front.</summary>
    public List<AppWheel> AppWheels { get; set; } = Core.AppWheels.Presets();
    public DateTime LastUpdateCheck { get; set; }

    public JarvisSettings Jarvis { get; set; } = new();
    public List<Scenario> Scenarios { get; set; } = new();
    public MouseSettings Mouse { get; set; } = new();
    // Game helper
    public bool GameOffers { get; set; } = true;                 // offer to build a base when a new game comes to the front
    public List<string> GamesDeclined { get; set; } = new();     // "not for this game"
    public bool GameKbOnTop { get; set; } = true;
    public double GameKbWidth { get; set; } = 1000;
    public double GameKbHeight { get; set; } = 680;
    public double? GameKbLeft { get; set; }
    public double? GameKbTop { get; set; }
    public List<string> AppKit { get; set; } = new();   // winget ids picked on «Программы для нового ПК»
}

public class MouseSettings
{
    public bool HoldWheelRadial { get; set; }          // hold the wheel → the wheel menu; a short click stays a click
    public bool TaskbarVolume { get; set; } = true;    // scroll over the taskbar → volume
    public bool Gestures { get; set; }                 // hold a side button and move → an action; a short click stays a click
    public string GestureButton { get; set; } = "Mouse5";
    public Dictionary<string, string> GestureActions { get; set; } = new() { ["up"] = "quick", ["down"] = "widgets", ["left"] = "palette", ["right"] = "clipboard" };
}

public static class Settings
{
    public static readonly string Dir = AppPaths.Data;
    static readonly string FilePath = System.IO.Path.Combine(Dir, "settings.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings Current { get; private set; } = new();

    public static void Load()
    {
        try
        {
            if (File.Exists(FilePath))
                Current = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new();
        }
        catch
        {
            // Corrupt file: keep a copy so the user doesn't silently lose it, start fresh.
            try { File.Copy(FilePath, FilePath + ".bad", true); } catch { }
            Current = new();
        }
        Defaults.Fill(Current);
    }

    public static void Save()
    {
        Directory.CreateDirectory(Dir);
        var tmp = FilePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Current, Json));
        File.Move(tmp, FilePath, true);
    }
}
