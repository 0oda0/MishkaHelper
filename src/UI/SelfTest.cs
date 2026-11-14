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

        await Step("fullclean", async () =>   // analysis only, changes nothing
        {
            foreach (var (it, v, _) in FullClean.Startup()) L($"startup {v,-4} {it.Name} | {it.Command}");
            foreach (var b in await Task.Run(FullClean.Background)) L($"bg      {b.Verdict,-4} {b.Name} x{b.Count} {Cleaner.Size(b.Bytes)}");
        });

        await Step("phonelive", async () =>   // real phone over `adb reverse tcp:8770 tcp:8770`: log everything it sends, push clip + file, no ringing
        {
            var pin = Settings.Current.RemotePin; var notifs = Settings.Current.PhoneNotifications;
            Settings.Current.RemotePin = "432198"; Settings.Current.PhoneNotifications = true;   // show them, so the owner sees phone notifications arrive
            Remote.StartLocal();
            L("phonelive: waiting for the phone (PIN 432198)…");
            int seenNotifs = 0; bool pushed = false; var until = DateTime.Now.AddMinutes(20);   // the owner tries things by hand meanwhile; <report>.stop ends it early
            while (DateTime.Now < until)
            {
                await Task.Delay(1000);
                if (Phone.Connected && Phone.Last != null && !pushed)
                {
                    pushed = true;
                    L($"phonelive: connected, status {Phone.Last}");
                    L($"phonelive: clip → {await Phone.SendClipboard("Привет с ПК — Mishka 1.0")}");
                    var tmp = Path.Combine(Path.GetTempPath(), "Mishka-test.txt");
                    File.WriteAllText(tmp, "Файл с ПК: проверка Mishka 1.0");
                    L($"phonelive: file → {await Phone.SendFile(tmp)}");
                }
                List<Phone.Notification> ns;
                lock (Phone.Notifications) ns = Phone.Notifications.ToList();
                foreach (var n in ns.Take(ns.Count - seenNotifs).Reverse()) L($"phonelive: notification {n.App}: {n.Title} — {n.Text}");
                seenNotifs = ns.Count;
                if (File.Exists(file + ".stop")) break;
            }
            L($"phonelive: end, connected={Phone.Connected} last={Phone.Last}");
            Remote.Stop();
            Settings.Current.RemotePin = pin; Settings.Current.PhoneNotifications = notifs;
        });

#if SECRETARY
        await Step("secretary", async () =>   // nothing is opened, moved or saved; no microphone
        {
            foreach (var name in new[] { "chrome", "msedge", "browser", "firefox" })
                foreach (var p in System.Diagnostics.Process.GetProcessesByName(name).Where(p => p.MainWindowHandle != IntPtr.Zero).Take(1))
                    L($"secretary: {name} address → {(SecCapture.BrowserUrl(p.MainWindowHandle, name) is { } u ? new Uri(u).Host : "не прочитан")}");
            var snap = await SecWorkspace.Snapshot();
            L($"secretary: workspace {string.Join(", ", snap.GroupBy(s => s.Kind).Select(g => g.Key + "=" + g.Count()))}");
            L($"secretary: downloads {string.Join(", ", SecDownloads.Pending().GroupBy(i => i.Category).Select(g => g.Key + "=" + g.Count()))}");
            L($"secretary: secrets round trip {Secrets.Unprotect(Secrets.Protect("проверка")) == "проверка"}");
            if (Models.Has(Models.Vosk))
            {
                using var model = new Vosk.Model(Models.PathOf(Models.Vosk));
                using var rec = new Vosk.VoskRecognizer(model, 16000);
                rec.AcceptWaveform(new byte[32000], 32000);
                L("secretary: vosk on silence → " + rec.FinalResult().Replace("\n", " "));
            }
            else L("secretary: vosk model not downloaded");
        });
#endif

