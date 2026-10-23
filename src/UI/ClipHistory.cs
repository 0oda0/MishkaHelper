using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace MishkaHelper.Core;

/// <summary>
/// Clipboard history (Ctrl+Alt+V): last 100 texts. Like Windows' own Win+V, only pinned items survive a restart,
/// and anything a password manager marks as private is never recorded.
/// </summary>
public static class ClipHistory
{
    public class Item { public string Text { get; set; } = ""; public bool Pinned { get; set; } public DateTime When { get; set; } }

    static readonly string PinFile = Path.Combine(Settings.Dir, "clipboard-pinned.json");
    public static readonly List<Item> Items = new();
    static HwndSource? _src;
    static bool _ignoreNext;
    static Window? _w;

    [DllImport("user32.dll")] static extern bool AddClipboardFormatListener(IntPtr h);
    [DllImport("user32.dll")] static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] static extern bool IsClipboardFormatAvailable(uint f);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
    const int WM_CLIPBOARDUPDATE = 0x031D;
    static readonly uint Exclude = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    static readonly uint NoHistory = RegisterClipboardFormat("CanIncludeInClipboardHistory");   // present (as 0) = "don't keep"

    public static void Start()
    {
        try { if (File.Exists(PinFile)) Items.AddRange(JsonSerializer.Deserialize<List<Item>>(File.ReadAllText(PinFile)) ?? new()); } catch (Exception e) { Log.Error(e); }
        // Message-only window just to receive WM_CLIPBOARDUPDATE.
        _src = new HwndSource(new HwndSourceParameters("MishkaClip") { ParentWindow = new IntPtr(-3), Width = 0, Height = 0, WindowStyle = 0 });
        _src.AddHook(Hook);
        AddClipboardFormatListener(_src.Handle);
    }

    static IntPtr Hook(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
    {
        if (msg == WM_CLIPBOARDUPDATE) Application.Current.Dispatcher.BeginInvoke(OnChange);
        return IntPtr.Zero;
    }

    static void OnChange()
    {
        if (_ignoreNext) { _ignoreNext = false; return; }
        if (!Settings.Current.ClipboardHistory) return;
        try
        {
            if (IsClipboardFormatAvailable(Exclude) || IsClipboardFormatAvailable(NoHistory) || !Clipboard.ContainsText()) return;
            var t = Clipboard.GetText();
            if (string.IsNullOrWhiteSpace(t) || t.Length > 100_000) return;
            var old = Items.FirstOrDefault(i => i.Text == t);
            if (old != null) Items.Remove(old);
            Items.Insert(0, new Item { Text = t, Pinned = old?.Pinned == true, When = DateTime.Now });
            var extra = Items.Where(i => !i.Pinned).Skip(100).ToList();
            foreach (var x in extra) Items.Remove(x);
        }
        catch { }   // clipboard busy in another app — skip this copy
    }

    static void SavePins()
    {
        try { Directory.CreateDirectory(Path.GetDirectoryName(PinFile)!); File.WriteAllText(PinFile, JsonSerializer.Serialize(Items.Where(i => i.Pinned))); }
        catch (Exception e) { Log.Error(e); }
    }

    public static void Clear() { Items.RemoveAll(i => !i.Pinned); }

    public static void Toggle()
    {
        if (_w != null) { _w.Close(); return; }
        var target = GetForegroundWindow();
        var w = Floating.Create(clickThrough: false, noActivate: false);
        var box = new TextBox { FontSize = 16, MinWidth = 520, Padding = new Thickness(10, 8, 10, 8), BorderThickness = new Thickness(0) };
        var list = new ListBox { MaxHeight = 420, Width = 520, Margin = new Thickness(0, 8, 0, 0) };
        ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
        var sp = new StackPanel();
        sp.Children.Add(box); sp.Children.Add(list);
        sp.Children.Add(UiKit.Text("Enter — вставить · Ctrl+P — закрепить · Del — удалить", 11, true));
        var panel = Floating.Panel(sp, 16); panel.Padding = new Thickness(12);
        w.Content = panel;

        void Filter()
        {
            var q = box.Text.ToLowerInvariant();
            list.Items.Clear();
            foreach (var i in Items.OrderByDescending(i => i.Pinned).Where(i => q.Length == 0 || i.Text.ToLowerInvariant().Contains(q)).Take(60))
            {
                var one = i.Text.Replace("\r", "").Replace('\n', ' ').Trim();
                var row = new DockPanel { Tag = i };
                var when = UiKit.Text((i.Pinned ? "📌 " : "") + i.When.ToString("HH:mm"), 11, true); DockPanel.SetDock(when, Dock.Right);
                row.Children.Add(when);
                row.Children.Add(new TextBlock { Text = one.Length > 90 ? one[..90] + "…" : one, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = i.Text.Length > 90 ? i.Text[..Math.Min(1000, i.Text.Length)] : null });
                list.Items.Add(row);
            }
            list.SelectedIndex = 0;
        }

        Item? Sel() => (list.SelectedItem as FrameworkElement)?.Tag as Item;

        async void Paste()
        {
            if (Sel() is not { } i) return;
            w.Close();
            _ignoreNext = true;
            Clipboard.SetText(i.Text);
            Items.Remove(i); Items.Insert(0, i);
            Native.SetForegroundWindow(target);
            await Task.Delay(120);
            keybd_event(0x11, 0, 0, UIntPtr.Zero); keybd_event(0x56, 0, 0, UIntPtr.Zero);   // Ctrl+V
            keybd_event(0x56, 0, 2, UIntPtr.Zero); keybd_event(0x11, 0, 2, UIntPtr.Zero);
        }

        box.TextChanged += (_, _) => Filter();
        box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Down) { list.SelectedIndex = Math.Min(list.Items.Count - 1, list.SelectedIndex + 1); list.ScrollIntoView(list.SelectedItem); e.Handled = true; }
            else if (e.Key == Key.Up) { list.SelectedIndex = Math.Max(0, list.SelectedIndex - 1); list.ScrollIntoView(list.SelectedItem); e.Handled = true; }
            else if (e.Key == Key.Enter) { Paste(); e.Handled = true; }
            else if (e.Key == Key.Escape) w.Close();
            else if (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control && Sel() is { } p) { p.Pinned = !p.Pinned; SavePins(); Filter(); e.Handled = true; }
            else if (e.Key == Key.Delete && box.Text.Length == 0 && Sel() is { } d) { Items.Remove(d); if (d.Pinned) SavePins(); Filter(); e.Handled = true; }
        };
        list.MouseDoubleClick += (_, _) => Paste();
        bool closing = false;
        w.Closing += (_, _) => closing = true;
        w.Deactivated += (_, _) => { if (!closing) w.Close(); };
        w.Closed += (_, _) => _w = null;
        Filter();
        _w = w;
        w.Show();
        var wa = SystemParameters.WorkArea;
        w.Left = wa.Left + (wa.Width - w.ActualWidth) / 2;
        w.Top = wa.Top + wa.Height * 0.18;
        Native.SetForegroundWindow(new WindowInteropHelper(w).Handle);
        w.Activate();
        box.Focus();
    }
}
