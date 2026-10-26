using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace MishkaHelper.Core;

public static class Display
{
    // Brightness (internal panel via WMI)
    public static int Brightness
    {
        get
        {
            try
            {
                using var s = new ManagementObjectSearcher(@"root\wmi", "SELECT CurrentBrightness FROM WmiMonitorBrightness");
                foreach (ManagementObject o in s.Get()) return Convert.ToInt32(o["CurrentBrightness"]);
            }
            catch { }
            return -1;
        }
        set
        {
            try
            {
                using var s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM WmiMonitorBrightnessMethods");
                foreach (ManagementObject o in s.Get())
                    o.InvokeMethod("WmiSetBrightness", new object[] { 1u, (byte)Math.Clamp(value, 0, 100) });
            }
            catch (Exception e) { Log.Error(e); }
        }
    }

    public static bool HasBrightness => Brightness >= 0;

    // Display modes (primary display)
    static Native.DEVMODE Current()
    {
        var dm = new Native.DEVMODE { dmSize = (short)Marshal.SizeOf<Native.DEVMODE>() };
        Native.EnumDisplaySettings(null, Native.ENUM_CURRENT_SETTINGS, ref dm);
        return dm;
    }

    public static int RefreshRate => Current().dmDisplayFrequency;
    public static (int W, int H) Resolution { get { var c = Current(); return (c.dmPelsWidth, c.dmPelsHeight); } }

    static List<Native.DEVMODE> Modes()
    {
        var res = new List<Native.DEVMODE>();
        var dm = new Native.DEVMODE { dmSize = (short)Marshal.SizeOf<Native.DEVMODE>() };
        for (int i = 0; Native.EnumDisplaySettings(null, i, ref dm); i++)
            if (dm.dmBitsPerPel == 32) res.Add(dm);
        return res;
    }

    public static int[] RefreshRates()
    {
        var (w, h) = Resolution;
        return Modes().Where(m => m.dmPelsWidth == w && m.dmPelsHeight == h)
            .Select(m => m.dmDisplayFrequency).Distinct().OrderBy(x => x).ToArray();
    }

    public static (int W, int H)[] Resolutions() =>
        Modes().Select(m => (m.dmPelsWidth, m.dmPelsHeight)).Distinct().OrderByDescending(r => r.Item1 * r.Item2).ToArray();

    public static bool SetMode(int? w = null, int? h = null, int? hz = null)
    {
        var dm = Current();
        if (w is int ww && h is int hh) { dm.dmPelsWidth = ww; dm.dmPelsHeight = hh; dm.dmFields |= Native.DM_PELSWIDTH | Native.DM_PELSHEIGHT; }
        if (hz is int f)
        {
            // Pick the closest supported rate at the target resolution.
            var rates = Modes().Where(m => m.dmPelsWidth == dm.dmPelsWidth && m.dmPelsHeight == dm.dmPelsHeight).Select(m => m.dmDisplayFrequency).ToList();
            if (rates.Count > 0) f = rates.OrderBy(r => Math.Abs(r - f)).First();
            dm.dmDisplayFrequency = f; dm.dmFields |= Native.DM_DISPLAYFREQUENCY;
        }
        return Native.ChangeDisplaySettingsEx(null, ref dm, IntPtr.Zero, Native.CDS_UPDATEREGISTRY, IntPtr.Zero) == 0;
    }

    // ---- TV over HDMI: projection mode and per-monitor resolution ----

