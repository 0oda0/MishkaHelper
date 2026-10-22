using System.Diagnostics;
using Microsoft.Win32;

namespace MishkaHelper.Core;

public static class Profiles
{
    public static string? Active { get; private set; }
    public static event Action? Changed;

    static string? _beforeApp;      // profile to restore when the game/app exits
    static string? _appTrigger;

    public static void Apply(Profile p, bool osd = true)
    {
        try
        {
            if (p.Volume is int v) Audio.Volume = v;
            if (p.Brightness is int b) Display.Brightness = b;
            if (p.PowerMode is { } pm) Power.Mode = pm;
            if (p.AcerMode is int am && Acer.Available && Acer.SupportedModes().Any(m => m.Id == am)) Acer.Mode = am;
            if (p.RefreshRate is int hz && hz != Display.RefreshRate) Display.SetMode(hz: hz);
            if (p.DoNotDisturb is bool dnd) Tweaks.DoNotDisturb = dnd;
            if (p.NightLight is bool nl) Display.NightLight = nl;
            if (!string.IsNullOrEmpty(p.AudioDevice) && Audio.Devices(NAudio.CoreAudioApi.DataFlow.Render).Any(d => d.Id == p.AudioDevice)) Audio.SetDefault(p.AudioDevice);
            if (p.CleanOnApply) _ = Task.Run(() => { Cleaner.FlushDns(); Cleaner.TrimRam(); });
        }
        catch (Exception e) { Log.Error(e); }
        Active = p.Name;
        Changed?.Invoke();
        if (osd) Osd.Show(p.Icon, "Профиль: " + p.Name);
    }

    public static void Apply(string name)
    {
        var p = Settings.Current.Profiles.FirstOrDefault(x => x.Name == name);
        if (p != null) Apply(p);
    }

    public static void Next()
    {
        var list = Settings.Current.Profiles;
        if (list.Count == 0) return;
        int i = list.FindIndex(p => p.Name == Active);
        Apply(list[(i + 1) % list.Count]);
    }

    /// <summary>Capture the current state into a profile (used by "Сохранить текущее").</summary>
    public static Profile Snapshot(string name) => new()
    {
        Name = name, Volume = Audio.Volume, Brightness = Display.HasBrightness ? Display.Brightness : null, PowerMode = Power.Mode,
        AcerMode = Acer.Available ? Acer.Mode : null, RefreshRate = Display.RefreshRate, DoNotDisturb = Tweaks.DoNotDisturb,
        NightLight = Display.NightLight,
    };

    // Automation: AC/battery and app rules.
    public static void StartAuto()
    {
        SystemEvents.PowerModeChanged += (_, e) =>
        {
            if (e.Mode != PowerModes.StatusChange || !Settings.Current.AutoProfiles) return;
            var name = Power.OnAc ? Settings.Current.OnAcProfile : Settings.Current.OnBatteryProfile;
            if (!string.IsNullOrEmpty(name) && name != Active)
                System.Windows.Application.Current.Dispatcher.BeginInvoke(() => Apply(name));
        };
    }

    /// <summary>Called every few seconds by the scheduler.</summary>
    public static void CheckApps()
    {
        var s = Settings.Current;
        if (!s.AutoProfiles || s.AppRules.Count == 0) return;
        var running = Process.GetProcesses().Select(p => { var n = p.ProcessName; p.Dispose(); return n; }).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hit = s.AppRules.FirstOrDefault(r => running.Contains(r.Process.Replace(".exe", "", StringComparison.OrdinalIgnoreCase)));
        var ui = System.Windows.Application.Current.Dispatcher;
        if (hit != null && _appTrigger == null)
        {
            _appTrigger = hit.Process;
            _beforeApp = Active;
            if (!string.IsNullOrEmpty(hit.Profile)) ui.BeginInvoke(() => Apply(hit.Profile));
            if (Enum.TryParse<ProcessPriorityClass>(hit.Priority, out var prio))
                foreach (var p in Process.GetProcessesByName(hit.Process.Replace(".exe", "", StringComparison.OrdinalIgnoreCase)))
                {
                    try { p.PriorityClass = prio; } catch { }
                    p.Dispose();
                }
        }
        else if (hit == null && _appTrigger != null)
        {
            _appTrigger = null;
            if (_beforeApp != null) ui.BeginInvoke(() => Apply(_beforeApp));
        }
    }
}
