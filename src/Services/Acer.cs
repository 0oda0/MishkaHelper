using System.Management;

namespace MishkaHelper.Core;

/// <summary>
/// Acer Nitro/Predator gaming WMI (what NitroSense uses). Codes follow the Linux acer-wmi
/// "predator v4" driver and were verified on a Nitro AN515-58.
/// </summary>
public static class Acer
{
    static ManagementObject? _obj;
    static readonly object Lock = new();

    public static readonly (int Id, string Name)[] AllModes =
    {
        (0, "Тихий"), (1, "Баланс"), (4, "Производительность"), (5, "Турбо"), (6, "Эко"),
    };

    public static bool Available { get; } = Init();

    static bool Init()
    {
        try
        {
            using var s = new ManagementObjectSearcher(@"root\wmi", "SELECT * FROM AcerGamingFunction");
            _obj = s.Get().Cast<ManagementObject>().FirstOrDefault();
            return _obj != null;
        }
        catch { return false; }
    }

    static ulong Call(string method, object input)
    {
        if (_obj == null) return 0;
        lock (Lock)
        {
            using var p = _obj.GetMethodParameters(method);
            p["gmInput"] = input;
            using var r = _obj.InvokeMethod(method, p, null);
            return Convert.ToUInt64(r["gmOutput"]);
        }
    }

    static byte[]? CallBytes(string method, object input)
    {
        if (_obj == null) return null;
        lock (Lock)
        {
            using var p = _obj.GetMethodParameters(method);
            p["gmInput"] = input;
            using var r = _obj.InvokeMethod(method, p, null);
            return r["gmOutput"] as byte[];
        }
    }

    static int Sensor(int id) => (int)((Call("GetGamingSysInfo", (uint)(0x01 | id << 8)) >> 8) & 0xFFFF);

    public static int CpuTemp => Safe(() => Sensor(0x01));
    public static int CpuFanRpm => Safe(() => Sensor(0x02));
    public static int SysTemp => Safe(() => Sensor(0x03));
    public static int GpuFanRpm => Safe(() => Sensor(0x06));
    public static int GpuTemp => Safe(() => Sensor(0x0A));

    static int Safe(Func<int> f) { try { return f(); } catch { return 0; } }

    // Thermal profile
    public static (int Id, string Name)[] SupportedModes()
    {
        try
        {
            var mask = (Call("GetGamingMiscSetting", 0x0Au) >> 8) & 0xFF;
            return AllModes.Where(m => (mask & (1UL << m.Id)) != 0).ToArray();
        }
        catch { return Array.Empty<(int, string)>(); }
    }

    public static int Mode
    {
        get { try { return (int)((Call("GetGamingMiscSetting", 0x0Bu) >> 8) & 0xFF); } catch { return -1; } }
        set { try { Call("SetGamingMiscSetting", (ulong)(0x0B | value << 8)); } catch (Exception e) { Log.Error(e); } }
    }

    // Fans: per fan 2 bits, 1 = auto, 2 = max, 3 = custom. CPU fan bit 0 / GPU fan bit 3 in the low byte.
    public enum FanMode { Auto = 1, Max = 2, Custom = 3 }

    public static FanMode Fans
    {
        get
        {
            try { return (FanMode)((Call("GetGamingFanBehavior", 0x9u) >> 8) & 0x3); }
            catch { return FanMode.Auto; }
        }
        set
        {
            int m = (int)value;
            try { Call("SetGamingFanBehavior", (ulong)(0x09 | m << 16 | m << 22)); } catch (Exception e) { Log.Error(e); }
        }
    }

    /// <summary>Custom fan speed in percent. fan: 1 = CPU, 4 = GPU.</summary>
    public static int GetFanPercent(int fan) { try { return (int)((Call("GetGamingFanSpeed", (uint)fan) >> 8) & 0xFF); } catch { return 0; } }
    public static void SetFanPercent(int fan, int pct)
    {
        pct = Math.Clamp(pct, 0, 100);
        try { Call("SetGamingFanSpeed", (ulong)(fan | pct << 8)); } catch (Exception e) { Log.Error(e); }
    }

    // Keyboard: 4 zones (bits 1,2,4,8). Static colour per zone + animated effects.
    public static readonly string[] Effects = { "Статика", "Дыхание", "Неон", "Волна", "Сдвиг", "Зум", "Метеор", "Мерцание" };

    public static (byte R, byte G, byte B) GetZone(int zoneBit)
    {
        try
        {
            var v = Call("GetGamingRgbKb", (uint)zoneBit) >> 8;
            return ((byte)(v & 0xFF), (byte)(v >> 8 & 0xFF), (byte)(v >> 16 & 0xFF));
        }
        catch { return (0, 0, 0); }
    }

    public static void SetZone(int zoneMask, byte r, byte g, byte b)
    {
        try { Call("SetGamingRgbKb", (ulong)(uint)zoneMask | (ulong)r << 8 | (ulong)g << 16 | (ulong)b << 24); }
        catch (Exception e) { Log.Error(e); }
    }

    /// <summary>effect 0..7, speed 0..9, brightness 0..100, direction 1 = right-to-left, 2 = left-to-right.</summary>
    public static void SetKeyboard(int effect, int speed, int brightness, int direction, byte r, byte g, byte b)
    {
        var buf = new byte[16];
        buf[0] = (byte)effect; buf[1] = (byte)speed; buf[2] = (byte)Math.Clamp(brightness, 0, 100);
        buf[4] = (byte)direction; buf[5] = r; buf[6] = g; buf[7] = b; buf[9] = 1;
        try { CallBytes("SetGamingKBBacklight", buf); } catch (Exception e) { Log.Error(e); }
    }

    public static byte[]? GetKeyboardRaw() { try { return CallBytes("GetGamingKBBacklight", 1u); } catch { return null; } }
}
