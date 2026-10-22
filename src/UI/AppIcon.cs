using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace MishkaHelper.Core;

/// <summary>Real icons of programs/shortcuts/folders (as Explorer shows them) for the radial menu.</summary>
public static class AppIcon
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct SHFILEINFO
    {
        public IntPtr hIcon; public int iIcon; public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern IntPtr SHGetFileInfo(string path, uint attrs, ref SHFILEINFO info, uint size, uint flags);
    [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr h);
    const uint SHGFI_ICON = 0x100, SHGFI_LARGEICON = 0;

    static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? For(Launcher l)
    {
        // A folder opened through explorer.exe shows the folder's own icon.
        var target = Environment.ExpandEnvironmentVariables(l.Path);
        if (Path.GetFileName(target).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase) && l.Args.Trim('"') is { Length: > 0 } a && Directory.Exists(Environment.ExpandEnvironmentVariables(a)))
            target = Environment.ExpandEnvironmentVariables(a);
        if (target.Contains("://") || target.EndsWith(':')) return null;   // sites, ms-settings: → glyph
        if (!Path.IsPathRooted(target)) target = Resolve(target) ?? target;
        return For(target);
    }

    public static ImageSource? For(string path)
    {
        if (Cache.TryGetValue(path, out var cached)) return cached;
        ImageSource? img = null;
        try
        {
            var info = new SHFILEINFO();
            if (SHGetFileInfo(path, 0, ref info, (uint)Marshal.SizeOf<SHFILEINFO>(), SHGFI_ICON | SHGFI_LARGEICON) != IntPtr.Zero && info.hIcon != IntPtr.Zero)
            {
                var bs = Imaging.CreateBitmapSourceFromHIcon(info.hIcon, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                img = bs;
                DestroyIcon(info.hIcon);
            }
        }
        catch { }
        return Cache[path] = img;
    }

    /// <summary>"taskmgr.exe" → full path via System32 / PATH.</summary>
    static string? Resolve(string exe)
    {
        foreach (var dir in new[] { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) }
                     .Concat((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';')))
        {
            try { var p = Path.Combine(dir, exe); if (File.Exists(p)) return p; } catch { }
        }
        return null;
    }
}
