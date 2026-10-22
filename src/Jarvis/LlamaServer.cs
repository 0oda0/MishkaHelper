using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace MishkaHelper.Core;

/// <summary>Windows job object: child processes are killed when Mishka exits, even on a crash.</summary>
public static class ChildJob
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    static extern IntPtr CreateJobObject(IntPtr attrs, string? name);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool SetInformationJobObject(IntPtr job, int cls, ref ExtendedLimits info, int size);
    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct ExtendedLimits
    {
        public long PerProcessUserTimeLimit, PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass, SchedulingClass;
        public ulong ReadOps, WriteOps, OtherOps, ReadBytes, WriteBytes, OtherBytes;   // IO_COUNTERS
        public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
    }

    static readonly IntPtr Job = Create();

    static IntPtr Create()
    {
        var job = CreateJobObject(IntPtr.Zero, null);
        var info = new ExtendedLimits { LimitFlags = 0x2000 };   // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE
        SetInformationJobObject(job, 9, ref info, System.Runtime.InteropServices.Marshal.SizeOf<ExtendedLimits>());
        return job;
    }

    public static void Add(Process p) { try { AssignProcessToJobObject(Job, p.Handle); } catch (Exception e) { Log.Error(e); } }
}

/// <summary>
/// Runs the official llama.cpp server (Vulkan build: any GPU, CPU fallback) as a hidden child process
/// and talks to it over localhost. Keeps the model warm between questions; a crash can't take Mishka down.
/// </summary>
public static class LlamaServer
{
    const string Tag = "b11819";
    public static readonly ModelInfo Runtime = new("llama-server", "llama.cpp (Vulkan, 35 МБ)",
        $"https://github.com/ggml-org/llama.cpp/releases/download/{Tag}/llama-{Tag}-bin-win-vulkan-x64.zip", 35, "llama-cpp", Zip: true);

    const int Port = 18765;
    static Process? _proc;
    static string? _model;
    static readonly HttpClient Http = new() { BaseAddress = new Uri($"http://127.0.0.1:{Port}"), Timeout = TimeSpan.FromMinutes(3) };
    static readonly SemaphoreSlim StartLock = new(1, 1);

    static string Exe => Path.Combine(Models.PathOf(Runtime), "llama-server.exe");

    public static async Task EnsureRunning()
    {
        var m = Models.LlmModel;
        await StartLock.WaitAsync();
        try
        {
            if (_proc is { HasExited: false } && _model == m.Key && await Healthy()) return;
            Stop();
            if (!File.Exists(Exe)) throw new InvalidOperationException("Нужен llama.cpp — скачай его на странице «Джарвис»");
            if (!Models.Has(m)) throw new InvalidOperationException("Нейросеть не скачана");

            // A leftover server (e.g. after a crash) would hold the port and answer instead of ours.
            foreach (var old in Process.GetProcessesByName("llama-server"))
            {
                try { if (string.Equals(old.MainModule?.FileName, Exe, StringComparison.OrdinalIgnoreCase)) { old.Kill(); old.WaitForExit(3000); } } catch { }
                old.Dispose();
            }

            // Pick the discrete GPU explicitly: by default llama.cpp would also split onto the slow integrated one.
            var dev = Settings.Current.Jarvis.AiGpu ? BestDevice() : "none";
            var gpu = dev == "none" ? "-dev none -ngl 0" : $"-dev {dev} --fit on";
            var psi = new ProcessStartInfo(Exe,
                $"-m \"{Models.PathOf(m)}\" --host 127.0.0.1 --port {Port} -c 8192 {gpu} -fa auto --jinja --no-webui -t {Math.Max(2, Environment.ProcessorCount / 2)}")
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardError = true, RedirectStandardOutput = true,
                WorkingDirectory = Path.GetDirectoryName(Exe)!,
            };
            _proc = Process.Start(psi)!;
            ChildJob.Add(_proc);
            var tail = new Queue<string>();
            void Keep(string? l) { if (l == null) return; lock (tail) { tail.Enqueue(l); while (tail.Count > 30) tail.Dequeue(); } }
            _proc.ErrorDataReceived += (_, e) => Keep(e.Data);
            _proc.OutputDataReceived += (_, e) => Keep(e.Data);
            _proc.BeginErrorReadLine(); _proc.BeginOutputReadLine();
            _model = m.Key;

            // Loading a few GB from disk takes a while the first time.
            for (int i = 0; i < 240; i++)
            {
                if (_proc.HasExited)
                {
                    string log; lock (tail) log = string.Join("\n", tail);
                    Log.Info("llama-server exited:\n" + log);
                    throw new InvalidOperationException("llama.cpp не запустился (см. журнал)");
                }
                if (await Healthy()) { Log.Info($"llama-server ready: {m.Key}, gpu={gpu}"); return; }
                await Task.Delay(500);
            }
            throw new TimeoutException("Нейросеть слишком долго загружается");
        }
        finally { StartLock.Release(); }
    }

    static string? _device;

    /// <summary>"Vulkan0"… for the strongest GPU; integrated Intel/AMD graphics only if nothing else exists.</summary>
    static string BestDevice()
    {
        if (_device != null) return _device;
        var o = Shell.Run(Exe, "--list-devices", 30_000, Encoding.UTF8);
        var devs = System.Text.RegularExpressions.Regex.Matches(o, @"(Vulkan\d+):\s*(.+?)\s*\((\d+) MiB")
            .Select(x => (Id: x.Groups[1].Value, Name: x.Groups[2].Value, Mb: int.Parse(x.Groups[3].Value))).ToList();
        bool Integrated(string n) => System.Text.RegularExpressions.Regex.IsMatch(n, @"UHD|Iris|HD Graphics|Radeon\(TM\) Graphics|Radeon Graphics|Vega \d+ Graphics", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var pick = devs.Where(d => !Integrated(d.Name)).OrderByDescending(d => d.Mb).FirstOrDefault();
        if (pick.Id == null) pick = devs.FirstOrDefault();
        _device = pick.Id ?? "none";
        Log.Info($"llama device: {_device} {pick.Name}");
        return _device;
    }

    static async Task<bool> Healthy()
    {
        try { using var r = await Http.GetAsync("/health"); return r.IsSuccessStatusCode; }
        catch { return false; }
    }

    public static void Stop()
    {
        try { if (_proc is { HasExited: false }) _proc.Kill(true); } catch { }
        _proc?.Dispose(); _proc = null; _model = null;
    }

    /// <summary>OpenAI-style chat completion with native tool calling. Returns the assistant message object.</summary>
    public static async Task<JsonObject> Chat(JsonArray messages, JsonArray? tools = null, int maxTokens = 600)
    {
        await EnsureRunning();
        var body = new JsonObject
        {
            ["messages"] = messages.DeepClone(),
            ["max_tokens"] = maxTokens,
            ["temperature"] = 0.4,
            ["chat_template_kwargs"] = new JsonObject { ["enable_thinking"] = false },
        };
        if (tools != null) body["tools"] = tools.DeepClone();
        using var resp = await Http.PostAsync("/v1/chat/completions", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        var json = await resp.Content.ReadAsStringAsync();
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException("llama.cpp: " + json);
        var msg = JsonNode.Parse(json)!["choices"]![0]!["message"]!.AsObject();
        if (msg["content"]?.GetValue<string>() is { } c)
            msg["content"] = System.Text.RegularExpressions.Regex.Replace(c, @"<think>[\s\S]*?</think>", "").Trim();
        return (JsonObject)msg.DeepClone();
    }
}
