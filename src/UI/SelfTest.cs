using System.IO;
using System.Text;

namespace MishkaHelper.Core;

/// <summary>Dev aid: "--selftest file" exercises the admin-only features and writes a report, then exits.</summary>
public static class SelfTest
{
    /// <param name="only">Comma-separated step-name fragments to run (empty = all).</param>
    public static async void Run(string file, MainWindow main, string only = "")
    {
        var sb = new StringBuilder();
        var filter = only.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        void L(string s) { sb.AppendLine($"{DateTime.Now:HH:mm:ss} {s}"); File.WriteAllText(file, sb.ToString()); }
        async Task Step(string name, Func<Task> f)
        {
            if (filter.Length > 0 && !filter.Any(name.Contains)) return;
            try { await f(); } catch (Exception e) { L($"FAIL {name}: {e.GetType().Name}: {e.Message}"); }
        }

        L($"admin={new System.Security.Principal.WindowsPrincipal(System.Security.Principal.WindowsIdentity.GetCurrent()).IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator)} acer={Acer.Available}");

        await Step("acer sensors", async () =>
        {
            L($"sensors cpu={Acer.CpuTemp}C gpu={Acer.GpuTemp}C sys={Acer.SysTemp}C fans={Acer.CpuFanRpm}/{Acer.GpuFanRpm}rpm");
            await Task.CompletedTask;
        });

        await Step("acer modes", async () =>
        {
            int orig = Acer.Mode;
            L($"mode now={orig} supported=[{string.Join(", ", Acer.SupportedModes().Select(m => $"{m.Id}:{m.Name}"))}]");
            foreach (var (id, name) in Acer.SupportedModes())
            {
                Acer.Mode = id;
                await Task.Delay(1500);
                L($"set mode {id} ({name}) -> read {Acer.Mode} {(Acer.Mode == id ? "OK" : "MISMATCH")}");
            }
            Acer.Mode = orig;
            await Task.Delay(800);
            L($"restored mode -> {Acer.Mode}");
        });

        await Step("fans", async () =>
        {
            var orig = Acer.Fans;
            L($"fans mode={orig} rpm={Acer.CpuFanRpm}/{Acer.GpuFanRpm}");
            Acer.Fans = Acer.FanMode.Max;
            await Task.Delay(8000);
            L($"MAX -> mode={Acer.Fans} rpm={Acer.CpuFanRpm}/{Acer.GpuFanRpm}");
            Acer.Fans = Acer.FanMode.Custom;
            Acer.SetFanPercent(1, 40); Acer.SetFanPercent(4, 40);
            await Task.Delay(8000);
            L($"CUSTOM 40% -> mode={Acer.Fans} pct={Acer.GetFanPercent(1)}/{Acer.GetFanPercent(4)} rpm={Acer.CpuFanRpm}/{Acer.GpuFanRpm}");
            Acer.Fans = Acer.FanMode.Auto;
            await Task.Delay(6000);
            L($"AUTO -> mode={Acer.Fans} rpm={Acer.CpuFanRpm}/{Acer.GpuFanRpm}");
        });

        await Step("keyboard", async () =>
        {
            var zones = Enumerable.Range(0, 4).Select(z => Acer.GetZone(1 << z)).ToArray();
            L($"kb raw={string.Join(",", Acer.GetKeyboardRaw() ?? Array.Empty<byte>())} zones={string.Join(" ", zones.Select(c => $"#{c.R:X2}{c.G:X2}{c.B:X2}"))}");
            Osd.Show("", "Смотри на клавиатуру: красный");
            Acer.SetKeyboard(0, 0, 100, 1, 255, 0, 0);
            Acer.SetZone(0xF, 255, 0, 0);
            await Task.Delay(4000);
            L($"after red zones={string.Join(" ", Enumerable.Range(0, 4).Select(z => Acer.GetZone(1 << z)).Select(c => $"#{c.R:X2}{c.G:X2}{c.B:X2}"))}");
            Osd.Show("", "Зоны: красный / зелёный / синий / белый");
            Acer.SetZone(1, 255, 0, 0); Acer.SetZone(2, 0, 255, 0); Acer.SetZone(4, 0, 0, 255); Acer.SetZone(8, 255, 255, 255);
            await Task.Delay(4000);
            Osd.Show("", "Эффект: волна");
            Acer.SetKeyboard(3, 4, 100, 1, 0, 0, 0);
            await Task.Delay(5000);
            Osd.Show("", "Возвращаю исходные цвета");
            Acer.SetKeyboard(0, 0, 100, 1, zones[0].R, zones[0].G, zones[0].B);
            for (int z = 0; z < 4; z++) Acer.SetZone(1 << z, zones[z].R, zones[z].G, zones[z].B);
            L("keyboard restored");
        });

        await Step("defender status", async () =>
        {
            var s = await Task.Run(Security.Status);
            L($"defender {s}");
            L($"threats={Security.Threats().Count} quarantine=[{string.Join("; ", await Security.Quarantine())}]");
        });

        await Step("defender custom scan", async () =>
        {
            var tmp = Path.Combine(Path.GetTempPath(), "mishka-scan-test");
            Directory.CreateDirectory(tmp);
            File.WriteAllText(Path.Combine(tmp, "hello.txt"), "harmless");
            var (threats, outp) = await Security.Scan(Security.ScanKind.Custom, tmp);
            L($"custom scan threats={threats} out={outp.Replace("\r", "").Replace("\n", " | ").Trim()}");
            Directory.Delete(tmp, true);
        });

        await Step("defender quick scan", async () =>
        {
            var t0 = DateTime.Now;
            var (threats, outp) = await Security.Scan(Security.ScanKind.Quick);
            L($"quick scan {(DateTime.Now - t0).TotalSeconds:0}s threats={threats} out={outp.Replace("\r", "").Replace("\n", " | ").Trim()}");
        });

        await Step("sweep", async () =>
        {
            var list = await Security.Sweep();
            L($"sweep found {list.Count}");
            foreach (var x in list.Take(15)) L($"  [{x.Score}] {x.Name} | {x.Where} | {x.ReasonText} | {x.Path}");
        });

        await Step("cleanup measure", async () =>
        {
            foreach (var t in Cleaner.Targets())
            {
                if (t.Special && t.Key != "recycle") continue;
                await Task.Run(() => Cleaner.Measure(t, CancellationToken.None));
                L($"measure {t.Key}: {Cleaner.Size(t.Size)} in {t.Files} files");
            }
        });

        await Step("cleanup temp", async () =>
        {
            foreach (var key in new[] { "usertemp", "wintemp" })
            {
                var t = Cleaner.Targets().First(x => x.Key == key);
                await Task.Run(() => Cleaner.Measure(t, CancellationToken.None));
                var before = t.Size;
                var freed = await Task.Run(() => Cleaner.Clean(t, CancellationToken.None));
                await Task.Run(() => Cleaner.Measure(t, CancellationToken.None));
                L($"clean {key}: before={Cleaner.Size(before)} freed={Cleaner.Size(freed)} left={Cleaner.Size(t.Size)} (locked files stay)");
            }
            L($"trim ram: {Cleaner.TrimRam()} processes");
        });

        await Step("disk health", async () =>
        {
            foreach (var d in await Task.Run(SystemInfo.Disks)) L($"disk {d}");
        });

        await Step("autostart", async () =>
        {
            await Task.Run(() => Autostart.Set(true));
            L($"autostart task enabled={Autostart.Enabled} pointsHere={Autostart.PointsHere}");
        });

        await Step("brain", async () =>
        {
            if (!Models.Has(LlamaServer.Runtime)) { L("downloading llama.cpp…"); await Models.Download(LlamaServer.Runtime); }
            Settings.Current.Jarvis.AiModel = "qwen3-4b-2507";
            if (!Models.Has(Models.LlmModel)) { L("downloading model…"); await Models.Download(Models.LlmModel); }
            var w0 = DateTime.Now;
            await LlamaServer.EnsureRunning();
            L($"llama-server up in {(DateTime.Now - w0).TotalSeconds:0.0}s");
            // Volume/brightness are restored afterwards so the test doesn't leave the PC changed.
            int vol = Audio.Volume;
            foreach (var q in new[] { "привет, как дела у компа?", "сделай немного тише и скажи сколько сейчас времени", "почему ноут может тормозить в играх?", "запиши в заметку сдать лабу по физике в пятницу", "что у меня по физике?", "кто такой Тони Старк в двух словах" })
            {
                var t0 = DateTime.Now;
                ChatHistory.Add(true, q);
                var r = await Brain.Ask(q);
                ChatHistory.Add(false, r.Say);
                L($"brain {(DateTime.Now - t0).TotalSeconds:0.0}s  Q: {q}  A: {r.Say}{(r.Confirm != null ? " [confirm]" : "")}");
            }
            Audio.Volume = vol;
        });

        await Step("ears", async () =>
        {
            var phrases = new[] { "Джарвис, включи ночной свет и сделай громкость тридцать.", "Джарвис, что у меня завтра по парам?", "Запиши в заметку купить молоко и хлеб." };
            var synth = new Windows.Media.SpeechSynthesis.SpeechSynthesizer();
            synth.Voice = Windows.Media.SpeechSynthesis.SpeechSynthesizer.AllVoices.First(v => v.Language.StartsWith("ru"));
            var whisperTurbo = Models.Whisper.First(w => w.Key == "turbo");
            if (!Models.Has(whisperTurbo)) { L("downloading whisper turbo…"); await Models.Download(whisperTurbo); }
            foreach (var engine in new[] { "whisper", "vosk" })
            {
                Settings.Current.Jarvis.Engine = engine;
                Settings.Current.Jarvis.WhisperModel = "turbo";
                foreach (var ph in phrases)
                {
                    using var st = await synth.SynthesizeTextToStreamAsync(ph);
                    using var ms = new MemoryStream();
                    await st.AsStreamForRead().CopyToAsync(ms);
                    ms.Position = 0;
                    using var rd = new NAudio.Wave.WaveFileReader(ms);
                    NAudio.Wave.ISampleProvider src = NAudio.Wave.WaveExtensionMethods.ToSampleProvider(rd);
                    if (rd.WaveFormat.Channels == 2) src = new NAudio.Wave.SampleProviders.StereoToMonoSampleProvider(src);
                    var mono = new NAudio.Wave.SampleProviders.WdlResamplingSampleProvider(src, 16000);
                    var pcm16 = new NAudio.Wave.SampleProviders.SampleToWaveProvider16(mono);
                    using var outMs = new MemoryStream();
                    var buf = new byte[4096]; int n;
                    while ((n = pcm16.Read(buf, 0, buf.Length)) > 0) outMs.Write(buf, 0, n);
                    var t0 = DateTime.Now;
                    var text = await Ears.TestTranscribe(outMs.ToArray());
                    L($"ears[{engine}] {(DateTime.Now - t0).TotalSeconds:0.00}s  said: {ph}  heard: {text}  wake={Ears.WakeIndex(text)}");
                }
            }
        });

        await Step("voice", async () =>
        {
            var j = Settings.Current.Jarvis;
            j.Tts = "piper"; j.PiperVoice = "dmitri"; j.VoiceFx = true;
            var t0 = DateTime.Now;
            await Voice.DownloadPiper("dmitri");
            L($"piper ready={Voice.PiperReady("dmitri")} in {(DateTime.Now - t0).TotalSeconds:0}s");
            var dir = Path.GetDirectoryName(file)!;
            foreach (var (fx, name) in new[] { (true, "jarvis-fx.wav"), (false, "jarvis-plain.wav") })
            {
                j.VoiceFx = fx;
                t0 = DateTime.Now;
                var sec = await Voice.RenderToFile("Добрый вечер, сэр. Все системы в норме. Температура процессора сорок пять градусов. Чем могу помочь?", Path.Combine(dir, name));
                L($"voice fx={fx}: {sec:0.0}s audio in {(DateTime.Now - t0).TotalSeconds:0.00}s → {name}");
            }
        });

        // Voice samples for choosing: every Edge voice plain, plus the butler effect on the male ones.
        await Step("voices", async () =>
        {
            var j = Settings.Current.Jarvis;
            var dir = Path.Combine(Path.GetDirectoryName(file)!, "voices");
            Directory.CreateDirectory(dir);
            const string line = "Добрый вечер, сэр. Все системы в норме, температура процессора сорок пять градусов. Напоминаю: завтра в девять у вас пара по физике. Чем могу помочь?";
            j.Tts = "edge";
            // Film mode samples first.
            const string en = "Good evening, sir. All systems are online and running at optimal levels. Your physics lecture is tomorrow at nine. Shall I set a reminder?";
            j.Movie = true;
            foreach (var (id, _) in EdgeTts.Movie)
                foreach (var (fx, pitch) in new[] { (false, 0), (true, -3) })
                {
                    j.MovieVoice = id; j.VoiceFx = fx; j.EdgePitch = pitch;
                    var fn = $"00-movie-{id.Split('-')[2].Replace("Neural", "")}{(fx ? "-fx" : "")}.wav";
                    try { var sec = await Voice.RenderToFile(en, Path.Combine(dir, fn)); L($"voice {fn}: {sec:0.0}s"); }
                    catch (Exception e) { L($"voice {fn}: FAIL {e.Message}"); }
                }
            j.Movie = false;
            int n = 1;
            foreach (var (id, name) in EdgeTts.Russian)
            {
                foreach (var fx in new[] { false, true })
                {
                    if (fx && id.Contains("Svetlana")) continue;
                    j.EdgeVoice = id; j.VoiceFx = fx; j.EdgePitch = fx ? -4 : 0;
                    var fn = $"{n:00}-{id.Split('-')[2].Replace("MultilingualNeural", "").Replace("Neural", "")}{(fx ? "-jarvis-fx" : "")}.wav";
                    try { var sec = await Voice.RenderToFile(line, Path.Combine(dir, fn)); L($"voice {fn}: {sec:0.0}s"); }
                    catch (Exception e) { L($"voice {fn}: FAIL {e.Message}"); }
                }
                n++;
            }
        });

        // Real end-to-end update: check GitHub, download this edition's package, apply (the app quits and restarts).
        await Step("update", async () =>
        {
            L($"edition={Edition.Name} version={Edition.Version} installed={Updater.IsInstalled}");
            var u = await Updater.Check();
            L(u == null ? "no newer release" : $"found {u.Version}: setup={u.SetupUrl} portable={u.PortableUrl}");
            if (u != null) { L("installing…"); await Updater.Install(u, f => { if ((int)(f * 100) % 25 == 0) L($"  {f:P0}"); }); }
        });

        if (filter.Contains("brain") || filter.Contains("ears") || filter.Contains("voice") || filter.Contains("voices") || filter.Contains("update")) { L("DONE"); App.Current.Quit(); return; }
        L("DONE, rendering pages");
        Shots.Run(Path.Combine(Path.GetDirectoryName(file)!, "shots-admin"), main);   // quits when done
    }
}
