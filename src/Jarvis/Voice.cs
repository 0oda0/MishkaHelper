using System.IO;
using NAudio.Wave;
using Windows.Media.SpeechSynthesis;

namespace MishkaHelper.Core;

/// <summary>Text-to-speech with the Windows voices (Irina/Pavel for Russian). Mutes the ears while talking.</summary>
public static class Voice
{
    static readonly SpeechSynthesizer Synth = new();
    static WaveOutEvent? _out;
    static readonly SemaphoreSlim One = new(1, 1);
    public static event Action<bool>? Speaking;

    public static List<string> Voices() =>
        SpeechSynthesizer.AllVoices.Where(v => v.Language.StartsWith("ru", StringComparison.OrdinalIgnoreCase)).Select(v => v.DisplayName)
            .Concat(SpeechSynthesizer.AllVoices.Where(v => !v.Language.StartsWith("ru", StringComparison.OrdinalIgnoreCase)).Select(v => v.DisplayName)).ToList();

    public static void Stop()
    {
        try { _out?.Stop(); } catch { }
    }

    public static async Task Say(string text)
    {
        var s = Settings.Current.Jarvis;
        if (!s.Speak || string.IsNullOrWhiteSpace(text)) return;
        Stop();
        await One.WaitAsync();
        try
        {
            var voice = SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.DisplayName == s.Voice)
                        ?? SpeechSynthesizer.AllVoices.FirstOrDefault(v => v.Language.StartsWith("ru", StringComparison.OrdinalIgnoreCase))
                        ?? SpeechSynthesizer.DefaultVoice;
            Synth.Voice = voice;
            Synth.Options.SpeakingRate = Math.Clamp(s.Rate, 0.5, 3);
            using var stream = await Synth.SynthesizeTextToStreamAsync(text);
            using var ms = new MemoryStream();
            await stream.AsStreamForRead().CopyToAsync(ms);
            ms.Position = 0;

            Ears.Muted = true;
            Speaking?.Invoke(true);
            using var reader = new WaveFileReader(ms);
            using var output = new WaveOutEvent();
            _out = output;
            var done = new TaskCompletionSource();
            output.PlaybackStopped += (_, _) => done.TrySetResult();
            output.Init(reader);
            output.Play();
            await done.Task;
        }
        catch (Exception e) { Log.Error(e); }
        finally
        {
            _out = null;
            await Task.Delay(250);   // room echo tail
            Ears.Muted = false;
            Speaking?.Invoke(false);
            One.Release();
        }
    }
}
