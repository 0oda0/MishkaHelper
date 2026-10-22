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

        L("DONE, rendering pages");
        Shots.Run(Path.Combine(Path.GetDirectoryName(file)!, "shots-admin"), main);   // quits when done
    }
}
