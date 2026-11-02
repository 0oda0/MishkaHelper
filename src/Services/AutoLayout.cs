using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Automation;

namespace MishkaHelper.Core;

/// <summary>
/// Fixes a word typed in the wrong layout as soon as the space after it is pressed. Off by default, and it stands aside while Caramba Switcher runs.
/// This is a keyboard hook in an elevated app, so it stays narrow: only the current word, in memory, gone at the next space;
/// nothing is written anywhere; password fields, terminals and code editors are left alone. When unsure it does nothing.
/// </summary>
public static class AutoLayout
{
    const string RuText_ = "сегодня завтра расписание привет спасибо пожалуйста хорошо сделать работа человек время когда только может очень после нужно надо потом здесь больше почему потому который которая которые этого этот эта если чтобы тоже уже ещё было были будет будут есть нет так как что где куда кто вот весь всех всегда никогда сейчас теперь думаю знаю хочу могу буду пойду делать дела отчёт вопрос ответ письмо почта компьютер телефон программа файл папка запись заметка встреча занятие лекция семинар экзамен зачёт учёба университет группа преподаватель студент деньги оплата покупка магазин дорога машина поезд билет дом квартира комната семья мама папа брат сестра друг подруга ребёнок жизнь любовь счастье здоровье утро день вечер ночь неделя месяц год понедельник вторник среда четверг пятница суббота воскресенье январь февраль март апрель май июнь июль август сентябрь октябрь ноябрь декабрь большой маленький новый старый хороший плохой красивый важный главный первый второй третий последний интересно понятно конечно наверное возможно например сначала сразу вместе отдельно быстро медленно тихо громко война мир город страна язык русский английский книга фильм музыка игра объект съезд подъезд жёлтый чёрный ещё шёл нашёл пришёл щука ящик чашка площадь общий вещь вещи счёт считать щётка мышь тишина жить шить цирк цель цифра конец отец перец птица лицо яйцо юбка юмор люди люблю союз краю пою бюро эхо этаж поэт дуэль поэтому фото хотя гость ключ врач речь ночью дочь печь ездить ехать ждать жду надежда одежда между дождь";
    const string EnText_ = "today tomorrow schedule hello thanks please good make work people time when only maybe very after need must then here more why because which that this these those there their they them what where who whom whose while with without would could should shall will have has had having been being were was are the and but for not you your yours our out over under about above below between through during before again further once both each few most other some such than too can just now know think want going come came take took give gave find found tell told ask asked use used using file folder note meeting lesson lecture exam study university group teacher student money payment shop road car train ticket house room family mother father brother sister friend child life love happy health morning day evening night week month year monday tuesday wednesday thursday friday saturday sunday january february march april may june july august september october november december big small new old great bad nice important main first second third last interesting sure probably example quickly slowly quiet loud war peace city country language english russian book film movie music game windows microsoft google github python docker server client update download install setting system computer phone program software hardware keyboard mouse screen window button browser internet network message email telegram discord youtube video audio photo picture image project code script function class json html https http www login password user admin mishka zoom quick jump fox lazy dog box wax exit next text extra queue quit equal squad gym rhythm sky try fly why shy dry year yes you yellow knew know knife write wrong wrap";
    static readonly HashSet<string> RuPairs = Pairs(RuText_), EnPairs = Pairs(EnText_);

    static HashSet<string> Pairs(string text)
    {
        var set = new HashSet<string>();
        foreach (var w in text.Split(' '))
            for (int i = 0; i + 1 < w.Length; i++) set.Add(w.Substring(i, 2));
        return set;
    }

    /// <summary>Share of the word's letter pairs that occur in this language at all.</summary>
    internal static double Score(string word, bool russian)
    {
        var w = word.ToLowerInvariant().Replace('ё', 'е');
        if (w.Length < 2) return 0;
        var set = russian ? RuPairs : EnPairs;
        int ok = 0;
        for (int i = 0; i + 1 < w.Length; i++) if (set.Contains(w.Substring(i, 2)) || (russian && set.Contains(w.Substring(i, 2).Replace('е', 'ё')))) ok++;
        return ok / (double)(w.Length - 1);
    }

    // The commonest short words: too few letters for patterns to tell. Only those whose wrong-layout form is not a word itself.
    static readonly HashSet<string> ShortRu = new("как что это кто где так там тут нет все всё для мне меня тебя она они оно его еще уже вот при про над под без или дела есть будет была было".Split(' '));
    static readonly HashSet<string> ShortEn = new("the and for you are not with this that have from was but all can what your".Split(' '));

    const string Vowels = "aeiouyаеёиоуыэюя";
    static bool IsVowel(char c) => Vowels.Contains(char.ToLowerInvariant(c));
    static readonly HashSet<string> NoVowelEn = new("html css sql http https npm pdf ssh tcp udp vpn ftp xml svg png jpg mp3 mp4 cmd dns gps wtf lol pls thx bff brb hmm shh psst tsk nth".Split(' '), StringComparer.OrdinalIgnoreCase);

