using System.Diagnostics;
using System.IO;
using System.Net.Http;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Windows.Media.SpeechSynthesis;

namespace MishkaHelper.Core;

/// <summary>
/// Text-to-speech: Piper neural voices (offline, natural) with an optional "AI butler" effect,
/// or the built-in Windows voices as a fallback. Mutes the ears while talking.
/// </summary>
public static class Voice
{
    public static readonly ModelInfo PiperRuntime = new("piper", "Piper — нейроголос (21 МБ)",
        "https://github.com/rhasspy/piper/releases/download/2023.11.14-2/piper_windows_amd64.zip", 21, "piper", Zip: true);

    public static readonly (string Key, string Name)[] PiperVoices =
    {
        ("dmitri", "Дмитрий — спокойный мужской (как дворецкий)"),
        ("denis", "Денис — мужской"),
        ("ruslan", "Руслан — низкий мужской"),
        ("irina", "Ирина — женский"),
    };

    public static ModelInfo PiperVoice(string key) =>
        new("piper-" + key, key, $"https://huggingface.co/rhasspy/piper-voices/resolve/main/ru/ru_RU/{key}/medium/ru_RU-{key}-medium.onnx", 60, $"ru_RU-{key}-medium.onnx");

    static readonly SpeechSynthesizer Synth = new();
    static WaveOutEvent? _out;

    // Speech queue: each phrase starts synthesizing as soon as it's queued and plays in order (no gaps between sentences).
    static readonly object Q = new();
    static readonly Queue<(int Gen, Task<ISampleProvider> Audio)> Pending = new();
    static Task? _player;
    static int _gen;
    public static bool IsSpeaking { get; private set; }
    /// <summary>Raised when the queue has been fully spoken (or stopped).</summary>
    public static event Action? Finished;
    static readonly HttpClient Http = new();
    public static event Action<bool>? Speaking;

    public static bool PiperReady(string key) =>
        File.Exists(Path.Combine(Models.PathOf(PiperRuntime), "piper.exe")) && Models.Has(PiperVoice(key)) && File.Exists(Models.PathOf(PiperVoice(key)) + ".json");

    /// <summary>Runtime + voice model + its config json.</summary>
    public static async Task DownloadPiper(string key, Action<double>? progress = null)
    {
        if (!File.Exists(Path.Combine(Models.PathOf(PiperRuntime), "piper.exe"))) await Models.Download(PiperRuntime, f => progress?.Invoke(f * 0.25));
        var v = PiperVoice(key);
        if (!Models.Has(v)) await Models.Download(v, f => progress?.Invoke(0.25 + f * 0.75));
        var json = Models.PathOf(v) + ".json";
        if (!File.Exists(json)) await File.WriteAllBytesAsync(json, await Http.GetByteArrayAsync(v.Url + ".json"));
        progress?.Invoke(1);
    }

    public static List<string> WindowsVoices() =>
        SpeechSynthesizer.AllVoices.Where(v => v.Language.StartsWith("ru", StringComparison.OrdinalIgnoreCase)).Select(v => v.DisplayName)
            .Concat(SpeechSynthesizer.AllVoices.Where(v => !v.Language.StartsWith("ru", StringComparison.OrdinalIgnoreCase)).Select(v => v.DisplayName)).ToList();

    /// <summary>Stop talking and drop everything queued.</summary>
    public static void Stop()
    {
        lock (Q) { _gen++; Pending.Clear(); }
        try { _out?.Stop(); } catch { }
    }

    /// <summary>Engine chain: Edge neural (online) → Piper (offline) → Windows voice; then the optional butler effect.</summary>
    static async Task<ISampleProvider> Render(string text, JarvisSettings s)
    {
        ISampleProvider? audio = null;
        if (s.Tts == "edge" || s.Movie)
        {
            bool cyrillic = text.Any(c => c is >= 'а' and <= 'я' or >= 'А' and <= 'Я');
            var voice = !s.Movie ? s.EdgeVoice : cyrillic ? EdgeTts.RussianFallback : s.MovieVoice;
            try { audio = Mp3(await EdgeTts.Synthesize(text, voice, (int)Math.Round((s.Rate - 1) * 100), s.EdgePitch)); }
            catch (Exception e) { Log.Info("edge tts: " + e.Message); }   // offline → fall through
        }
        if (audio == null && (s.Tts is "piper" or "edge") && PiperReady(s.PiperVoice)) audio = await PiperAudio(text, s.PiperVoice, s.Rate);
        audio ??= await WindowsAudio(text, s);
        return s.VoiceFx ? new ButlerFx(audio) : audio;
    }

    // ACM MP3 decoder ships with Windows.
    static ISampleProvider Mp3(byte[] mp3) => new Mp3FileReaderBase(new MemoryStream(mp3), wf => new AcmMp3FrameDecompressor(wf)).ToSampleProvider();

    /// <summary>Self-test / samples: synthesize with the current settings into a WAV file; returns seconds of audio.</summary>
    public static async Task<double> RenderToFile(string text, string path)
    {
        var audio = await Render(text, Settings.Current.Jarvis);
        WaveFileWriter.CreateWaveFile16(path, audio);
        return new FileInfo(path).Length / 2.0 / audio.WaveFormat.SampleRate;
    }

    /// <summary>Say this now, replacing whatever is being said.</summary>
    public static Task Say(string text) { Stop(); return Enqueue(text); }

