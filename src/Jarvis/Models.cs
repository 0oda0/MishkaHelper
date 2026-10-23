using System.IO;
using System.IO.Compression;
using System.Net.Http;

namespace MishkaHelper.Core;

public record ModelInfo(string Key, string Name, string Url, long ApproxMb, string File, bool Zip = false);

/// <summary>Downloads speech/LLM models on demand into %LocalAppData%\MishkaHelper\models. No keys, no accounts.</summary>
public static class Models
{
    public static readonly string Dir = Path.Combine(AppPaths.Shared, "models");

    public static readonly ModelInfo Vosk = new("vosk", "Vosk RU (команды, 44 МБ)", "https://alphacephei.com/vosk/models/vosk-model-small-ru-0.22.zip", 44, "vosk-model-small-ru-0.22", Zip: true);

    public static readonly ModelInfo[] Whisper =
    {
        new("turbo", "Whisper large-v3-turbo — лучшее качество (547 МБ, нужна видеокарта)", "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-large-v3-turbo-q5_0.bin", 547, "ggml-large-v3-turbo-q5_0.bin"),
        new("base", "Whisper base (141 МБ, быстро)", "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-base.bin", 141, "ggml-base.bin"),
        new("small", "Whisper small (465 МБ, точнее)", "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/ggml-small.bin", 465, "ggml-small.bin"),
    };

    public static readonly ModelInfo[] Llm =
    {
        new("qwen3-4b-2507", "Qwen3 4B Instruct 2507 — рекомендую (2,4 ГБ, быстро и умно)", "https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF/resolve/main/Qwen3-4B-Instruct-2507-Q4_K_M.gguf", 2382, "Qwen3-4B-Instruct-2507-Q4_K_M.gguf"),
        new("qwen3-8b", "Qwen3 8B — умнее, но нужна видеокарта от 8 ГБ (4,8 ГБ)", "https://huggingface.co/Qwen/Qwen3-8B-GGUF/resolve/main/Qwen3-8B-Q4_K_M.gguf", 4795, "Qwen3-8B-Q4_K_M.gguf"),
        new("qwen3-4b", "Qwen3 4B — баланс (2,4 ГБ, нужно ~5 ГБ RAM)", "https://huggingface.co/Qwen/Qwen3-4B-GGUF/resolve/main/Qwen3-4B-Q4_K_M.gguf", 2382, "Qwen3-4B-Q4_K_M.gguf"),
        new("qwen3-1.7b", "Qwen3 1.7B — для слабых ПК (1,7 ГБ)", "https://huggingface.co/Qwen/Qwen3-1.7B-GGUF/resolve/main/Qwen3-1.7B-Q8_0.gguf", 1749, "Qwen3-1.7B-Q8_0.gguf"),
    };

    public static string PathOf(ModelInfo m) => Path.Combine(Dir, m.File);
    public static bool Has(ModelInfo m) => m.Zip ? Directory.Exists(PathOf(m)) : File.Exists(PathOf(m));

    public static ModelInfo WhisperModel => Whisper.FirstOrDefault(w => w.Key == Settings.Current.Jarvis.WhisperModel) ?? Whisper[0];
    public static ModelInfo LlmModel => Llm.FirstOrDefault(w => w.Key == Settings.Current.Jarvis.AiModel) ?? Llm[0];

    static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    static readonly HashSet<string> Busy = new();

    /// <summary>Download (resumable via .part) and unpack if needed. progress: 0..1.</summary>
    public static async Task Download(ModelInfo m, Action<double>? progress = null, CancellationToken ct = default)
    {
        lock (Busy) { if (!Busy.Add(m.Key)) throw new InvalidOperationException("Уже скачивается"); }
        try
        {
            Directory.CreateDirectory(Dir);
            var part = PathOf(m) + (m.Zip ? ".zip" : "") + ".part";
            long have = File.Exists(part) ? new FileInfo(part).Length : 0;
            using var req = new HttpRequestMessage(HttpMethod.Get, m.Url);
            if (have > 0) req.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(have, null);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            if (resp.StatusCode != System.Net.HttpStatusCode.PartialContent) have = 0;   // server ignored Range
            long total = have + (resp.Content.Headers.ContentLength ?? m.ApproxMb << 20);
            await using (var src = await resp.Content.ReadAsStreamAsync(ct))
            await using (var dst = new FileStream(part, have > 0 ? FileMode.Append : FileMode.Create))
            {
                var buf = new byte[1 << 16];
                long done = have, lastReport = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct);
                    done += n;
                    if (done - lastReport > (1 << 20)) { lastReport = done; progress?.Invoke((double)done / total); }
                }
            }
            if (m.Zip)
            {
                // Vosk zips have a top-level folder named like the model; llama.cpp zips are flat.
                using (var z = ZipFile.OpenRead(part))
                {
                    bool nested = z.Entries.All(e => e.FullName.StartsWith(m.File + "/", StringComparison.OrdinalIgnoreCase));
                    z.ExtractToDirectory(nested ? Dir : PathOf(m), true);
                }
                File.Delete(part);
            }
            else File.Move(part, PathOf(m), true);
            progress?.Invoke(1);
        }
        finally { lock (Busy) Busy.Remove(m.Key); }
    }

    /// <summary>Largest dedicated video memory in GB, from the display adapters' driver keys (WMI caps AdapterRAM at 4 GB).</summary>
    public static double VramGb()
    {
        long best = 0;
        try
        {
            using var cls = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            foreach (var n in cls?.GetSubKeyNames() ?? [])
            {
                try
                {
                    using var k = cls!.OpenSubKey(n);
                    var v = k?.GetValue("HardwareInformation.qwMemorySize") ?? k?.GetValue("HardwareInformation.MemorySize");
                    long b = v switch { long l => l, int i => (uint)i, byte[] a when a.Length >= 8 => BitConverter.ToInt64(a), byte[] a when a.Length >= 4 => BitConverter.ToUInt32(a), _ => 0 };
                    best = Math.Max(best, b);
                }
                catch { }
            }
        }
        catch (Exception e) { Log.Error(e); }
        return best / 1073741824.0;
    }

    public static double RamGb() { var m = new Native.MEMORYSTATUSEX(); Native.GlobalMemoryStatusEx(m); return m.ullTotalPhys / 1073741824.0; }

    /// <summary>What to download for this PC: a ≥4 GB GPU runs Whisper turbo + Qwen3-4B; otherwise light models on the CPU.</summary>
    public static (string Engine, ModelInfo Stt, ModelInfo Llm, bool Gpu, string Why) Recommend()
    {
        double vram = VramGb(), ram = RamGb();
        bool gpu = vram >= 3.5;
        var stt = gpu ? Whisper[0] : ram >= 8 ? Whisper[1] : Vosk;
        var llm = gpu || ram >= 12 ? Llm[0] : Llm[3];
        return (stt == Vosk ? "vosk" : "whisper", stt, llm, gpu,
            $"Видеокарта {vram:0.#} ГБ, ОЗУ {ram:0} ГБ → {(gpu ? "всё на видеокарте" : "на процессоре")}: {stt.Name.Split(" (")[0]} + {llm.Name.Split(" —")[0]}");
    }

    public static void Delete(ModelInfo m)
    {
        try { if (m.Zip) Directory.Delete(PathOf(m), true); else File.Delete(PathOf(m)); } catch (Exception e) { Log.Error(e); }
    }
}
