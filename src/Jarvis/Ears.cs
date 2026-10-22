using System.IO;
using System.Text.Json;
using NAudio.Wave;
using Vosk;
using Whisper.net;

namespace MishkaHelper.Core;

public enum EarState { Off, Idle, Listening, Thinking }

/// <summary>
/// Microphone → Vosk (always, cheap) for wake word and commands; optionally Whisper re-transcribes
/// the command utterance for accuracy. Everything runs locally.
/// </summary>
public static class Ears
{
    static WaveInEvent? _in;
    static Model? _vosk;
    static VoskRecognizer? _rec;
    static WhisperFactory? _whisper;
    static readonly SemaphoreSlim WhisperLock = new(1, 1);

    static readonly MemoryStream Utterance = new();   // PCM since the last final result
    static bool _commandNext;
    static DateTime _listenUntil;

    public static EarState State { get; private set; } = EarState.Off;
    public static event Action<EarState>? StateChanged;
    public static event Action<string>? Partial;
    public static event Action<string>? Heard;

    /// <summary>Set while Jarvis talks so it doesn't hear itself.</summary>
    public static volatile bool Muted;

    static void Set(EarState s) { if (State == s) return; State = s; StateChanged?.Invoke(s); }

    public static List<string> Microphones() =>
        Enumerable.Range(0, WaveInEvent.DeviceCount).Select(i => WaveInEvent.GetCapabilities(i).ProductName).ToList();

    public static async Task Start()
    {
        Stop();
        if (!Models.Has(Models.Vosk)) throw new InvalidOperationException("Нужна модель Vosk — скачай её на странице «Джарвис»");
        await Task.Run(() =>
        {
            Vosk.Vosk.SetLogLevel(-1);
            _vosk ??= new Model(Models.PathOf(Models.Vosk));
            _rec = new VoskRecognizer(_vosk, 16000f);
            if (Settings.Current.Jarvis.Engine == "whisper" && Models.Has(Models.WhisperModel))
                _whisper ??= WhisperFactory.FromPath(Models.PathOf(Models.WhisperModel));
        });

        var mic = Settings.Current.Jarvis.Microphone;
        int dev = Microphones().FindIndex(m => m == mic);
        _in = new WaveInEvent { WaveFormat = new WaveFormat(16000, 16, 1), BufferMilliseconds = 100, DeviceNumber = dev };
        _in.DataAvailable += OnData;
        _in.StartRecording();
        Set(EarState.Idle);
    }

    public static void Stop()
    {
        try { _in?.StopRecording(); } catch { }
        _in?.Dispose(); _in = null;
        _rec?.Dispose(); _rec = null;
        Set(EarState.Off);
    }

    /// <summary>Drop the cached Whisper model (engine/model changed in settings).</summary>
    public static void ResetWhisper() { _whisper?.Dispose(); _whisper = null; }

    /// <summary>Push-to-talk / orb click: the next phrase is a command, no wake word needed.</summary>
    public static void Listen(int seconds = 7)
    {
        if (State == EarState.Off) return;
        _commandNext = true;
        _listenUntil = DateTime.Now.AddSeconds(seconds);
        Set(EarState.Listening);
    }

    public static void Thinking(bool on) { if (State != EarState.Off) Set(on ? EarState.Thinking : EarState.Idle); }

    static void OnData(object? s, WaveInEventArgs e)
    {
        var rec = _rec;
        if (rec == null) return;
        if (Muted) { if (Utterance.Length > 0) { rec.Reset(); Utterance.SetLength(0); } return; }

        Utterance.Write(e.Buffer, 0, e.BytesRecorded);
        if (Utterance.Length > 16000 * 2 * 20) Utterance.SetLength(0);   // safety cap: 20 s

        bool final;
        try { final = rec.AcceptWaveform(e.Buffer, e.BytesRecorded); } catch { return; }
        if (final)
        {
            var text = Field(rec.Result(), "text");
            var audio = Utterance.ToArray();
            Utterance.SetLength(0);
            if (text.Length > 0) OnFinal(text, audio);
        }
        else
        {
            var partial = Field(rec.PartialResult(), "partial");
            if (partial.Length == 0) { CheckTimeout(); return; }
            if (!_commandNext && Settings.Current.Jarvis.WakeWordOn && WakeIndex(partial) >= 0) { _commandNext = true; _listenUntil = DateTime.Now.AddSeconds(7); Set(EarState.Listening); }
            if (_commandNext) { _listenUntil = DateTime.Now.AddSeconds(4); Partial?.Invoke(partial); }
        }
    }

    static void CheckTimeout()
    {
        if (_commandNext && DateTime.Now > _listenUntil) { _commandNext = false; Set(EarState.Idle); }
    }

    static void OnFinal(string text, byte[] audio)
    {
        int wake = Settings.Current.Jarvis.WakeWordOn ? WakeIndex(text) : -1;
        if (!_commandNext && wake < 0) return;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var cmd = string.Join(' ', wake >= 0 ? words.Skip(wake + 1) : words);
        if (cmd.Length < 2)
        {
            // Just "Джарвис" — wait for the command in the next phrase.
            _commandNext = true; _listenUntil = DateTime.Now.AddSeconds(7); Set(EarState.Listening);
            Partial?.Invoke("Слушаю…");
            return;
        }
        _commandNext = false;

        if (_whisper != null)
        {
            Set(EarState.Thinking);
            _ = Task.Run(async () =>
            {
                var better = await Transcribe(audio);
                if (better.Length > 0)
                {
                    var bw = better.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    int i = WakeIndex(better);
                    var stripped = string.Join(' ', i >= 0 ? bw.Skip(i + 1) : bw);
                    if (stripped.Length >= 2) cmd = stripped;
                }
                Set(EarState.Idle);
                Heard?.Invoke(cmd);
            });
        }
        else
        {
            Set(EarState.Idle);
            Heard?.Invoke(cmd);
        }
    }

    static async Task<string> Transcribe(byte[] pcm)
    {
        if (_whisper == null || pcm.Length < 16000) return "";
        await WhisperLock.WaitAsync();
        try
        {
            using var wav = new MemoryStream();
            using (var w = new WaveFileWriter(new NonClosing(wav), new WaveFormat(16000, 16, 1))) w.Write(pcm, 0, pcm.Length);
            wav.Position = 0;
            using var p = _whisper.CreateBuilder().WithLanguage("ru").Build();
            var sb = new System.Text.StringBuilder();
            await foreach (var seg in p.ProcessAsync(wav)) sb.Append(seg.Text);
            return Clean(sb.ToString());
        }
        catch (Exception e) { Log.Error(e); return ""; }
        finally { WhisperLock.Release(); }
    }

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

    static string Field(string json, string name)
    {
        try { using var d = JsonDocument.Parse(json); return Clean(d.RootElement.GetProperty(name).GetString() ?? ""); }
        catch { return ""; }
    }

    /// <summary>Index of the word that sounds like the wake word (fuzzy: the small model may hear "джервис").</summary>
    public static int WakeIndex(string text)
    {
        var wake = Clean(Settings.Current.Jarvis.WakeWord);
        if (wake.Length == 0) return -1;
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int tolerance = wake.Length >= 6 ? 2 : wake.Length >= 4 ? 1 : 0;
        for (int i = 0; i < words.Length; i++)
            if (Distance(words[i], wake) <= tolerance || (wake == "джарвис" && words[i] is "jarvis" or "жарвис" or "джарвиз"))
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
}
