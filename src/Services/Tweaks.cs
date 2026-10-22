using Microsoft.Win32;

namespace MishkaHelper.Core;

/// <summary>Small Windows switches stored in HKCU / SystemParametersInfo.</summary>
public static class Tweaks
{
    const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string Advanced = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";
    const string Notif = @"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings";

    static int GetDword(string key, string name, int def)
    {
        using var k = Registry.CurrentUser.OpenSubKey(key);
        return k?.GetValue(name) is int v ? v : def;
    }

    static void SetDword(string key, string name, int v)
    {
        using var k = Registry.CurrentUser.CreateSubKey(key);
        k.SetValue(name, v, RegistryValueKind.DWord);
    }

    public static bool DarkTheme
    {
        get => GetDword(Personalize, "AppsUseLightTheme", 1) == 0;
        set
        {
            SetDword(Personalize, "AppsUseLightTheme", value ? 0 : 1);
            SetDword(Personalize, "SystemUsesLightTheme", value ? 0 : 1);
            Native.BroadcastSettingChange("ImmersiveColorSet");
        }
    }

    public static bool Transparency
    {
        get => GetDword(Personalize, "EnableTransparency", 1) == 1;
        set { SetDword(Personalize, "EnableTransparency", value ? 1 : 0); Native.BroadcastSettingChange("ImmersiveColorSet"); }
    }

    /// <summary>"Do not disturb" = global toast switch.</summary>
    public static bool DoNotDisturb
    {
        get => GetDword(Notif, "NOC_GLOBAL_SETTING_TOASTS_ENABLED", 1) == 0;
        set => SetDword(Notif, "NOC_GLOBAL_SETTING_TOASTS_ENABLED", value ? 0 : 1);
    }

    public static bool ShowExtensions
    {
        get => GetDword(Advanced, "HideFileExt", 1) == 0;
        set { SetDword(Advanced, "HideFileExt", value ? 0 : 1); Native.BroadcastSettingChange("ShellState"); }
    }

    public static bool ShowHidden
    {
        get => GetDword(Advanced, "Hidden", 2) == 1;
        set { SetDword(Advanced, "Hidden", value ? 1 : 2); Native.BroadcastSettingChange("ShellState"); }
    }

    public static bool GameMode
    {
        get => GetDword(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", 1) == 1;
        set => SetDword(@"Software\Microsoft\GameBar", "AutoGameModeEnabled", value ? 1 : 0);
    }

    /// <summary>Sticky keys popup after 5×Shift (classic game annoyance).</summary>
    public static bool StickyKeysShortcut
    {
        get
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Accessibility\StickyKeys");
            return int.TryParse(k?.GetValue("Flags") as string, out var f) && (f & 4) != 0;
        }
        set
        {
            using var k = Registry.CurrentUser.CreateSubKey(@"Control Panel\Accessibility\StickyKeys");
            int.TryParse(k.GetValue("Flags") as string, out var f);
            k.SetValue("Flags", (value ? f | 4 : f & ~4).ToString());
        }
    }

    // Mouse
    public static int MouseSpeed   // 1..20
    {
        get { int v = 10; Native.SystemParametersInfo(Native.SPI_GETMOUSESPEED, 0, ref v, 0); return v; }
        set => Native.SystemParametersInfo(Native.SPI_SETMOUSESPEED, 0, new IntPtr(Math.Clamp(value, 1, 20)), Native.SPIF_UPDATE);
    }

    /// <summary>"Enhance pointer precision" (mouse acceleration).</summary>
    public static bool MouseAcceleration
    {
        get
        {
            var a = new int[3];
            var h = System.Runtime.InteropServices.GCHandle.Alloc(a, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { Native.SystemParametersInfo(0x0003, 0, h.AddrOfPinnedObject(), 0); } finally { h.Free(); }
            return a[2] != 0;
        }
        set
        {
            var a = value ? new[] { 6, 10, 1 } : new[] { 0, 0, 0 };
            var h = System.Runtime.InteropServices.GCHandle.Alloc(a, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { Native.SystemParametersInfo(0x0004, 0, h.AddrOfPinnedObject(), Native.SPIF_UPDATE); } finally { h.Free(); }
        }
    }

    public static bool Animations
    {
        get { int v = 1; Native.SystemParametersInfo(0x1042, 0, ref v, 0); return v != 0; }
        set => Native.SystemParametersInfo(0x1043, 0, new IntPtr(value ? 1 : 0), Native.SPIF_UPDATE);
    }
}
