namespace MishkaHelper.Core;

public class AppKey
{
    public string Label { get; set; } = "";
    public string Keys { get; set; } = "";   // "ctrl+b"; a sequence with spaces: "alt w f f" (Office ribbon letters)
    public string Icon { get; set; } = "";
}

public class AppWheel
{
    public string Name { get; set; } = "";
    public List<string> Processes { get; set; } = new();   // without .exe: "winword"
    public bool Enabled { get; set; } = true;
    public List<AppKey> Items { get; set; } = new();
}

/// <summary>
/// The wheel for a work program: with Word, Excel or Photoshop in front, Ctrl+Alt+R opens that program's shortcuts instead of the usual wheel
/// (which stays one segment away). A pick goes back to the program and presses its keys there.
/// </summary>
public static class AppWheels
{
    static AppKey K(string label, string keys, string icon = "") => new() { Label = label, Keys = keys, Icon = icon };

    /// <summary>Ready sets with each program's own standard shortcuts (Russian versions use the same keys).</summary>
    public static List<AppWheel> Presets() => new()
    {
        new() { Name = "Word", Processes = { "winword" }, Items =
        {
            K("Жирный", "ctrl+b", ""), K("Курсив", "ctrl+i", ""), K("Подчеркнуть", "ctrl+u", ""),
            K("Заголовок 1", "ctrl+alt+1", ""), K("Заголовок 2", "ctrl+alt+2", ""), K("Обычный текст", "ctrl+shift+n", ""),
            K("По центру", "ctrl+e", ""), K("По ширине", "ctrl+j", ""), K("Убрать оформление", "ctrl+space", ""),
            K("Разрыв страницы", "ctrl+enter", ""), K("Найти и заменить", "ctrl+h", ""),
        } },
        new() { Name = "Excel", Processes = { "excel" }, Items =
        {
            K("Автосумма", "alt+=", ""), K("Формат ячеек", "ctrl+1", ""), K("Фильтр", "ctrl+shift+l", ""),
            K("Как таблица", "ctrl+t", ""), K("Сегодня", "ctrl+;", ""), K("Сейчас (время)", "ctrl+shift+;", ""),
            K("Процент", "ctrl+shift+5", ""), K("Денежный", "ctrl+shift+4", ""), K("Вставить значения", "ctrl+alt+v", ""),
            K("Закрепить области", "alt w f f", ""), K("Пересчитать", "f9", ""),
        } },
        new() { Name = "Photoshop", Processes = { "photoshop" }, Items =
        {
            K("Новый слой", "ctrl+shift+n", ""), K("Дублировать слой", "ctrl+j", ""), K("Трансформирование", "ctrl+t", ""),
            K("Кисть меньше", "[", ""), K("Кисть больше", "]", ""), K("Уровни", "ctrl+l", ""), K("Кривые", "ctrl+m", ""),
            K("Обесцветить", "ctrl+shift+u", ""), K("Размер изображения", "ctrl+alt+i", ""), K("Снять выделение", "ctrl+d", ""),
            K("Экспортировать как", "ctrl+alt+shift+w", ""),
        } },
        new() { Name = "PowerPoint", Processes = { "powerpnt" }, Items =
        {
            K("Новый слайд", "ctrl+m", ""), K("Дублировать", "ctrl+d", ""), K("Показ с начала", "f5", ""),
            K("Показ с текущего", "shift+f5", ""), K("Группировать", "ctrl+g", ""), K("Разгруппировать", "ctrl+shift+g", ""),
            K("По центру", "ctrl+e", ""), K("Жирный", "ctrl+b", ""),
        } },
    };

    /// <summary>The set for a process name, if one is on.</summary>
    public static AppWheel? For(string process) =>
        string.IsNullOrEmpty(process) ? null
        : Settings.Current.AppWheels.FirstOrDefault(w => w.Enabled && w.Items.Count > 0 && w.Processes.Any(p => p.Equals(process, StringComparison.OrdinalIgnoreCase)));

    /// <summary>"ctrl+b" or "alt w f f": each space-separated step pressed in turn. False if a step isn't a key we know.</summary>
    public static bool Valid(string keys) => Steps(keys).Count > 0 && Steps(keys).All(InputSim.IsKey);

    /// <summary>The recorder's "Ctrl+Shift+N" / "Ctrl+OemPlus" in the wheel's own spelling: "ctrl+shift+n" / "ctrl+=".</summary>
    public static string FromHotkey(string combo) => string.Join("+", combo.Split('+').Select(p => p switch
    {
        "OemPlus" => "=", "OemMinus" => "-", "OemComma" => ",", "OemPeriod" => ".", "Oem1" or "OemSemicolon" => ";", "Oem2" or "OemQuestion" => "/",
        "Oem3" or "OemTilde" => "`", "Oem4" or "OemOpenBrackets" => "[", "Oem6" or "OemCloseBrackets" => "]", "Oem7" or "OemQuotes" => "'", "Oem5" or "OemPipe" => "\\",
        "Return" => "enter", "Escape" => "esc", "Back" => "backspace", "Next" => "pagedown", "Prior" => "pageup",
        _ when p.Length == 2 && p[0] == 'D' && char.IsDigit(p[1]) => p[1].ToString(),
        _ => p.ToLowerInvariant(),
    }));

    internal static List<string> Steps(string keys) => keys.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    /// <summary>Back to the program, then its keys. The wheel had the focus; the program needs a moment to get it back.</summary>
    public static void Press(IntPtr window, string keys)
    {
        if (window != IntPtr.Zero) Native.SetForegroundWindow(window);
        _ = Task.Run(async () =>
        {
            await Task.Delay(120);
            foreach (var step in Steps(keys)) { InputSim.Key(step); await Task.Delay(70); }
        });
    }
}