    /// <summary>Add a phrase after the current ones (used for streamed answers, sentence by sentence).</summary>
    public static Task Enqueue(string text)
    {
        var s = Settings.Current.Jarvis;
        if (!s.Speak || string.IsNullOrWhiteSpace(text)) return Task.CompletedTask;
        lock (Q)
        {
            Pending.Enqueue((_gen, Render(text, s)));
            if (_player == null || _player.IsCompleted) _player = Task.Run(PlayLoop);
            return _player;
        }
    }

    static async Task PlayLoop()
    {
        var s = Settings.Current.Jarvis;
        try
        {
            // With barge-in the ears stay open (only "стоп" is accepted while talking); otherwise mute them.
            Ears.Muted = !s.BargeIn;
            IsSpeaking = true;
            Speaking?.Invoke(true);
            while (true)
            {
                (int Gen, Task<ISampleProvider> Audio) next;
                lock (Q) { if (Pending.Count == 0) break; next = Pending.Dequeue(); }
                ISampleProvider audio;
                try { audio = await next.Audio; } catch (Exception e) { Log.Error(e); continue; }
                if (next.Gen != _gen) continue;   // stopped while synthesizing

                using var output = new WaveOutEvent();
                _out = output;
                var done = new TaskCompletionSource();
                output.PlaybackStopped += (_, _) => done.TrySetResult();
                output.Init(audio);
                output.Play();
                await done.Task;
                _out = null;
            }
        }
        catch (Exception e) { Log.Error(e); }
        finally
        {
            _out = null;
            await Task.Delay(250);   // room echo tail
            IsSpeaking = false;
            Ears.Muted = false;
            Speaking?.Invoke(false);
            Finished?.Invoke();
        }
    }

    /// <summary>Piper writes raw 16-bit mono PCM for the whole sentence; ~0.3 s on a modern CPU.</summary>
    static async Task<ISampleProvider?> PiperAudio(string text, string key, double rate)
    {
        var exe = Path.Combine(Models.PathOf(PiperRuntime), "piper.exe");
        var model = Models.PathOf(PiperVoice(key));
        int sampleRate = 22050;
        try
        {
            using var cfg = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(model + ".json"));
            sampleRate = cfg.RootElement.GetProperty("audio").GetProperty("sample_rate").GetInt32();
        }
        catch { }
        var psi = new ProcessStartInfo(exe, $"--model \"{model}\" --output_raw --length_scale {1 / Math.Clamp(rate, 0.5, 2):0.00} --sentence_silence 0.15".Replace(',', '.'))
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!, StandardInputEncoding = new System.Text.UTF8Encoding(false),
        };
        using var p = Process.Start(psi)!;
        ChildJob.Add(p);
        _ = p.StandardError.ReadToEndAsync();
        await p.StandardInput.WriteLineAsync(text.Replace('\n', ' '));
        p.StandardInput.Close();
        using var ms = new MemoryStream();
        await p.StandardOutput.BaseStream.CopyToAsync(ms);
        await p.WaitForExitAsync();
        if (ms.Length < 1000) return null;
        return new RawSourceWaveStream(new MemoryStream(ms.ToArray()), new WaveFormat(sampleRate, 16, 1)).ToSampleProvider();
    }

    static async Task<ISampleProvider> WindowsAudio(string text, JarvisSettings s)
    {
        var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.DisplayName == s.Voice)
                    ?? SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
                    ?? SpeechSynthesizer.DefaultVoice;
        Synth.Voice = voice;
        Synth.Options.SpeakingRate = Math.Clamp(s.Rate, 0.5, 3);
        using var stream = await Synth.SynthesizeTextToStreamAsync(text);
        var ms = new MemoryStream();
        await stream.AsStreamForRead().CopyToAsync(ms);
        ms.Position = 0;
        return new WaveFileReader(ms).ToSampleProvider();
    }

    /// <summary>
    /// "AI butler" colour, in the spirit of film assistants (not a copy of any real voice):
    /// slightly lower pitch, a faint doubled "digital" layer and a short room reflection.
    /// </summary>
    sealed class ButlerFx : ISampleProvider
    {
        readonly ISampleProvider _src;
        readonly float[] _d1, _d2;   // 11 ms doubling, 70 ms reflection
        int _i1, _i2;
        int _tail;                  // samples of reverb tail left after the source ends

        public ButlerFx(ISampleProvider src)
        {
            var shifted = new SmbPitchShiftingSampleProvider(src) { PitchFactor = 0.93f };
            _src = shifted;
            WaveFormat = shifted.WaveFormat;
            int sr = WaveFormat.SampleRate * WaveFormat.Channels;
            _d1 = new float[Math.Max(1, sr * 11 / 1000)];
            _d2 = new float[Math.Max(1, sr * 70 / 1000)];
            _tail = sr / 4;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            int n = _src.Read(buffer, offset, count);
            if (n == 0 && _tail > 0) { n = Math.Min(count, _tail); Array.Clear(buffer, offset, n); _tail -= n; }
            for (int i = 0; i < n; i++)
            {
                float x = buffer[offset + i];
                float doubled = _d1[_i1];
                float echo = _d2[_i2];
                _d1[_i1] = x;
                _d2[_i2] = x + echo * 0.35f;
                _i1 = (_i1 + 1) % _d1.Length;
                _i2 = (_i2 + 1) % _d2.Length;
                buffer[offset + i] = Math.Clamp(x * 0.85f + doubled * 0.22f + echo * 0.18f, -1f, 1f);
            }
            return n;
        }
    }
}