#if SECRETARY
        await Step("seclive", async () =>   // by name only: remote on localhost, phrases go in as from the phone, everything added is taken back at the end; no sound
        {
            if (!filter.Contains("seclive")) return;
            var sec = Settings.Current.Secretary; var was = sec.Enabled; var pin = Settings.Current.RemotePin;
            int rem = Settings.Current.Reminders.Count, notes = Notes.All.Count, tasks = SecStore.Cur.Tasks.Count, trig = SecStore.Cur.Triggers.Count;
            sec.Enabled = true; Settings.Current.RemotePin = "432198";
            Remote.StartLocal();
            foreach (var t in new[] { "надо купить молоко", "послезавтра в 18:00 сдать отчёт", "каждый вторник в 18:00 тренировка", "день рождения мамы 12 мая", "когда открою Блокнот, проверить титульник", "пароль от вайфая на холодильнике" })
                L($"seclive: {t} → {System.Text.Json.JsonSerializer.Serialize(await Remote.Do("secAdd", t), new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })}");
            L($"seclive: tasks {string.Join(" | ", Secretary.OpenTasks().Select(t => t.Text))}");
            L($"seclive: repeats {string.Join(" | ", Settings.Current.Reminders.Where(r => r.Repeat != "").Select(r => $"{r.Text} {r.Repeat} {r.At:dd.MM HH:mm} lead={r.LeadDays}"))}");
            L($"seclive: day log {string.Join(" | ", Secretary.DayLog())}");
            L($"seclive: file search 'договор' → {(await SecDownloads.Find("договор")).Count}");
            Toast.Show("", "Напоминание", "Проверка кнопок: 10 минут, час, завтра.", null, new (string, Action)[]
            {
                ("10 мин", () => L("seclive: snooze pressed")), ("Час", () => { }), ("Завтра", () => { }),
            });
            main.Navigate("Secretary");
            L("seclive: ready");
            var until = DateTime.Now.AddMinutes(20);
            while (DateTime.Now < until && !File.Exists(file + ".stop")) await Task.Delay(1000);
            Remote.Stop();
            Settings.Current.Reminders.RemoveRange(rem, Settings.Current.Reminders.Count - rem);
            while (Notes.All.Count > notes) Notes.Remove(Notes.All[^1]);
            SecStore.Cur.Tasks.RemoveRange(tasks, SecStore.Cur.Tasks.Count - tasks);
            SecStore.Cur.Triggers.RemoveRange(trig, SecStore.Cur.Triggers.Count - trig);
            sec.Enabled = was; Settings.Current.RemotePin = pin;
            SecStore.Save(); Settings.Save();
            L("seclive: cleaned up");
        });
#endif

#if SECRETARY
        await Step("sectrack", async () =>   // by name only: logs what the secretary says about each program in front; sample data is taken back at the end; no sound
        {
            if (!filter.Contains("sectrack")) return;
            var sec = Settings.Current.Secretary; var was = sec.Enabled; var store = SecStore.Cur;
            var tasks = store.Tasks.ToList(); var notes = new Dictionary<string, string>(store.AppNotes); var pages = store.Remembered.ToList(); var inbox = store.Inbox.ToList();
            sec.Enabled = true; sec.Bubble = true; sec.ContextHints = true; sec.AppHints = true;
            var speakWas = Settings.Current.Jarvis.Speak; Settings.Current.Jarvis.Speak = false;   // the owner's rule: no sound in tests
            store.AppNotes["notepad"] = "Пример заметки: черновики храню в папке Учёба";
            store.Tasks.Add(new SecTask { Text = "Проверить таблицу в excel", Due = DateTime.Today });
            store.Tasks.Add(new SecTask { Text = "Просроченное для проверки", Due = DateTime.Today.AddDays(-2) });
            store.Remembered.Add(new RememberedItem { Title = "Пример сайта", Target = "https://example.com/page", App = "msedge" });
            SecInbox.ReviewOverdue(DateTime.Now);
            SecInbox.Add(new InboxItem { Kind = "offer", Title = "Напомнить завтра? (из письма)", Text = "Встреча с куратором", At = DateTime.Now.AddDays(1), HasTime = false });
            Secretary.Hinted += (app, hint) => L($"sectrack: {app} → {hint ?? "молчит"}");
            Secretary.Apply();
            L("sectrack: ready");
            foreach (var q in new[] { "что сегодня?", "какой план", "что просрочено", "где заметка", "что с почтой?", "расписание завтра" })
                L($"sectrack: «{q}» → {(await SecAsk.Reply(q)).Text.Replace('\n', ' ')}");
            var until = DateTime.Now.AddMinutes(20);
            while (DateTime.Now < until && !File.Exists(file + ".stop")) await Task.Delay(500);
            store.Tasks = tasks; store.AppNotes = notes; store.Remembered = pages; store.Inbox = inbox; sec.Enabled = was; Settings.Current.Jarvis.Speak = speakWas;
            SecStore.Save(); Settings.Save();
            L("sectrack: cleaned up");
        });
