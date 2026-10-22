# Mishka Helper — project map

Neon-dark all-in-one Windows utility (WPF, .NET 9, C#) for the owner's **Acer Nitro AN515-58** (i9-12900H, RTX 3060 6 GB) and friends' Win10/11 PCs.
UI language: **Russian**. Repo: `0oda0/MishkaHelper` (public). Owner talks Russian, wants pushback on bad ideas.

## Build / run / test

| What | Command |
|---|---|
| Build (full edition) | `dotnet build src` |
| Build Lite (no Jarvis) | `dotnet build src -p:Flavor=Lite` |
| Release folder | `dotnet publish src/MishkaHelper.csproj -c Release -r win-x64 --self-contained [-p:Flavor=Lite] -o publish` |
| Dev copy without UAC | add `-p:ApplicationManifest=<repo>\tools\app.dev.manifest` (asInvoker) and output to `test-out\dev\bin` |
| Screenshots of every page | `MishkaHelper.exe --shots <dir>` (dev copy; runs beside the installed copy) |
| Unit tests | `dotnet test tests` (xunit; Intents/Ears/Hotkeys/Updater/Remote/Calendar). Also run by CI before release |
| Self-test | `MishkaHelper.exe --selftest <report.txt> [steps]`, steps: `acer modes,fans,keyboard,defender,sweep,cleanup,disk,autostart,brain,ears,voice,update` (comma list; empty = all). Admin-only steps need the real (elevated) exe |
| Install on this PC | elevated: `tools\install-local.ps1 -Source publish [-SelfTest report] [-Only steps]` |
| Release | bump `<Version>` in csproj → commit → `git tag -a vX.Y.Z` → push tag → **`gh workflow run release.yml --ref vX.Y.Z`** (tag-push trigger fires 20–60 min late; cancel the late duplicate run if it appears) |

The app is `requireAdministrator`; autostart = scheduled task `MishkaHelper` (HighestAvailable, no UAC at logon).
`test-out/`, `publish*/`, `dist/` are git-ignored scratch.

## Layout

```
src/
  App.xaml(.cs)        startup: single instance (mutex MishkaHelper.Single, events .Show/.Quit), --tray/--shots/--selftest/--quit, Log class
  MainWindow.xaml(.cs) sidebar nav (Nav<Page> RadioButtons) → Pages; "Что можно сделать" button
  Theme.xaml           all styles/brushes (neon); accent/glass swapped at runtime by ThemeManager
  Core/
    Settings.cs        AppSettings JSON model (%AppData%\MishkaHelper\settings.json) incl. JarvisSettings, Scenario, WebSource
    Defaults.cs        first-run values, hotkey names/defaults, migrations (Jarvis.Version)
    Actions.cs         single dispatcher for every action key (hotkeys, tray, radial, palette, scenarios)
    Edition.cs         Full vs Lite (LITE define), version
    Native.cs          P/Invoke (DWM, hotkeys, display modes, power overlay, recycle bin…)
    Shell.cs           run console tools (OEM 866 decoding), open files/URLs
    ThemeManager.cs    accent colours, glass (DWM acrylic)
    Tray.cs            NotifyIcon + dark menu
    Features.cs        opens docs/index.html (landing)
  Services/            system logic, no UI
    Acer.cs            Acer gaming WMI (root\wmi AcerGamingFunction): sensors, modes, fans, RGB — needs admin
    Audio.cs           NAudio CoreAudio: volume, mic, sessions, default device (IPolicyConfig), auto-switch, volume cap
    Display.cs         brightness (WMI), refresh/resolution, night light (CloudStore blob)
    Power.cs           power overlay modes, plans, timeouts, battery (cached 5 s)
    Radios.cs          Wi-Fi/BT/airplane (WinRT)
    Tweaks.cs          HKCU tweaks (theme, DND, mouse, file ext…)
    SysMon.cs          sampling loop (1 s live / 5 s tray), GPU perf counters, History (CSV per minute)
    SystemInfo.cs      static info, disk SMART, top processes
    Devices.cs         peripherals via device containers, BT pair/unpair, eject, Touchpad
    Cleaner.cs         cleanup targets, large files, duplicates, boost
    StartupApps.cs     autostart entries (StartupApproved), installed programs
    Security.cs        Defender (MpCmdRun/WMI), VirusTotal, suspicious sweep (WinVerifyTrust)
    Toolbox.cs         Tools page utilities (topmost, opacity, color pick, power timer, Wi-Fi passwords, ping, speedtest, crashes, privacy…)
    Hotkeys.cs         RegisterHotKey, Pretty(), scenario hotkeys
    Profiles.cs        apply profiles, auto by AC/app rules (+process priority)
    Alerts.cs          threshold toasts
    Remote.cs          phone remote: HttpListener on LAN (port 8770), PIN header, private IPs only, netsh firewall rule (Private)
    ScreenTime.cs      foreground app seconds per day (idle >3 min skipped), 30 days JSON
    Updater.cs         GitHub releases → silent Inno setup or portable zip swap; per edition
    Services.cs        start/stop everything, periodic jobs (5 s tick), auto theme, Caps/Num OSD; Autostart task
  Jarvis/              (excluded from Lite)
    Jarvis.cs          orchestrator: Handle(text) → scenarios → Intents (+compound "и") → Brain; confirmations; proactive Offer()
    Ears.cs            mic → energy VAD → Whisper (GPU) or Vosk; wake word (fuzzy); hallucination filter
    Voice.cs           TTS chain: Edge neural (EdgeTts) → Piper (offline) → Windows; ButlerFx; sentence queue (Say/Enqueue/Stop), Finished → dialog follow-up
    EdgeTts.cs         Microsoft Edge read-aloud voices over WebSocket (Sec-MS-GEC token); unofficial, online
    Intents.cs         offline Russian command parser, number words, app/site launch, Keys (SendInput)
    Brain.cs           LLM agent with native tool calling (pc_command, read_site, notes, diagnose)
    LlamaServer.cs     llama.cpp server child process (Vulkan, picks discrete GPU, --fit), ChildJob (kill on exit)
    Models.cs          on-demand downloads to %LocalAppData%\MishkaHelper\models (Vosk, Whisper, Qwen, resumable)
    Notes.cs           notes store + chat history (jsonl)
    WebReader.cs       WebView2: login window + invisible page text reader for "web sources"
    Diagnostics.cs     "почему тормозит" report, status, snapshot for the LLM
    Calendar.cs        ICS links → events per day (DAILY/WEEKLY RRULE, EXDATE, COUNT/UNTIL); intent + Brain tool
    Scenarios.cs       scenario engine: steps + triggers (voice/hotkey/time/events)
  Pages/               one UserControl per section (XAML shell + code-built cards)
    Home, Jarvis, Controls, System, Devices, Nitro, Boost, Security, Tools, Profiles, Hotkeys, Widgets, Settings
  UI/
    UiKit.cs           shared builders (tiles, cards, ask/confirm dialogs)
    Controls.cs        Sparkline, Ring
    Popups.cs          Floating window base, Osd, Toast
    QuickPanel.xaml    tray flyout
    RadialMenu.cs      pie menu (Ctrl+Alt+R); submenus page at 11 items; OpenAt(label) for screenshots
    AppIcon.cs         real program/folder icons (SHGetFileInfo) for the wheel
    CommandPalette.cs  Ctrl+Alt+K search
    ClipHistory.cs     Ctrl+Alt+V clipboard history (message-only window, AddClipboardFormatListener; only pinned items persist)
    Widgets.cs / WidgetsExtra.cs  20 desktop widgets (partial class)
    JarvisUi.cs        orb + chat window
    Shots.cs / SelfTest.cs  dev tooling
docs/features/         landing page (index.html + img/*.png) shipped as <app>\docs
installer/MishkaHelper.iss   Inno Setup, FLAVOR env = ""/Lite, one AppId for both; uninstall asks to delete AppData/models
tests/                 xunit project (InternalsVisibleTo MishkaHelper.Tests)
tools/                 install-local.ps1, app.dev.manifest, make-icon.ps1
.github/workflows/release.yml  builds both editions, setup + portable zip, GitHub release
ROADMAP.md             feature list: done / later / rejected (with reasons)
STATUS.md              honest audit: not done / not verified live / known issues / improvement ideas — keep it current
```

## Gotchas (learned the hard way)

- **Never edit source with PowerShell 5 `Get-Content`/`Set-Content`** — it reads UTF-8 as cp1251 and corrupts Cyrillic. Use the Edit tool, or `[IO.File]` with `UTF8Encoding($false)`. `R` is a PS alias (Invoke-History).
- No single-file publish: Whisper/Vosk native loaders can't find libs in the extraction dir. LLamaSharp failed to load natives at all → LLM runs via `llama-server.exe` instead.
- llama.cpp would split onto the Intel iGPU → always pass `-dev VulkanN` of the discrete GPU. 8B doesn't fit 6 GB VRAM; default model is Qwen3-4B-Instruct-2507.
- Elevated windows block simulated input from non-elevated tools (UIPI) — computer-use can't click inside the app; use `--shots`/`--selftest`.
- Ctrl+Alt+Space is taken by Claude's quick entry; Ctrl+Alt+arrows rotate the screen on Intel graphics.
- Acer keyboard RGB: setters work, getters return stale colours → we persist zone colours in settings.
- Whisper hallucinates on silence ("Продолжение следует…") → filtered in Ears.IsJunk.
- A second running copy (installed + dev) shares llama port 18765 → set `MISHKA_LLAMA_PORT` for dev runs.
- Hotkeys pressed by the owner work; hotkeys injected while an elevated window is in front don't.

## Conventions

Code-behind, no MVVM framework. Small static services. Comments explain *why*. Russian UI strings, English code.
Prefer stdlib/WinAPI over new packages. Dangerous actions (shutdown, delete, network reset) always confirm.
