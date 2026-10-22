using System.IO;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace MishkaHelper.Core;

public record DeviceItem(string ContainerId, string Name, string Kind, string Icon, bool Connected, bool Paired,
    int? Battery, bool Bluetooth, bool Disabled, string? Drive, List<string> Nodes)
{
    public string Status => Disabled ? "Отключено" : Connected ? "Подключено" : Paired ? "Не подключено" : "—";
}

/// <summary>
/// Physical peripherals the way Settings → "Bluetooth и устройства" shows them (device containers),
/// with battery, enable/disable, pair/unpair and safe eject.
/// </summary>
public static class Devices
{
    const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    const string ProblemKey = "{4340a6c5-93fa-4706-972c-7b648008a5a7} 3";
    const string ContainerKey = "System.Devices.ContainerId";
    const string InstanceKey = "System.Devices.DeviceInstanceId";

    static readonly (string Prefix, string Kind, string Icon)[] Kinds =
    {
        ("Audio.Headphone", "Наушники", ""),
        ("Audio.Headset", "Гарнитура", ""),
        ("Audio.Speaker", "Колонки", ""),
        ("Audio.Microphone", "Микрофон", ""),
        ("Audio", "Аудио", ""),
        ("Input.Mouse", "Мышь", ""),
        ("Input.Keyboard", "Клавиатура", ""),
        ("Input.Gaming", "Геймпад", ""),
        ("Input.Pen", "Перо / планшет", ""),
        ("Input", "Ввод", ""),
        ("Storage", "Накопитель", ""),
        ("PortableDevice", "Портативное устройство", ""),
        ("Communication.Phone", "Телефон", ""),
        ("Imaging", "Камера", ""),
        ("Display.Monitor", "Монитор", ""),
        ("PrintFax", "Принтер", ""),
    };

    public static async Task<List<DeviceItem>> List()
    {
        var cons = await DeviceInformation.FindAllAsync("",
            new[] { "System.Devices.CategoryIds", "System.Devices.Connected", "System.Devices.LocalMachine", "System.Devices.Paired", "System.Devices.IsDefault" },
            DeviceInformationKind.DeviceContainer);
        var nodes = await DeviceInformation.FindAllAsync("",
            new[] { ContainerKey, InstanceKey, BatteryKey, ProblemKey }, DeviceInformationKind.Device);

        var byContainer = nodes.Where(n => n.Properties.TryGetValue(ContainerKey, out var c) && c != null)
            .GroupBy(n => n.Properties[ContainerKey]!.ToString()!.Trim('{', '}').ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.ToList());

        var drives = UsbDrives();
        var res = new List<DeviceItem>();
        foreach (var c in cons)
        {
            if (Get<bool>(c, "System.Devices.LocalMachine")) continue;
            var cats = c.Properties.TryGetValue("System.Devices.CategoryIds", out var v) && v is string[] a ? a : Array.Empty<string>();
            var kind = Kinds.FirstOrDefault(k => cats.Any(x => x.StartsWith(k.Prefix, StringComparison.OrdinalIgnoreCase)));
            var id = c.Id.Trim('{', '}').ToLowerInvariant();
            byContainer.TryGetValue(id, out var devs);
            devs ??= new();
            var inst = devs.Select(d => d.Properties.TryGetValue(InstanceKey, out var i) ? i as string : null).Where(i => i != null).Cast<string>().ToList();
            bool bt = inst.Any(i => i.StartsWith("BTH", StringComparison.OrdinalIgnoreCase));
            if (kind.Kind == null && !bt) continue;   // only real peripherals, not every system node
            int? battery = devs.Select(d => d.Properties.TryGetValue(BatteryKey, out var b) && b is byte bb ? (int?)bb : null).FirstOrDefault(b => b != null);
            bool disabled = devs.Any(d => d.Properties.TryGetValue(ProblemKey, out var p) && p is uint pc && pc == 22);
            bool connected = Get<bool>(c, "System.Devices.Connected");
            string? drive = kind.Prefix == "Storage" ? drives.FirstOrDefault(dr => c.Name.Contains(dr.Label, StringComparison.OrdinalIgnoreCase) || drives.Count == 1).Root : null;
            res.Add(new DeviceItem(id, c.Name, kind.Kind ?? "Bluetooth", kind.Icon ?? "", connected, Get<bool>(c, "System.Devices.Paired"),
                battery, bt, disabled, drive, inst));
        }
        return res.OrderByDescending(d => d.Connected).ThenBy(d => d.Kind).ThenBy(d => d.Name).ToList();
    }

    static T? Get<T>(DeviceInformation d, string key) => d.Properties.TryGetValue(key, out var v) && v is T t ? t : default;

    static List<(string Root, string Label)> UsbDrives() =>
        DriveInfo.GetDrives().Where(d => d.DriveType == DriveType.Removable && d.IsReady)
            .Select(d => (d.Name, d.VolumeLabel)).ToList();

    /// <summary>Disable/enable the device's bus nodes (USB/BT/HID). Children follow their parent.</summary>
    public static async Task SetEnabled(DeviceItem d, bool on)
    {
        var targets = d.Nodes.Where(n => n.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) || n.StartsWith("BTH", StringComparison.OrdinalIgnoreCase)
                                         || n.StartsWith("HID\\", StringComparison.OrdinalIgnoreCase)).ToList();
        if (targets.Count == 0) targets = d.Nodes;
        foreach (var n in targets)
            await Shell.RunAsync("pnputil", $"/{(on ? "enable" : "disable")}-device \"{n}\"");
    }

    public static void Eject(string root)
    {
        // Same as Explorer's "Eject": the shell flushes caches and notifies apps.
        var t = Type.GetTypeFromProgID("Shell.Application")!;
        dynamic sh = Activator.CreateInstance(t)!;
        sh.NameSpace(17).ParseName(root.TrimEnd('\\')).InvokeVerb("Eject");
    }

    // Bluetooth pairing
    public static async Task<List<DeviceInformation>> DiscoverBluetooth()
    {
        var classic = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(false));
        var le = await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(false));
        return classic.Concat(le).Where(d => !string.IsNullOrWhiteSpace(d.Name)).GroupBy(d => d.Name).Select(g => g.First()).ToList();
    }

    public static async Task<string> Pair(DeviceInformation d)
    {
        d.Pairing.Custom.PairingRequested += (_, a) => a.Accept();
        var r = await d.Pairing.Custom.PairAsync(DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin | DevicePairingKinds.ConfirmPinMatch);
        return r.Status.ToString();
    }

    public static async Task<bool> Unpair(DeviceItem item)
    {
        var sel = BluetoothDevice.GetDeviceSelectorFromPairingState(true);
        foreach (var d in (await DeviceInformation.FindAllAsync(sel)).Concat(await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true))))
        {
            if (d.Name != item.Name) continue;
            var r = await d.Pairing.UnpairAsync();
            return r.Status == DeviceUnpairingResultStatus.Unpaired;
        }
        return false;
    }

    /// <summary>Background check: low battery on any connected device → alert.</summary>
    public static async Task CheckBatteries()
    {
        try
        {
            foreach (var d in (await List()).Where(d => d.Connected && d.Battery is int b && b <= Settings.Current.Alerts.HeadsetBattery))
                Alerts.Fire("bat" + d.ContainerId, d.Icon, $"{d.Name}: {d.Battery}%", "Пора зарядить", 60);
        }
        catch (Exception e) { Log.Error(e); }
    }
}
