using Windows.Devices.Radios;

namespace MishkaHelper.Core;

/// <summary>Wi-Fi / Bluetooth radios and airplane mode (all radios off).</summary>
public static class Radios
{
    static IReadOnlyList<Radio>? _cache;

    static async Task<IReadOnlyList<Radio>> All()
    {
        if (_cache != null) return _cache;
        await Radio.RequestAccessAsync();
        return _cache = await Radio.GetRadiosAsync();
    }

    public static async Task<bool?> Get(RadioKind kind)
    {
        var r = (await All()).FirstOrDefault(x => x.Kind == kind);
        return r == null ? null : r.State == RadioState.On;
    }

    public static async Task Set(RadioKind kind, bool on)
    {
        foreach (var r in (await All()).Where(x => x.Kind == kind))
            await r.SetStateAsync(on ? RadioState.On : RadioState.Off);
    }

    public static async Task<bool> Airplane() => (await All()).All(r => r.State != RadioState.On);

    public static async Task SetAirplane(bool on)
    {
        foreach (var r in await All()) await r.SetStateAsync(on ? RadioState.Off : RadioState.On);
    }
}
