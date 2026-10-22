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
