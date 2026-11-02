using System.Runtime.InteropServices;
using System.Windows;

namespace MishkaHelper.Core;

/// <summary>
/// "ghbdtn" → "привет" on a hotkey: the selected text, or the last word if nothing is selected. Nothing watches the keyboard;
/// it acts only when pressed, and keeps nothing. A password field refuses to copy, so it is left alone.
/// </summary>
public static class LayoutFix
{
    const string En = "`qwertyuiop[]asdfghjkl;'zxcvbnm,./~QWERTYUIOP{}ASDFGHJKL:\"ZXCVBNM<>?@#$^&";
    const string Ru = "ёйцукенгшщзхъфывапролджэячсмитьбю.ЁЙЦУКЕНГШЩЗХЪФЫВАПРОЛДЖЭЯЧСМИТЬБЮ,\"№;:?";

    /// <summary>Into the other layout, keeping what has no pair. Which way is decided by the letters in the text. No letters at all: null.</summary>
    public static string? Convert(string text)
    {
        int ru = text.Count(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё');
        int en = text.Count(char.IsAsciiLetter);
        if (ru == 0 && en == 0) return null;
        // A word typed in Latin by mistake may also hold "б" typed as a comma; the bigger side tells the direction.
        var (from, to) = en >= ru ? (En, Ru) : (Ru, En);
        return string.Concat(text.Select(c => from.IndexOf(c) is >= 0 and var i ? to[i] : c));
    }

    /// <summary>Which keyboard to leave on after fixing: the language the text has just become.</summary>
    internal static bool EndsRussian(string fixedText) =>
        fixedText.Count(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё') >= fixedText.Count(char.IsAsciiLetter);

    /// <summary>Some editors copy the whole current line when nothing is selected; that isn't a selection.</summary>
    internal static bool IsLineCopy(string copied) => copied.EndsWith('\n');

    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr LoadKeyboardLayout(string id, uint flags);
    [DllImport("user32.dll")] static extern int GetKeyboardLayoutList(int n, IntPtr[]? list);

    /// <summary>The hotkey's own Ctrl+Alt must be up, or what we send arrives as Ctrl+Alt+C.</summary>
    public static async Task WaitKeysUp()
    {
        for (int i = 0; i < 60 && new[] { 0x11, 0x12, 0x10, 0x5B, 0x5C }.Any(k => GetAsyncKeyState(k) < 0); i++) await Task.Delay(25);
    }

    static async Task<string?> Copy()
    {
        var seq = GetClipboardSequenceNumber();
        ClipHistory.IgnoreNext();
        InputSim.Key("ctrl+c");
        for (int i = 0; i < 16 && GetClipboardSequenceNumber() == seq; i++) await Task.Delay(25);
        if (GetClipboardSequenceNumber() == seq) return null;
        await Task.Delay(20);
        return !ClipHistory.IsPrivateCopy() && Clipboard.ContainsText() ? Clipboard.GetText() : null;
    }

    static void Switch(bool russian)
    {
        var want = (ushort)(russian ? 0x0419 : 0x0409);
        int n = GetKeyboardLayoutList(0, null);
        var list = new IntPtr[n];
        GetKeyboardLayoutList(n, list);
        // Low word = language, high word = keyboard: a US keyboard under the Russian language counts as English too.
        var have = list.FirstOrDefault(h => (ushort)((h.ToInt64() >> 16) & 0xFFFF) == want) is var byKeyboard && byKeyboard != IntPtr.Zero ? byKeyboard : list.FirstOrDefault(h => (ushort)h.ToInt64() == want);
        if (have == IntPtr.Zero) return;   // that language isn't installed: not adding one on the user's behalf
        const int WM_INPUTLANGCHANGEREQUEST = 0x50;
        PostMessage(GetForegroundWindow(), WM_INPUTLANGCHANGEREQUEST, IntPtr.Zero, have);
    }

    public static async void Run()
    {
        try
        {
            await WaitKeysUp();
            // ponytail: only text on the clipboard is put back; a copied picture is lost. Reading all formats fails on delayed clipboard data.
            string? before = null;
            try { if (Clipboard.ContainsText()) before = Clipboard.GetText(); } catch { }
            var text = await Copy();
            bool selected = text is { Length: > 0 } && !IsLineCopy(text);
            if (!selected)
            {
                InputSim.Key("ctrl+shift+left");
                await Task.Delay(40);
                text = await Copy();
            }
            try
            {
                if (string.IsNullOrEmpty(text) || IsLineCopy(text) || Convert(text) is not { } done || done == text)
                {
                    if (!selected) InputSim.Key("right");   // take the word selection back off
                    Osd.Show("", "Нечего исправлять");
                    return;
                }
                InputSim.Type(done);
                Switch(EndsRussian(done));
            }
            finally
            {
                await Task.Delay(60);
                ClipHistory.IgnoreNext();
                try { if (before != null) Clipboard.SetText(before); else Clipboard.Clear(); } catch { }
            }
        }
        catch (Exception e) { Log.Error(e); Osd.Show("", "Не получилось исправить раскладку"); }
    }
}