#endif

        await Step("kbapi", async () =>   // by name only: the phone's game tab calls, straight through the remote's handler
        {
            if (!filter.Contains("kbapi")) return;
            string J(object? o) => System.Text.Json.JsonSerializer.Serialize(o, new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
            L("kbapi: list " + J(await Remote.Do("kbList", "")));
            var kb = J(await Remote.Do("kb", "steam-1086940"));
            L($"kbapi: kb {kb.Length} chars, starts {kb[..Math.Min(300, kb.Length)]}");
            var b = GameKb.All().First(x => x.Key == "steam-1086940");
            var item = b.Entries.First(e => e.Kind == "item" && e.Text.Length == 0);
            L("kbapi: open " + J(await Remote.Do("kbOpen", b.Key + "	" + item.Id)));
            L("kbapi: ask " + J(await Remote.Do("kbAsk", b.Key + "	как победить Этель?")));
            var was = b.Progress;
            await Remote.Do("kbProgress", b.Key + "	3");
            L($"kbapi: progress set → {GameKb.All().First(x => x.Key == b.Key).Progress}");
            await Remote.Do("kbProgress", b.Key + "	" + was);
        });

        await Step("gamekb", async () =>   // by name only, game in MISHKA_GAME: builds its base with whatever brain is set up (no sound), writes what came out
        {
            if (!filter.Contains("gamekb")) return;
            var name = Environment.GetEnvironmentVariable("MISHKA_GAME") ?? "Probably Stolen";
            var id = await GameSources.SteamFind(name);
            L($"gamekb: steam folder check: {GameKb.Steam(@"D:\SteamLibrary\steamapps\common\Baldurs Gate 3\bin\bg3.exe")}");
            L($"gamekb: {name} → steam {id}, brain online={Online.Active} local={LlamaServer.LocalReady}");
            var g = id > 0 ? new GameKb.Game("steam-" + id, name, "", id, "") : GameKb.Manual("", name) with { Key = GameKb.Key(name) };
            var t0 = DateTime.Now;
            await GameBuilder.Build(g);
            var b = GameKb.All().First(x => x.Key == g.Key);
            L($"gamekb: {(DateTime.Now - t0).TotalMinutes:0.0} min, status='{b.Status}', wiki={b.Wiki}, stages={b.Stages.Count}, entries={b.Entries.Count}");
            foreach (var st in b.Stages) L($"  stage {st.N}: {st.Name} — {st.Summary}");
            foreach (var grp in b.Entries.GroupBy(e => e.Kind)) L($"  {grp.Key}: {grp.Count()} (with text {grp.Count(e => e.Text.Length > 0)})");
            foreach (var e in b.Entries.Where(e => e.Text.Length > 0).GroupBy(e => e.Kind).SelectMany(x => x.Take(2)))
                L($"  [{e.Kind} st{e.Stage}{(e.Spoiler ? " spoiler" : "")}] {e.Title} / {e.TitleEn}: {(e.Text.Length > 300 ? e.Text[..300] + "…" : e.Text)}");
            var item = b.Entries.FirstOrDefault(e => e.Kind == "item");
            if (item != null) { await GameBuilder.Expand(b, item); L($"  expand item: {item.Title}: {item.Text}"); }
            foreach (var q in new[] { "босс", "оружие" }) L($"  search '{q}': {string.Join(", ", GameBuilder.Search(b, q).Take(5).Select(e => e.Title))}");
            L($"  answer: {await GameBuilder.Answer(b, "С чего начать новичку?")}");
        });

#if SECRETARY
        await Step("secsched", async () =>   // by name only, URL in MISHKA_SCHED_URL: the owner signs in once in the window, then the real page is read until the day is found
        {
            if (!filter.Contains("secsched")) return;
            var url = Environment.GetEnvironmentVariable("MISHKA_SCHED_URL") ?? "";
            var sec = Settings.Current.Secretary; var was = sec.Enabled; var store = SecStore.Cur; var pages = store.Remembered.ToList();
            sec.Enabled = true; sec.ScheduleRead = true;
            store.Remembered.Insert(0, new RememberedItem { Title = "Расписание", Target = url, Daily = true });
            WebReader.OpenWindow(url, "Вход на сайт расписания");
            var until = DateTime.Now.AddMinutes(15);
            while (DateTime.Now < until && !File.Exists(file + ".stop"))
            {
                await Task.Delay(20000);
                var today = await SecSchedule.Lines(DateTime.Today, fresh: true);
                var tomorrow = await SecSchedule.Lines(DateTime.Today.AddDays(1), fresh: true);
                L($"secsched: today {today.Count}, tomorrow {tomorrow.Count}, problem={store.ScheduleProblem}");
                foreach (var x in today) L("  сегодня: " + x);
                foreach (var x in tomorrow) L("  завтра: " + x);
                if (today.Count + tomorrow.Count > 0) break;
            }
            store.Remembered = pages; sec.Enabled = was; store.ScheduleProblem = ""; SecStore.Save(); Settings.Save();
            L("secsched: done");
        });
#endif

        await Step("supportpic", async () =>   // by name only: one real message with a tiny picture to the live server
        {
            if (!filter.Contains("supportpic")) return;
            var png = Path.Combine(Path.GetTempPath(), "mishka-test.png");
            File.WriteAllBytes(png, Convert.FromHexString("89504e470d0a1a0a0000000d49484452000000010000000108060000001f15c4890000000d49444154789c6360000002000001e221bc330000000049454e44ae426082"));
            await Support.Send("Проверка скриншота из самотеста", png);
            L("supportpic: sent, messages " + (await Support.Messages()).Count);
        });

        await Step("ownvoice", async () =>   // by name only, with MISHKA_NO_SPEECH=1 and a test data folder: fetches the voice, renders to a file, never plays anything
        {
            if (!filter.Contains("ownvoice")) return;
            var j = Settings.Current.Jarvis;
            j.Tts = "windows"; j.Speak = false;
            L($"ownvoice: windows voices {Voice.HasWindowsVoices}, needs own {Voice.NeedsOwnVoice}");
            L($"ownvoice: fetched {await Voice.EnsureOwnVoice()} ({Voice.OwnVoiceStatus})");
            var wav = Path.Combine(Path.GetTempPath(), "mishka-ownvoice.wav");
            L($"ownvoice: rendered {await Voice.RenderToFile("Добрый вечер. Это проверка собственного голоса.", wav):0.0} s, {new FileInfo(wav).Length} bytes");
            File.Delete(wav);
        });

        await Step("palette", async () =>   // by name only: opens the command palette a little later, for a hand check over another program
        {
            if (!filter.Contains("palette")) return;
            await Task.Delay(15000);
            CommandPalette.Toggle();
            L("palette: open");
            await Task.Delay(25000);
        });

        await Step("support", async () =>   // real server: sends one test message, then waits up to 15 min for an answer in the page
        {
            if (!filter.Contains("support")) return;   // writes to the live server: only when asked by name
            var at = DateTime.Now.ToString("HH:mm:ss");
            await Support.Send("Проверка чата из самотеста " + at);
            L("support: sent, messages " + (await Support.Messages()).Count);
            main.Navigate("Support");
            for (int i = 0; i < 180; i++)
            {
                await Task.Delay(5000);
                await Support.CheckReplies();
                var reply = (await Support.Messages()).LastOrDefault(m => m.FromAdmin);
                if (reply != null) { L("support: reply arrived: " + reply.Body); await Task.Delay(15000); return; }
            }
            L("support: no reply in 15 min");
        });

        await Step("phone", async () =>   // plays the phone app on this PC: discovery, websocket, status, ring, file
        {
            var pin = Settings.Current.RemotePin; var notifs = Settings.Current.PhoneNotifications;
            Settings.Current.RemotePin = "432198"; Settings.Current.PhoneNotifications = false;
            Remote.StartLocal(); Phone.StartDiscovery();
            using (var udp = new System.Net.Sockets.UdpClient())
            {
                udp.Client.ReceiveTimeout = 2000;
                var ask = Encoding.UTF8.GetBytes("MISHKA?");
                await udp.SendAsync(ask, ask.Length, "127.0.0.1", Phone.DiscoveryPort);
                var from = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
                try { L("phone discovery: " + Encoding.UTF8.GetString(udp.Receive(ref from))); } catch (Exception e) { L("phone discovery FAIL: " + e.Message); }
            }
            using var ws = new System.Net.WebSockets.ClientWebSocket();
            ws.Options.SetRequestHeader("X-Pin", Settings.Current.RemotePin);
            await ws.ConnectAsync(new Uri($"ws://localhost:{Settings.Current.RemotePort}/api/ws"), CancellationToken.None);
            async Task SendJ(string j) => await ws.SendAsync(Encoding.UTF8.GetBytes(j), System.Net.WebSockets.WebSocketMessageType.Text, true, CancellationToken.None);
            async Task<string> Recv() { var b = new byte[8192]; var r = await ws.ReceiveAsync(b, new CancellationTokenSource(3000).Token); return Encoding.UTF8.GetString(b, 0, r.Count); }
            await SendJ("{\"t\":\"status\",\"name\":\"TestPhone\",\"battery\":77,\"charging\":true,\"free\":1000000000,\"total\":64000000000}");
            await SendJ("{\"t\":\"notif\",\"app\":\"Telegram\",\"title\":\"Мама\",\"text\":\"Ты где?\"}");
            await Task.Delay(500);
            L($"phone connected={Phone.Connected} status={Phone.Last} notif={Phone.Notifications.FirstOrDefault()}");
            await Phone.Ring();
            L("phone ring → " + await Recv());
            var tmp = Path.Combine(Path.GetTempPath(), "mishka-phone-test.txt");
            File.WriteAllText(tmp, "привет с ПК");
            await Phone.SendFile(tmp);
            var offer = await Recv();
            L("phone file offer → " + offer);
            var id = System.Text.Json.JsonDocument.Parse(offer).RootElement.GetProperty("id").GetString();
            using var http = new System.Net.Http.HttpClient();
            var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, $"http://localhost:{Settings.Current.RemotePort}/api/phone/file/{id}");
            req.Headers.Add("X-Pin", Settings.Current.RemotePin);
            var got = await (await http.SendAsync(req)).Content.ReadAsStringAsync();
            L($"phone file download: «{got}» {(got == "привет с ПК" ? "OK" : "MISMATCH")}");
            File.Delete(tmp);
            await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, "", CancellationToken.None);
            await Task.Delay(300);
            L($"phone after close connected={Phone.Connected}");
            Remote.Stop();
            lock (Phone.Notifications) Phone.Notifications.Clear();
            Settings.Current.RemotePin = pin; Settings.Current.PhoneNotifications = notifs;
        });

        await Step("plan", async () =>   // silent: typed path, no ducking, no browser; everything created is removed after
        {
            var j = Settings.Current.Jarvis;
            var (duck, google) = (j.Duck, Settings.Current.GoogleCalendar);
            j.Duck = false; Settings.Current.GoogleCalendar = false;
            int sc0 = Settings.Current.Scenarios.Count, rem0 = Settings.Current.Reminders.Count;
            foreach (var q in new[]
                     {
                         "Через минут 5 запусти фотошоп и включи режим работы",
                         "В следующее воскресенье позарез надо сделать лабу по физике",
                         "завтра в 6 вечера тренировка",
                     })
                L($"plan «{q}» → {await Jarvis.HandleTyped(q)}");
            foreach (var s in Settings.Current.Scenarios.Skip(sc0))
                L($"plan   scenario: {s.Name} | {string.Join(", ", s.Triggers.Select(t => t.Kind + "=" + t.Value))} | steps: {string.Join(" / ", s.Steps.Select(x => x.Kind + ":" + x.Value))}");
            foreach (var r in Settings.Current.Reminders.Skip(rem0))
                L($"plan   reminder: {r.Text} at {r.At:yyyy-MM-dd HH:mm} time={r.HasTime}");
            Settings.Current.Scenarios.RemoveRange(sc0, Settings.Current.Scenarios.Count - sc0);
            Settings.Current.Reminders.RemoveRange(rem0, Settings.Current.Reminders.Count - rem0);
            (j.Duck, Settings.Current.GoogleCalendar) = (duck, google);
            Settings.Save();
        });

        await Step("duck", async () =>   // other apps' volume dips for ~1.5 s, then comes back
        {
            string Vols() => string.Join(", ", Audio.Apps().Where(a => a.Pid != Environment.ProcessId).Select(a => $"{a.Name}={a.Volume * 100:0}%"));
            L($"duck before: {Vols()}");
            Audio.Duck(true, 20);
            await Task.Delay(1500);
            L($"duck ducked: {Vols()}");
            Audio.Duck(false, 20);
            await Task.Delay(300);
            L($"duck after:  {Vols()}");
        });

        await Step("paths", async () =>   // run with MISHKA_PATHS_TEST set; elevated to test the protected folder
        {
            L($"paths elevated={AppPaths.Elevated} data={AppPaths.Data} shared={AppPaths.Shared} notes=[{string.Join("; ", AppPaths.Notes)}]");
            L($"paths settings migrated={File.Exists(Path.Combine(AppPaths.Data, "settings.json"))} model moved={File.Exists(Path.Combine(AppPaths.Shared, "models", "fake.gguf"))}");
            var acl = new DirectoryInfo(AppPaths.Shared).GetAccessControl();
            L($"paths owner={acl.GetOwner(typeof(System.Security.Principal.NTAccount))} protected={acl.AreAccessRulesProtected}");
            foreach (System.Security.AccessControl.FileSystemAccessRule r in acl.GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount)))
                L($"paths   {r.IdentityReference} {r.FileSystemRights} inherited={r.IsInherited}");
            var f = new FileInfo(Path.Combine(AppPaths.Shared, "models", "fake.gguf"));
            if (f.Exists)
                foreach (System.Security.AccessControl.FileSystemAccessRule r in f.GetAccessControl().GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount)))
                    L($"paths   model file: {r.IdentityReference} {r.FileSystemRights} inherited={r.IsInherited}");
            await Task.CompletedTask;
        });

        await Step("remote", async () =>   // no input injection — only status, screen, auth
        {
            var pin = Settings.Current.RemotePin;
            Settings.Current.RemotePin = "432198";
            Remote.StartLocal();
            using var http = new System.Net.Http.HttpClient { BaseAddress = new Uri($"http://localhost:{Settings.Current.RemotePort}") };
            L($"remote page {(await http.GetStringAsync("/")).Length} chars");
            async Task<System.Net.Http.HttpResponseMessage> Post(string p, string pinTry) { var r = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, p); r.Headers.Add("X-Pin", pinTry); return await http.SendAsync(r); }
            L($"remote wrong pin -> {(int)(await Post("/api/status", "0000")).StatusCode}");
            var st = await Post("/api/status", Settings.Current.RemotePin);
            L($"remote status {(int)st.StatusCode}: {await st.Content.ReadAsStringAsync()}");
            var scr = await Post("/api/screen?w=800", Settings.Current.RemotePin);
            L($"remote screen {(int)scr.StatusCode} {scr.Content.Headers.ContentType} {(await scr.Content.ReadAsByteArrayAsync()).Length} bytes");
            var up = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/api/upload?name=" + Uri.EscapeDataString("..\\..\\mishka-test?.txt"));
            up.Headers.Add("X-Pin", Settings.Current.RemotePin); up.Content = new System.Net.Http.StringContent("hello from the phone");
            var ur = await http.SendAsync(up);
            var saved = Directory.Exists(Remote.Inbox) ? Directory.GetFiles(Remote.Inbox, "mishka-test*") : Array.Empty<string>();
            L($"remote upload {(int)ur.StatusCode}: {await ur.Content.ReadAsStringAsync()} -> {string.Join(", ", saved)}");
            foreach (var f in saved) File.Delete(f);
            if (Directory.Exists(Remote.Inbox) && !Directory.EnumerateFileSystemEntries(Remote.Inbox).Any()) Directory.Delete(Remote.Inbox);
            var reb = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "/api/status");
            reb.Headers.Add("X-Pin", Settings.Current.RemotePin); reb.Headers.Host = "evil.example.com";
            L($"remote dns-rebinding host -> {(int)(await http.SendAsync(reb)).StatusCode}");
            for (int i = 0; i < 5; i++) await Post("/api/status", "000000");
            L($"remote after 5 wrong pins, right pin -> {(int)(await Post("/api/status", Settings.Current.RemotePin)).StatusCode} (expect 429)");
            Remote.Stop();
            Settings.Current.RemotePin = pin;

            var btWas = await Radios.Get(Windows.Devices.Radios.RadioKind.Bluetooth);
            var err = await BleRemote.Start();
            L($"ble start (radio was {btWas}): {err ?? "advertising OK"}");
            await Task.Delay(1500);
            BleRemote.Stop();
            if (btWas == false) await Radios.Set(Windows.Devices.Radios.RadioKind.Bluetooth, false);   // leave it as it was
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
                double first = 0;
                var r = await Brain.Ask(q, onSentence: s => { if (first == 0) first = (DateTime.Now - t0).TotalSeconds; L($"   ♪ {s}"); });
                ChatHistory.Add(false, r.Say);
                L($"brain {(DateTime.Now - t0).TotalSeconds:0.0}s (first sentence {first:0.0}s)  Q: {q}  A: {r.Say}{(r.Confirm != null ? " [confirm]" : "")}");
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
        await Step("edgetts", async () =>
        {
            var t0 = DateTime.Now;
            var mp3 = await EdgeTts.Synthesize("Добрый вечер, сэр. Проверка связи.", "ru-RU-DmitryNeural");
            L($"edgetts {mp3.Length} bytes mp3 in {(DateTime.Now - t0).TotalSeconds:0.0}s");
        });

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