    /// <summary>"internal" (only PC), "clone", "extend", "external" (only TV) — the same as Win+P.</summary>
    public static void Project(string mode)
    {
        if (mode is not ("internal" or "clone" or "extend" or "external")) return;
        Process.Start(new ProcessStartInfo(Shell.Sys("DisplaySwitch.exe"), "/" + mode) { UseShellExecute = false, CreateNoWindow = true });
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool EnumDisplayDevices(string? device, int i, ref DISPLAY_DEVICE dd, int flags);
    const int AttachedToDesktop = 1, PrimaryDevice = 4;

    public record Monitor(string Device, string Name, bool Primary, string Mode, string[] Modes);

    /// <summary>Screens in use now, with "1920x1080@60" modes (best rate per resolution, largest first).</summary>
    public static List<Monitor> Monitors()
    {
        var res = new List<Monitor>();
        for (int i = 0; ; i++)
        {
            var dd = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
            if ((dd.StateFlags & AttachedToDesktop) == 0) continue;
            var mon = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
            var name = EnumDisplayDevices(dd.DeviceName, 0, ref mon, 0) && mon.DeviceString.Length > 0 ? mon.DeviceString : dd.DeviceName;
            var cur = new Native.DEVMODE { dmSize = (short)Marshal.SizeOf<Native.DEVMODE>() };
            Native.EnumDisplaySettings(dd.DeviceName, Native.ENUM_CURRENT_SETTINGS, ref cur);
            var modes = new List<Native.DEVMODE>();
            var dm = new Native.DEVMODE { dmSize = (short)Marshal.SizeOf<Native.DEVMODE>() };
            for (int m = 0; Native.EnumDisplaySettings(dd.DeviceName, m, ref dm); m++) if (dm.dmBitsPerPel == 32) modes.Add(dm);
            var list = modes.GroupBy(m => (m.dmPelsWidth, m.dmPelsHeight)).OrderByDescending(g => g.Key.dmPelsWidth * g.Key.dmPelsHeight)
                .Select(g => $"{g.Key.dmPelsWidth}x{g.Key.dmPelsHeight}@{g.Max(m => m.dmDisplayFrequency)}").Take(14).ToArray();
            res.Add(new(dd.DeviceName, name, (dd.StateFlags & PrimaryDevice) != 0, $"{cur.dmPelsWidth}x{cur.dmPelsHeight}@{cur.dmDisplayFrequency}", list));
        }
        return res;
    }

    /// <summary>"1920x1080@60" on one screen, e.g. the TV. Only modes Windows lists for it.</summary>
    public static bool SetMonitorMode(string device, string mode)
    {
        var mon = Monitors().FirstOrDefault(m => m.Device == device);
        if (mon == null || !mon.Modes.Contains(mode)) return false;
        var p = mode.Split('x', '@').Select(int.Parse).ToArray();
        var dm = new Native.DEVMODE { dmSize = (short)Marshal.SizeOf<Native.DEVMODE>() };
        Native.EnumDisplaySettings(device, Native.ENUM_CURRENT_SETTINGS, ref dm);
        dm.dmPelsWidth = p[0]; dm.dmPelsHeight = p[1]; dm.dmDisplayFrequency = p[2];
        dm.dmFields |= Native.DM_PELSWIDTH | Native.DM_PELSHEIGHT | Native.DM_DISPLAYFREQUENCY;
        return Native.ChangeDisplaySettingsEx(device, ref dm, IntPtr.Zero, Native.CDS_UPDATEREGISTRY, IntPtr.Zero) == 0;
    }

    // Night light: CloudStore blob. Inner record gets "02 01" when on, and its length byte (index 18) grows by 2.
    const string NightKey = @"Software\Microsoft\Windows\CurrentVersion\CloudStore\Store\DefaultAccount\Current\default$windows.data.bluelightreduction.bluelightreductionstate\windows.data.bluelightreduction.bluelightreductionstate";

    static byte[]? NightBlob()
    {
        using var k = Registry.CurrentUser.OpenSubKey(NightKey);
        return k?.GetValue("Data") as byte[];
    }

    public static bool? NightLight
    {
        get
        {
            var d = NightBlob();
            if (d == null || d.Length < 25) return null;
            return d[23] == 0x02 && d[24] == 0x01;
        }
        set
        {
            var d = NightBlob();
            if (d == null || d.Length < 25 || value == null || NightLight == value) return;
            var list = d.ToList();
            if (value == true) { list.InsertRange(23, new byte[] { 0x02, 0x01 }); list[18] += 2; }
            else { list.RemoveRange(23, 2); list[18] -= 2; }
            // Bump the varint timestamp so the shell notices the change.
            for (int i = 10; i < 15; i++) if ((list[i] & 0x7F) != 0x7F) { list[i]++; break; }
            using var k = Registry.CurrentUser.OpenSubKey(NightKey, true);
            k?.SetValue("Data", list.ToArray(), RegistryValueKind.Binary);
        }
    }
}
