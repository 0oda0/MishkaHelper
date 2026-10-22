using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.Json;
using System.Windows;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace MishkaHelper.Core;

/// <summary>
/// Bluetooth remote without pairing: the PC advertises a BLE service, the phone connects from the remote page
/// (Web Bluetooth, https://0oda0.github.io/MishkaHelper/remote/), sends the PIN, then the same commands as the Wi-Fi remote.
/// Writes are "cmd\targ" in UTF-8, prefixed by one byte: 1 = more pieces follow, 0 = last piece.
/// </summary>
public static class BleRemote
{
    public const string PageUrl = "https://0oda0.github.io/MishkaHelper/remote/";
    static readonly Guid Svc = Guid.Parse("6d697368-6b61-4865-6c70-657200000001");
    static readonly Guid Cmd = Guid.Parse("6d697368-6b61-4865-6c70-657200000002");
    static readonly Guid Evt = Guid.Parse("6d697368-6b61-4865-6c70-657200000003");

    static GattServiceProvider? _prov;
    static GattLocalCharacteristic? _evt;
    static readonly HashSet<string> Authed = new();
    static readonly Dictionary<string, List<byte>> Pieces = new();
    static readonly Dictionary<string, (int Fails, DateTime Since)> Fails = new();

    public static bool Running => _prov != null;

    /// <summary>Returns null when advertising, otherwise why it can't.</summary>
    public static async Task<string?> Start()
    {
        Stop();
        var ad = await BluetoothAdapter.GetDefaultAsync();
        if (ad == null) return "Bluetooth-адаптер не найден";
        // The user just asked for a Bluetooth remote — switch the radio on rather than failing.
        if (await Windows.Devices.Radios.Radio.RequestAccessAsync() == Windows.Devices.Radios.RadioAccessStatus.Allowed && await ad.GetRadioAsync() is { State: not Windows.Devices.Radios.RadioState.On } radio)
        {
            await radio.SetStateAsync(Windows.Devices.Radios.RadioState.On);
            await Task.Delay(1500);
        }
        if (!ad.IsLowEnergySupported || !ad.IsPeripheralRoleSupported) return "Этот Bluetooth-адаптер не умеет быть «пультом» (нет режима BLE peripheral)";
        if (Settings.Current.RemotePin.Length < 4) { Settings.Current.RemotePin = Random.Shared.Next(1000, 10000).ToString(); Settings.Save(); }

        var res = await GattServiceProvider.CreateAsync(Svc);
        if (res.Error != BluetoothError.Success) return "Bluetooth: " + res.Error;
        _prov = res.ServiceProvider;

        var cmd = await _prov.Service.CreateCharacteristicAsync(Cmd, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Write | GattCharacteristicProperties.WriteWithoutResponse,
            WriteProtectionLevel = GattProtectionLevel.Plain,   // no pairing; the PIN is checked by us
        });
        cmd.Characteristic.WriteRequested += OnWrite;
        var evt = await _prov.Service.CreateCharacteristicAsync(Evt, new GattLocalCharacteristicParameters
        {
            CharacteristicProperties = GattCharacteristicProperties.Notify,
            ReadProtectionLevel = GattProtectionLevel.Plain,
        });
        _evt = evt.Characteristic;
        _prov.StartAdvertising(new GattServiceProviderAdvertisingParameters { IsConnectable = true, IsDiscoverable = true });
        return null;
    }

    public static void Stop()
    {
        try { _prov?.StopAdvertising(); } catch { }
        _prov = null; _evt = null;
        lock (Authed) { Authed.Clear(); Pieces.Clear(); }
    }

    static async void OnWrite(GattLocalCharacteristic s, GattWriteRequestedEventArgs a)
    {
        string id = a.Session.DeviceId.Id, text;
        using (var def = a.GetDeferral())
        {
            var req = await a.GetRequestAsync();
            if (req == null) return;
            var bytes = req.Value.ToArray();
            if (req.Option == GattWriteOption.WriteWithResponse) req.Respond();
            if (bytes.Length == 0) return;
            lock (Authed)
            {
                if (!Pieces.TryGetValue(id, out var buf)) Pieces[id] = buf = new();
                buf.AddRange(bytes.Skip(1));
                if (bytes[0] == 1 && buf.Count < 16_000) return;   // more pieces coming
                text = Encoding.UTF8.GetString(buf.ToArray());
                Pieces.Remove(id);
            }
        }

        var tab = text.IndexOf('\t');
        string cmd = tab < 0 ? text : text[..tab], arg = tab < 0 ? "" : text[(tab + 1)..];
        try
        {
            if (cmd == "pin")
            {
                // Same guard as Wi-Fi: 5 wrong PINs → that phone is ignored for a minute.
                var f = Fails.GetValueOrDefault(id);
                if (f.Fails >= 5 && DateTime.Now - f.Since < TimeSpan.FromMinutes(1)) return;
                bool ok = System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(arg), Encoding.UTF8.GetBytes(Settings.Current.RemotePin));
                lock (Authed) { if (ok) Authed.Add(id); }
                Fails[id] = ok ? default : (f.Fails + 1, f.Fails == 0 ? DateTime.Now : f.Since);
                await Notify(new { auth = ok });
                return;
            }
            lock (Authed) { if (!Authed.Contains(id)) return; }
            var result = await Application.Current.Dispatcher.InvokeAsync(() => Remote.Do(cmd, arg)).Task.Unwrap();
            if (cmd is "status" or "say" or "shutdown" or "cancelShutdown" || cmd is "volume" or "mute" or "brightness" or "profile") await Notify(result);
        }
        catch (Exception e) { Log.Error(e); }
    }

    // ponytail: one notification per answer; relies on the phone negotiating a big MTU (Chrome does, ~512). Chunk it if a phone shows cut answers.
    static async Task Notify(object o)
    {
        if (_evt == null) return;
        // Relaxed escaping keeps Cyrillic as UTF-8 (2 bytes) instead of \uXXXX (6) — notifications are small.
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
        try { await _evt.NotifyValueAsync(bytes.AsBuffer()); } catch (Exception e) { Log.Error(e); }
    }
}