    /// <summary>Letters that can't follow each other in the language this word claims to be in. The owner's idea: look at how it's written, not at a dictionary.</summary>
    internal static bool Impossible(string word, bool russian)
    {
        var w = word.ToLowerInvariant();
        if (!russian && (NoVowelEn.Contains(w) || w is "qwerty" or "asdf")) return false;
        int run = 0, vowels = 0;
        foreach (var c in w)
        {
            if (!char.IsLetter(c)) { run = 0; continue; }
            if (IsVowel(c)) { vowels++; run = 0; }
            else if (++run >= 5) return true;
        }
        if (vowels == 0 && w.Length >= 3) return russian || !NoVowelEn.Contains(w);
        if (russian)
        {
            if (w[0] is 'ь' or 'ъ' or 'ы') return true;
            for (int i = 0; i + 1 < w.Length; i++)
            {
                char x = w[i], y = w[i + 1];
                if (y is 'ь' or 'ъ' && (IsVowel(x) || x is 'ь' or 'ъ' or 'й')) return true;
                if (x == 'ъ' && !(y is 'е' or 'ё' or 'ю' or 'я')) return true;
                if (y == 'ы' && (IsVowel(x) || x is 'ь' or 'ъ' or 'й' or 'ж' or 'ш' or 'ч' or 'щ')) return true;
                if (x is 'й' && y is 'й' or 'ь') return true;
            }
            return false;
        }
        return System.Text.RegularExpressions.Regex.IsMatch(w, "[^aeiouy]q|q[^u]|[bcdfgjkmpvxz][bcdfgjkmpvxz]{2}[bcdfgjkmpvxz]|^[bcdfgkmptvz][bcdfgkmptvz]");
    }

