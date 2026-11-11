using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace MishkaHelper.Core;

/// <summary>Mouse/keyboard injection and screen snapshots for the remotes (Wi-Fi page and Bluetooth).</summary>
public static class InputSim
{
    [StructLayout(LayoutKind.Sequential)] struct MOUSEINPUT { public int dx, dy; public int mouseData; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Sequential)] struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }
    [StructLayout(LayoutKind.Explicit)] struct INPUT { [FieldOffset(0)] public uint type; [FieldOffset(8)] public MOUSEINPUT mi; [FieldOffset(8)] public KEYBDINPUT ki; }
    [DllImport("user32.dll")] static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] static extern bool SetCursorPos(int x, int y);

    static void Send(params INPUT[] i) => SendInput((uint)i.Length, i, Marshal.SizeOf<INPUT>());
    static INPUT M(uint flags, int dx = 0, int dy = 0, int data = 0) => new() { type = 0, mi = new MOUSEINPUT { dx = dx, dy = dy, dwFlags = flags, mouseData = data } };
    /// <summary>Marks keys we send ourselves, so a keyboard hook can tell them from anyone else's.</summary>
    public static readonly IntPtr Mark = new(0x4D534B41);
    static INPUT K(ushort vk, ushort scan, uint flags) => new() { type = 1, ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags, extra = Mark } };

    public static void Move(int dx, int dy) => Send(M(0x0001, dx, dy));

    public static void Click(string button = "left", bool dbl = false)
    {
        var (down, up) = button == "right" ? (0x0008u, 0x0010u) : button == "middle" ? (0x0020u, 0x0040u) : (0x0002u, 0x0004u);
        Send(M(down), M(up));
        if (dbl) Send(M(down), M(up));
    }

    /// <summary>One edge of the wheel or a side button: MouseMiddle / Mouse4 / Mouse5.</summary>
    public static void Button(string button, bool down)
    {
        if (button == "MouseMiddle") Send(M(down ? 0x0020u : 0x0040u));
        else Send(M(down ? 0x0080u : 0x0100u, data: button == "Mouse4" ? 1 : 2));
    }

    public static void Scroll(int notches) => Send(M(0x0800, data: notches * 120));

    /// <summary>Tap on the screen picture: relative 0..1 coordinates of the primary screen.</summary>
    public static void TapAt(double rx, double ry, string button = "left")
    {
        var b = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        SetCursorPos(b.X + (int)(Math.Clamp(rx, 0, 1) * (b.Width - 1)), b.Y + (int)(Math.Clamp(ry, 0, 1) * (b.Height - 1)));
        Click(button);
    }

    /// <summary>Any Unicode text (Cyrillic included), regardless of the keyboard layout.</summary>
    /// <summary>Erases and types in one call: nothing typed by the user or another program can land in the middle.</summary>
    public static void Replace(int erase, string text)
    {
        var e = new List<INPUT>();
        for (int i = 0; i < erase; i++) { e.Add(K(0x08, 0, 0)); e.Add(K(0x08, 0, 0x0002)); }
        foreach (var ch in text) { e.Add(K(0, ch, 0x0004)); e.Add(K(0, ch, 0x0004 | 0x0002)); }
        Send(e.ToArray());
    }

    public static void Type(string text)
    {
        foreach (var ch in text.Replace("\r\n", "\n"))
        {
            if (ch == '\n') { Key("enter"); continue; }
            Send(K(0, ch, 0x0004), K(0, ch, 0x0004 | 0x0002));
        }
    }

    static readonly Dictionary<string, ushort> Vk = new()
    {
        ["enter"] = 0x0D, ["backspace"] = 0x08, ["tab"] = 0x09, ["esc"] = 0x1B, ["space"] = 0x20, ["delete"] = 0x2E,
        ["left"] = 0x25, ["up"] = 0x26, ["right"] = 0x27, ["down"] = 0x28, ["home"] = 0x24, ["end"] = 0x23,
        ["pageup"] = 0x21, ["pagedown"] = 0x22, ["win"] = 0x5B, ["ctrl"] = 0x11, ["alt"] = 0x12, ["shift"] = 0x10,
        ["f5"] = 0x74, ["f11"] = 0x7A, ["play"] = 0xB3, ["next"] = 0xB0, ["prev"] = 0xB1,
        ["volup"] = 0xAF, ["voldown"] = 0xAE, ["mute"] = 0xAD,
    };

    // Arrows and the block above them are "extended" keys. Without the flag Windows takes them for the numeric keypad,
    // where Shift doesn't select: Ctrl+Shift+Left only moved the caret.
    internal static uint ExtendedFlag(ushort vk) => vk is 0x21 or 0x22 or 0x23 or 0x24 or 0x25 or 0x26 or 0x27 or 0x28 or 0x2D or 0x2E or 0x5B or 0x5C ? 1u : 0u;

    /// <summary>"enter", "alt+tab", "ctrl+c", "win+d"…</summary>
    public static bool Key(string combo)
    {
        var parts = combo.ToLowerInvariant().Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var vks = new List<ushort>();
        foreach (var p in parts)
        {
            if (Vk.TryGetValue(p, out var v)) vks.Add(v);
            else if (p.Length == 1 && char.IsLetterOrDigit(p[0]) && p[0] < 128) vks.Add(char.ToUpperInvariant(p[0]));
            else return false;
        }
        foreach (var v in vks) Send(K(v, 0, ExtendedFlag(v)));
        for (int i = vks.Count - 1; i >= 0; i--) Send(K(vks[i], 0, 0x0002u | ExtendedFlag(vks[i])));
        return true;
    }

    static readonly ImageCodecInfo Jpeg = ImageCodecInfo.GetImageEncoders().First(e => e.MimeType == "image/jpeg");

    /// <summary>Primary screen as JPEG, scaled to maxWidth.</summary>
    public static byte[] Screenshot(int maxWidth = 1280, long quality = 55)
    {
        var b = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
        using var full = new Bitmap(b.Width, b.Height);
        using (var g = Graphics.FromImage(full)) g.CopyFromScreen(b.X, b.Y, 0, 0, b.Size);
        double k = Math.Min(1, maxWidth / (double)b.Width);
        using var small = new Bitmap(full, (int)(b.Width * k), (int)(b.Height * k));
        using var ms = new MemoryStream();
        var p = new EncoderParameters(1) { Param = { [0] = new EncoderParameter(Encoder.Quality, quality) } };
        small.Save(ms, Jpeg, p);
        return ms.ToArray();
    }
}
