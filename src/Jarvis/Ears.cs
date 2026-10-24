using System.IO;
using System.Text.Json;
using NAudio.Wave;
using Vosk;
using Whisper.net;

namespace MishkaHelper.Core;

public enum EarState { Off, Idle, Listening, Thinking }

/// <summary>
/// Microphone → energy-based voice activity detection cuts whole phrases → Whisper (GPU, accurate) or Vosk (light)
/// transcribes each phrase → wake word / listening-mode logic. Everything runs locally.
/// </summary>
public static class Ears
{
    static WaveInEvent? _in;
    static Model? _vosk;
    static WhisperFactory? _whisper;
    static string? _whisperKey;
    static readonly SemaphoreSlim Recognize = new(1, 1);

    public static EarState State { get; private set; } = EarState.Off;
    public static event Action<EarState>? StateChanged;
    public static event Action<string>? Partial;
    public static event Action<string>? Heard;

    /// <summary>Set while Jarvis talks so it doesn't hear itself.</summary>
    public static volatile bool Muted;

    static bool _commandNext;
    static DateTime _listenUntil;

    static void Set(EarState s) { if (State == s) return; State = s; StateChanged?.Invoke(s); }

    public static List<string> Microphones() =>
        Enumerable.Range(0, WaveInEvent.DeviceCount).Select(i => WaveInEvent.GetCapabilities(i).ProductName).ToList();

    static bool UseWhisper => Settings.Current.Jarvis.Engine == "whisper" && Models.Has(Models.WhisperModel);

