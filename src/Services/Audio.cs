using System.Diagnostics;
using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace MishkaHelper.Core;

public record AudioDevice(string Id, string Name, bool IsDefault, DataFlow Flow);
public record AudioApp(string Name, int Pid, float Volume, bool Muted, AudioSessionControl Session);

public static class Audio
{
    static readonly MMDeviceEnumerator En = new();
    static Notifier? _notifier;

    /// <summary>Fires on default-device/volume changes (from any thread).</summary>
    public static event Action? Changed;

    public static void Start()
    {
        _notifier = new Notifier();
        En.RegisterEndpointNotificationCallback(_notifier);
        WatchCap();
    }

    // Volume limiter: clamp the default output to Settings.VolumeCap whenever anything raises it.
    static MMDevice? _capDev;

    static void WatchCap()
    {
        try
        {
            if (_capDev != null) _capDev.AudioEndpointVolume.OnVolumeNotification -= OnVolume;
            _capDev = Default(DataFlow.Render);
            if (_capDev != null) _capDev.AudioEndpointVolume.OnVolumeNotification += OnVolume;
        }
        catch (Exception e) { Log.Error(e); }
    }

    static void OnVolume(AudioVolumeNotificationData d)
    {
        int cap = Settings.Current.VolumeCap;
        if (cap > 0 && d.MasterVolume * 100 > cap + 0.5)
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Volume = cap;
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Osd.Show("", $"Лимит громкости {cap}%", cap));
            });
    }

    static MMDevice? Default(DataFlow f)
    {
        try { return En.GetDefaultAudioEndpoint(f, Role.Multimedia); } catch { return null; }
    }

    // Output
    public static int Volume
    {
        get => (int)Math.Round((Default(DataFlow.Render)?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0) * 100);
        set { if (Default(DataFlow.Render) is { } d) d.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0, 100) / 100f; }
    }

    public static bool Muted
    {
        get => Default(DataFlow.Render)?.AudioEndpointVolume.Mute ?? false;
        set { if (Default(DataFlow.Render) is { } d) d.AudioEndpointVolume.Mute = value; }
    }

    // Microphone
    public static int MicVolume
    {
        get => (int)Math.Round((Default(DataFlow.Capture)?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0) * 100);
        set { if (Default(DataFlow.Capture) is { } d) d.AudioEndpointVolume.MasterVolumeLevelScalar = Math.Clamp(value, 0, 100) / 100f; }
    }

    public static bool MicMuted
    {
        get => Default(DataFlow.Capture)?.AudioEndpointVolume.Mute ?? false;
        set { if (Default(DataFlow.Capture) is { } d) d.AudioEndpointVolume.Mute = value; }
    }

    public static float Peak => Default(DataFlow.Render)?.AudioMeterInformation.MasterPeakValue ?? 0;

    public static List<AudioDevice> Devices(DataFlow flow)
    {
        var def = Default(flow)?.ID;
        return En.EnumerateAudioEndPoints(flow, DeviceState.Active)
            .Select(d => new AudioDevice(d.ID, d.FriendlyName, d.ID == def, flow)).ToList();
    }

    public static string DefaultName(DataFlow f) => Default(f)?.FriendlyName ?? "—";

    public static void SetDefault(string id)
    {
        var pc = (IPolicyConfig)new PolicyConfigClient();
        foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
            pc.SetDefaultEndpoint(id, role);
        Changed?.Invoke();
    }

    /// <summary>Cycle default output; returns the new device name.</summary>
    public static string NextOutput()
    {
        var list = Devices(DataFlow.Render);
        if (list.Count == 0) return "Нет устройств";
        int i = list.FindIndex(d => d.IsDefault);
        var next = list[(i + 1) % list.Count];
        SetDefault(next.Id);
        return next.Name;
    }

    // Per-app mixer
    public static List<AudioApp> Apps()
    {
        var res = new List<AudioApp>();
        var d = Default(DataFlow.Render);
        if (d == null) return res;
        var sessions = d.AudioSessionManager.Sessions;
        for (int i = 0; i < sessions.Count; i++)
        {
            var s = sessions[i];
            if (s.State == AudioSessionState.AudioSessionStateExpired) continue;
            int pid = (int)s.GetProcessID;
            string name;
            if (s.IsSystemSoundsSession) name = "Системные звуки";
            else
            {
                try
                {
                    var p = Process.GetProcessById(pid);
                    name = string.IsNullOrWhiteSpace(p.MainWindowTitle) ? p.ProcessName : p.ProcessName;
                    try { name = p.MainModule?.FileVersionInfo.FileDescription is { Length: > 0 } fd ? fd : name; } catch { }
                }
                catch { continue; }
            }
            res.Add(new AudioApp(name, pid, s.SimpleAudioVolume.Volume, s.SimpleAudioVolume.Mute, s));
        }
        return res.GroupBy(a => a.Pid).Select(g => g.First()).ToList();
    }

    sealed class Notifier : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState newState)
        {
            if (newState == DeviceState.Active) MaybeAutoSwitch(deviceId);
            Changed?.Invoke();
        }
        public void OnDeviceAdded(string id) => Changed?.Invoke();
        public void OnDeviceRemoved(string id) => Changed?.Invoke();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string id)
        {
            if (flow == DataFlow.Render && role == Role.Multimedia) ThreadPool.QueueUserWorkItem(_ => WatchCap());
            Changed?.Invoke();
        }
        public void OnPropertyValueChanged(string id, PropertyKey key) { }
    }

    /// <summary>New headphones/headset came online → make them default (if enabled in settings).</summary>
    static void MaybeAutoSwitch(string id)
    {
        if (!Settings.Current.AutoSwitchAudio) return;
        try
        {
            var d = En.GetDevice(id);
            if (d.DataFlow != DataFlow.Render) return;
            var name = d.FriendlyName.ToLowerInvariant();
            // Internal speakers never steal default; anything else that appears does.
            if (name.Contains("speaker") || name.Contains("динамик")) return;
            System.Windows.Application.Current.Dispatcher.BeginInvoke(() =>
            {
                SetDefault(id);
                Osd.Show("", d.FriendlyName);
            });
        }
        catch { }
    }

    // Undocumented but stable since Windows 7; used by every audio switcher.
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")] class PolicyConfigClient { }

    enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(string id, IntPtr fmt);
        [PreserveSig] int GetDeviceFormat(string id, bool def, IntPtr fmt);
        [PreserveSig] int ResetDeviceFormat(string id);
        [PreserveSig] int SetDeviceFormat(string id, IntPtr a, IntPtr b);
        [PreserveSig] int GetProcessingPeriod(string id, bool def, IntPtr a, IntPtr b);
        [PreserveSig] int SetProcessingPeriod(string id, IntPtr p);
        [PreserveSig] int GetShareMode(string id, IntPtr m);
        [PreserveSig] int SetShareMode(string id, IntPtr m);
        [PreserveSig] int GetPropertyValue(string id, IntPtr key, IntPtr v);
        [PreserveSig] int SetPropertyValue(string id, IntPtr key, IntPtr v);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, ERole role);
        [PreserveSig] int SetEndpointVisibility(string id, bool visible);
    }
}