    /// <summary>The fixed word, or null. Words with digits and acronyms stay as typed; a clear pattern needs less proof than a mere low score.</summary>
    public static string? Decide(string typed)
    {
        if (typed.Length < 2 || typed.Length > 40 || typed.Any(char.IsDigit)) return null;
        var letters = typed.Where(char.IsLetter).ToList();
        if (letters.Count < 2 || (letters.Count >= 2 && letters.All(char.IsUpper))) return null;
        bool nowRussian = letters.Count(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я' or 'ё' or 'Ё') > letters.Count / 2;
        if (!nowRussian && !letters.All(char.IsAsciiLetter)) return null;
        var other = LayoutFix.Convert(typed);
        if (other == null || other == typed) return null;
        var otherTrim = other.TrimEnd(',', '.', ';', ':', '!', '?');
        if (!otherTrim.All(c => char.IsLetter(c) || c == '-')) return null;
        var lowOther = otherTrim.ToLowerInvariant();
        // Short words: only the known ones.
        if ((nowRussian ? ShortEn : ShortRu).Contains(lowOther)) return other;
        if (otherTrim.Length < 4) return null;
        var clean = new string(letters.ToArray());
        double now = Score(clean, nowRussian), then = Score(otherTrim, !nowRussian);
        if (Impossible(otherTrim, !nowRussian)) return null;   // it wouldn't be a word over there either
        // A key that gives punctuation inside a word, where the other layout has a letter: "hf,jnf" → "работа".
        bool innerKey = !nowRussian && System.Text.RegularExpressions.Regex.IsMatch(typed.TrimEnd(',', '.', ';', ':'), @"[,;\[\]]");
        if ((Impossible(clean, nowRussian) || innerKey) && then >= 0.55) return other;
        return now <= 0.7 && then >= 0.75 && then - now >= 0.3 ? other : null;
    }

    // ---------- the hook ----------
    delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
    [StructLayout(LayoutKind.Sequential)] struct KBD { public uint Vk, Scan, Flags, Time; public IntPtr Extra; }
    [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc f, IntPtr mod, uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string? name);

    static IntPtr _kb, _mouse;
    static readonly HookProc KbProc = OnKey, MouseProc = OnMouse;   // kept in fields: the garbage collector must not take what Windows calls back
    static readonly System.Text.StringBuilder Word = new();
    static IntPtr _window;
    static long _lastKey;
    static volatile bool _fixing;
    static long _fixingSince;
    static readonly bool TestMode = Environment.GetEnvironmentVariable("MISHKA_AUTOLAYOUT_TEST") == "1";

    // Where a wrong guess costs real work, or text isn't prose.
    static readonly HashSet<string> Excluded = new(StringComparer.OrdinalIgnoreCase)
    {
        "cmd", "powershell", "pwsh", "WindowsTerminal", "conhost", "wsl", "putty", "mintty", "Code", "devenv", "idea64", "pycharm64", "rider64", "webstorm64", "clion64", "sublime_text", "notepad++",
        "KeePass", "KeePassXC", "1Password", "Bitwarden", "LastPass", "mstsc", "AnyDesk", "rustdesk", "MishkaHelper",
    };

    public static bool CarambaRunning() => Process.GetProcessesByName("CarambaSwitcher").Length > 0;
    static volatile bool _running;
    public static bool Running => _running;

    [StructLayout(LayoutKind.Sequential)] struct MSG { public IntPtr Hwnd; public uint Message; public IntPtr W, L; public uint Time; public int X, Y; }
    [DllImport("user32.dll")] static extern int GetMessage(out MSG m, IntPtr h, uint min, uint max);
    [DllImport("user32.dll")] static extern bool PostThreadMessage(uint thread, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    static uint _thread;

    /// <summary>
    /// Starts or stops by the setting. Called at start, when the switch changes, and every minute in case Caramba appears.
    /// The hooks live on their own thread: Windows waits for a hook on every key in the system, so it must never queue behind the UI
    /// (typing would lag), and our own injected keys must not wait for the very thread that is sending them (they got lost and reordered).
    /// </summary>
    public static void Apply()
    {
        bool want = Settings.Current.AutoLayout && (TestMode || !CarambaRunning());
        if (want == Running) return;
        if (!want) { PostThreadMessage(_thread, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero); _running = false; return; }
        _running = true;
        new Thread(() =>
        {
            _thread = GetCurrentThreadId();
            var mod = GetModuleHandle(null);
            _kb = SetWindowsHookEx(13, KbProc, mod, 0);
            _mouse = SetWindowsHookEx(14, MouseProc, mod, 0);
            while (GetMessage(out _, IntPtr.Zero, 0, 0) > 0) { }
            UnhookWindowsHookEx(_kb);
            UnhookWindowsHookEx(_mouse);
            _kb = _mouse = IntPtr.Zero;
            Word.Clear();
        }) { IsBackground = true, Name = "AutoLayout hook", Priority = ThreadPriority.AboveNormal }.Start();
    }

    static IntPtr OnMouse(int code, IntPtr w, IntPtr l)
    {
        const int WM_LBUTTONDOWN = 0x201, WM_RBUTTONDOWN = 0x204;
        if (code >= 0 && (int)w is WM_LBUTTONDOWN or WM_RBUTTONDOWN) Word.Clear();   // the caret moved: what's buffered isn't next to it any more
        return CallNextHookEx(_mouse, code, w, l);
    }

    static IntPtr OnKey(int code, IntPtr w, IntPtr l)
    {
        const int WM_KEYDOWN = 0x100, WM_SYSKEYDOWN = 0x104;
        if (code < 0) return CallNextHookEx(_kb, code, w, l);
        var k = Marshal.PtrToStructure<KBD>(l);
        // Ours are marked; only the test mode counts other programs' injected keys as typing.
        bool typing = k.Extra != InputSim.Mark && ((k.Flags & 0x10) == 0 || TestMode);
        // While a word is being replaced real keys wait, or they would land in the middle of it. Replayed right after (see Fix).
        // The keyboard must never hang on us: if the UI thread is stuck, give the keys back after a moment.
        if (_fixing && Environment.TickCount64 - _fixingSince > 1500) { lock (Held) Held.Clear(); _fixing = false; Word.Clear(); }
        if (_fixing && typing && !Down(0x11) && !Down(0x12))
        {
            if ((int)w == WM_KEYDOWN) lock (Held) Held.Add((k.Vk, Down(0x10)));
            return (IntPtr)1;
        }
        if (!_fixing && typing && (int)w is WM_KEYDOWN or WM_SYSKEYDOWN) Key(k.Vk);
        return CallNextHookEx(_kb, code, w, l);
    }

    static readonly List<(uint Vk, bool Shift)> Held = new();

    const string OemEn = ";=,-./`[\\]'", OemEnShift = ":+<_>?~{|}\"";
    static readonly int[] OemVk = { 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, 0xDB, 0xDC, 0xDD, 0xDE };

    /// <summary>The Latin character on this physical key, whatever the layout. '\0' if it isn't a typing key.</summary>
    internal static char KeyChar(uint vk, bool shift)
    {
        if (vk is >= 0x41 and <= 0x5A) return (char)(shift ? vk : vk + 32);
        int i = Array.IndexOf(OemVk, (int)vk);
        return i >= 0 ? (shift ? OemEnShift : OemEn)[i] : '\0';
    }

    static bool Down(int vk) => GetAsyncKeyState(vk) < 0;

    static bool RussianOn(IntPtr window) => (ushort)GetKeyboardLayout(GetWindowThreadProcessId(window, out _)).ToInt64() == 0x0419;

    static string Shown(string latin, bool russian) => russian ? ToRussian(latin) : latin;

    /// <summary>A word needing a fix in this window, or null. Runs in the hook, so it stays quick: the rest is checked before anything is changed.</summary>
    static string? Wanted(string latin, IntPtr window, bool russian)
    {
        if (latin.Length < 2) return null;
        return Decide(Shown(latin, russian));
    }

    static void Key(uint vk)
    {
        const uint Space = 0x20, Back = 0x08, Shift1 = 0xA0, Shift2 = 0xA1, Shift3 = 0x10;
        var now = Environment.TickCount64;
        var fg = GetForegroundWindow();
        if (fg != _window || now - _lastKey > 15000) Word.Clear();
        _window = fg; _lastKey = now;
        if (vk is Shift1 or Shift2 or Shift3) return;
        if (Down(0x11) || Down(0x12) || Down(0x5B) || Down(0x5C)) { Word.Clear(); return; }   // shortcuts aren't typing
        if (vk == Back) { if (Word.Length > 0) Word.Length--; return; }
        if (vk == Space)
        {
            var latin = Word.ToString();
            Word.Clear();
            bool russian = RussianOn(fg);
            if (Wanted(latin, fg, russian) is not { } done) return;
            // From this moment every further key waits in Held: the decision and the order of keys can't be overtaken by anything.
            _fixingSince = Environment.TickCount64;
            _fixing = true;
            System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => Run(Shown(latin, russian), done, fg, russian));
            return;
        }
        var c = KeyChar(vk, Down(0x10) ^ ((GetAsyncKeyState(0x14) & 1) != 0));
        if (c == '\0' || Word.Length > 40) Word.Clear();
        else Word.Append(c);
    }

    static async Task<bool> MayTouch(IntPtr window)
    {
        if (GetForegroundWindow() != window || Focus.On || Focus.FullScreenNow()) return false;
        GetWindowThreadProcessId(window, out var pid);
        using (var p = Process.GetProcessById((int)pid)) if (Excluded.Contains(p.ProcessName)) return false;
        return !await IsPasswordField();
    }

    static async Task TypeSlowly(string text) { foreach (var ch in text) { InputSim.Type(ch.ToString()); await Task.Delay(8); } }

    /// <summary>Replaces the word, then plays the held keys in order through the same rules. Keys are released only when the queue is empty.</summary>
    static async void Run(string typed, string done, IntPtr window, bool russian)
    {
        try
        {
            await Task.Delay(30);   // let the program take the space itself
            bool allowed = await MayTouch(window);
            if (allowed) russian = await Replace(typed, done, russian);
            while (true)
            {
                (uint Vk, bool Shift) key;
                lock (Held)
                {
                    if (Held.Count == 0) { _fixing = false; return; }
                    key = Held[0]; Held.RemoveAt(0);
                }
                _fixingSince = Environment.TickCount64;
                if (key.Vk == 0x08) { if (Word.Length > 0) Word.Length--; InputSim.Key("backspace"); continue; }
                if (key.Vk == 0x20)
                {
                    var latin = Word.ToString();
                    Word.Clear();
                    InputSim.Type(" ");
                    await Task.Delay(8);
                    if (allowed && Wanted(latin, window, russian) is { } next) russian = await Replace(Shown(latin, russian), next, russian);
                    continue;
                }
                var c = KeyChar(key.Vk, key.Shift);
                if (c == '\0') { Word.Clear(); continue; }
                Word.Append(c);
                InputSim.Type(Shown(c.ToString(), russian));
                await Task.Delay(8);
            }
        }
        catch (Exception e)
        {
            Log.Error(e);
            lock (Held) Held.Clear();
            _fixing = false;
        }
    }

    // Returns which layout is on afterwards. Keeps its own account: a language change takes effect later than the keys already sent.
    static async Task<bool> Replace(string typed, string done, bool russian)
    {
        _fixingSince = Environment.TickCount64;
        InputSim.Replace(typed.Length + 1, "");
        await Task.Delay(40);
        await TypeSlowly(done + " ");
        await Task.Delay(120);
        bool ends = LayoutFix.EndsRussian(done);
        LayoutFix.Switch(ends);
        await Task.Delay(60);
        Log.Info("autolayout: fixed a word");   // never the word itself
        return ends;
    }

    static string ToRussian(string latin) => LayoutFix.Convert(latin + "q")?[..latin.Length] ?? latin;

    // UI Automation can stall on a hung window: no answer in time counts as "don't touch".
    static async Task<bool> IsPasswordField()
    {
        var check = Task.Run(() => { try { return AutomationElement.FocusedElement?.Current.IsPassword != false; } catch { return true; } });
        return await Task.WhenAny(check, Task.Delay(400)) != check || check.Result;
    }
}