    public static async Task Start()
    {
        Stop();
        if (!UseWhisper && !Models.Has(Models.Vosk))
            throw new InvalidOperationException("Скачай модель распознавания (Whisper или Vosk) на странице «Джарвис»");
        await Task.Run(() =>
        {
            if (UseWhisper)
            {
                var m = Models.WhisperModel;
                if (_whisperKey != m.Key)
                {
                    _whisper?.Dispose(); _whisper = WhisperFactory.FromPath(Models.PathOf(m)); _whisperKey = m.Key;
                    // First GPU inference compiles shaders (~15 s); do it now on silence, not on the user's first phrase.
                    _ = WhisperText(new byte[Rate * 2]);
                }
            }
            else
            {
                Vosk.Vosk.SetLogLevel(-1);
                _vosk ??= new Model(Models.PathOf(Models.Vosk));
            }
        });

        var mic = Settings.Current.Jarvis.Microphone;
        int dev = Microphones().FindIndex(m => m == mic);
        _in = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 50, DeviceNumber = dev };
        _in.DataAvailable += OnData;
        _in.StartRecording();
        Set(EarState.Idle);
    }

    public static void Stop()
    {
        try { _in?.StopRecording(); } catch { }
        _in?.Dispose(); _in = null;
        Set(EarState.Off);
    }

    public static void ResetWhisper() { _whisper?.Dispose(); _whisper = null; _whisperKey = null; }

    /// <summary>Push-to-talk / orb click: the next phrase is a command, no wake word needed.</summary>
    public static void Listen(int seconds = 8)
    {
        if (State == EarState.Off) return;
        _commandNext = true;
        _listenUntil = DateTime.Now.AddSeconds(seconds);
        Set(EarState.Listening);
    }

    public static void Thinking(bool on) { if (State != EarState.Off) Set(on ? EarState.Thinking : _commandNext ? EarState.Listening : EarState.Idle); }

    // ---------- voice activity detection ----------
    const int Rate = 16000;
    static readonly MemoryStream Speech = new();
    static readonly Queue<byte[]> PreRoll = new();   // ~300 ms before speech starts
    static bool _inSpeech;
    static int _loudFrames, _quietMs, _speechMs;
    static double _floor = 300;

    static void OnData(object? s, WaveInEventArgs e)
    {
        if (Muted) { ResetVad(); return; }
        if (_commandNext && !_inSpeech && DateTime.Now > _listenUntil) { _commandNext = false; Set(EarState.Idle); }

        var frame = new byte[e.BytesRecorded];
        Buffer.BlockCopy(e.Buffer, 0, frame, 0, e.BytesRecorded);
        double rms = Rms(frame);
        int ms = e.BytesRecorded * 1000 / (Rate * 2);
        double threshold = Math.Max(_floor * 3.2, 450);

        if (!_inSpeech)
        {
            _floor = _floor * 0.97 + Math.Min(rms, 4000) * 0.03;   // adapt to fan noise etc.
            PreRoll.Enqueue(frame);
            while (PreRoll.Count > 6) PreRoll.Dequeue();
            _loudFrames = rms > threshold ? _loudFrames + 1 : 0;
            if (_loudFrames >= 2)
            {
                _inSpeech = true; _quietMs = 0; _speechMs = 0;
                Speech.SetLength(0);
                foreach (var f in PreRoll) Speech.Write(f);
                PreRoll.Clear();
                if (_commandNext) Partial?.Invoke("Слушаю…");
            }
            return;
        }

        Speech.Write(frame);
        _speechMs += ms;
        _quietMs = rms < threshold * 0.6 ? _quietMs + ms : 0;
        if (_quietMs >= 750 || _speechMs >= 15000)
        {
            var pcm = Speech.ToArray();
            ResetVad();
            if (_speechMs >= 450) _ = Task.Run(() => OnPhrase(pcm));
        }
    }

    static void ResetVad() { _inSpeech = false; _loudFrames = 0; _quietMs = 0; Speech.SetLength(0); PreRoll.Clear(); }

    static double Rms(byte[] pcm)
    {
        double sum = 0; int n = pcm.Length / 2;
        for (int i = 0; i < n; i++) { short v = BitConverter.ToInt16(pcm, i * 2); sum += v * (double)v; }
        return n == 0 ? 0 : Math.Sqrt(sum / n);
    }

    // ---------- recognition ----------
    static async Task OnPhrase(byte[] pcm)
    {
        bool wakeOn = Settings.Current.Jarvis.WakeWordOn;
        if (!_commandNext && !wakeOn) return;
        await Recognize.WaitAsync();
        string text;
        try
        {
            if (_commandNext) Set(EarState.Thinking);
            text = UseWhisper ? await WhisperText(pcm) : VoskText(pcm);
        }
        catch (Exception e) { Log.Error(e); text = ""; }
        finally { Recognize.Release(); }

        if (text.Length == 0 || IsJunk(text)) { if (_commandNext) Set(EarState.Listening); else Set(EarState.Idle); return; }

        // Barge-in: while Jarvis talks, its own voice reaches the mic too — accept only a stop word.
        if (Voice.IsSpeaking)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(text, @"\b(стоп|хватит|замолчи|тихо|stop)\b")) { Voice.Stop(); _commandNext = false; Set(EarState.Idle); }
            return;
        }

        int wake = wakeOn ? WakeIndex(text) : -1;
        if (!_commandNext && wake < 0) { Set(EarState.Idle); return; }

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = string.Join(' ', wake >= 0 ? words.Skip(wake + 1) : words);
        if (cmd.Length < 2)
        {
            // Just "Джарвис" — wait for the command in the next phrase.
            _commandNext = true; _listenUntil = DateTime.Now.AddSeconds(8); Set(EarState.Listening);
            Partial?.Invoke("Слушаю…");
            return;
        }
        _commandNext = false;
        Set(EarState.Idle);
        Heard?.Invoke(cmd);
    }

    static async Task<string> WhisperText(byte[] pcm)
    {
        if (_whisper == null) return "";
        using var wav = new MemoryStream();
        using (var w = new WaveFileWriter(new NonClosing(wav), new WaveFormat(Rate, 16, 1))) w.Write(pcm, 0, pcm.Length);
        wav.Position = 0;
        // The prompt nudges spelling of the wake word and keeps Russian.
        var wake = Settings.Current.Jarvis.WakeWord;
        using var p = _whisper.CreateBuilder().WithLanguage("ru").WithPrompt(Prompt(wake)).Build();
        var sb = new System.Text.StringBuilder();
        await foreach (var seg in p.ProcessAsync(wav)) sb.Append(seg.Text);
        return Clean(sb.ToString());
    }

    /// <summary>
    /// Whisper's prompt biases spelling toward words it has "seen": the wake word, the user's own phrases and program names.
    /// Kept short — a long prompt costs time and can leak into the transcript.
    /// </summary>
    static string Prompt(string wake)
    {
        var words = Settings.Current.Jarvis.Commands.Select(c => c.Phrases.Split(';')[0].Trim())
            .Concat(Settings.Current.Launchers.Select(l => l.Name))
            .Where(w => w.Length > 1).Distinct().Take(12);
        var s = $"{char.ToUpper(wake[0])}{wake[1..]}, включи музыку. Открой браузер, закрой Яндекс. {string.Join(", ", words)}.";
        return s.Length > 300 ? s[..300] : s;
    }

    static string VoskText(byte[] pcm)
    {
        if (_vosk == null) return "";
        using var rec = new VoskRecognizer(_vosk, Rate);
        rec.AcceptWaveform(pcm, pcm.Length);
        try { using var d = JsonDocument.Parse(rec.FinalResult()); return Clean(d.RootElement.GetProperty("text").GetString() ?? ""); }
        catch { return ""; }
    }

    /// <summary>Whisper's classic hallucinations on noise/silence (trained on subtitled video).</summary>
    static bool IsJunk(string t) =>
        t.Contains("продолжение следует") || t.Contains("субтитр") || t.Contains("спасибо за просмотр") || t.Contains("dimatorzok") ||
        t.Contains("подписывайтесь") || t.Contains("редактор") && t.Contains("корректор") || t is "ну" or "а" or "м" or "э" or "угу";

    sealed class NonClosing(Stream inner) : Stream
    {
        public override bool CanRead => inner.CanRead; public override bool CanSeek => inner.CanSeek; public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length; public override long Position { get => inner.Position; set => inner.Position = value; }
        public override void Flush() => inner.Flush();
        public override int Read(byte[] b, int o, int c) => inner.Read(b, o, c);
        public override long Seek(long o, SeekOrigin s) => inner.Seek(o, s);
        public override void SetLength(long v) => inner.SetLength(v);
        public override void Write(byte[] b, int o, int c) => inner.Write(b, o, c);
        protected override void Dispose(bool disposing) { }
    }

    public static string Clean(string s) =>
        new string(s.ToLowerInvariant().Replace('ё', 'е').Select(c => char.IsLetterOrDigit(c) || c == ' ' || c == '%' ? c : ' ').ToArray())
            .Split(' ', StringSplitOptions.RemoveEmptyEntries).Aggregate("", (a, w) => a.Length == 0 ? w : a + " " + w);

    /// <summary>Index of the word that sounds like the wake word (fuzzy: "джервис", "jarvis"…).</summary>
    public static int WakeIndex(string text)
    {
        var wake = Clean(Settings.Current.Jarvis.WakeWord);
        if (wake.Length == 0) return -1;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int tolerance = wake.Length >= 6 ? 2 : wake.Length >= 4 ? 1 : 0;
        for (int i = 0; i < Math.Min(words.Length, 4); i++)   // the wake word comes first
            if (Distance(words[i], wake) <= tolerance || (wake == "джарвис" && words[i] is "jarvis" or "жарвис" or "джарвиз" or "джервис" or "дарвис"))
                return i;
        return -1;
    }

    public static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (int j = 0; j <= b.Length; j++) d[0, j] = j;
        for (int i = 1; i <= a.Length; i++)
            for (int j = 1; j <= b.Length; j++)
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
        return d[a.Length, b.Length];
    }

    /// <summary>Self-test hook: transcribe a 16 kHz mono PCM buffer with the current engine.</summary>
    public static async Task<string> TestTranscribe(byte[] pcm)
    {
        if (UseWhisper && _whisper == null) { _whisper = WhisperFactory.FromPath(Models.PathOf(Models.WhisperModel)); _whisperKey = Models.WhisperModel.Key; }
        if (!UseWhisper && _vosk == null && Models.Has(Models.Vosk)) _vosk = new Model(Models.PathOf(Models.Vosk));
        return UseWhisper ? await WhisperText(pcm) : VoskText(pcm);
    }
}
