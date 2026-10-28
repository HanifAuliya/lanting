namespace Lanting.Core.Battery;

/// <summary>Cara perangkat tersambung ke PC.</summary>
public enum DeviceConnection
{
    Bluetooth,

    /// <summary>Lewat dongle USB (biasanya 2.4 GHz).</summary>
    Wireless,

    Cable,
}

/// <summary>Keyboard, mouse, atau perangkat input lain yang sedang tersambung (tanpa info baterai).</summary>
/// <param name="Id">Id perangkat fisik (ContainerId Windows), sama dengan <see cref="BatteryDevice.Id"/> untuk perangkat Bluetooth.</param>
public sealed record InputDevice(string Id, string Name, BatteryDeviceKind Kind, DeviceConnection Connection);

/// <summary>Satu baris di halaman Perangkat.</summary>
/// <param name="Percent">Sisa baterai, atau <c>null</c> bila perangkat tidak melaporkannya ke Windows.</param>
public sealed record DeviceEntry(string Id, string Name, BatteryDeviceKind Kind, DeviceConnection Connection, int? Percent, bool Connected);

public static class DeviceList
{
    /// <summary>
    /// Gabungkan perangkat input yang tersambung dengan perangkat yang melaporkan baterai (Bluetooth).
    /// Perangkat yang sama (Id sama) jadi satu baris. Urutan: tersambung dulu, lalu yang punya info baterai, lalu nama.
    /// </summary>
    public static IReadOnlyList<DeviceEntry> Merge(IEnumerable<InputDevice> inputs, IEnumerable<BatteryDevice> batteries)
    {
        var byId = batteries.GroupBy(b => b.Id).ToDictionary(g => g.Key, g => g.Last());
        var entries = new List<DeviceEntry>();
        foreach (var input in inputs.DistinctBy(i => i.Id))
        {
            byId.Remove(input.Id, out var battery);
            var kind = input.Kind != BatteryDeviceKind.Other ? input.Kind : battery?.Kind ?? BatteryDeviceKind.Other;
            entries.Add(new DeviceEntry(input.Id, battery?.Name ?? input.Name, kind, input.Connection, battery?.Percent, Connected: true));
        }

        // Perangkat Bluetooth yang bukan input (misalnya headset), atau sedang terputus.
        foreach (var battery in byId.Values)
        {
            entries.Add(new DeviceEntry(battery.Id, battery.Name, battery.Kind, DeviceConnection.Bluetooth, battery.Percent, battery.Connected));
        }

        return
        [
            .. entries
                .OrderByDescending(e => e.Connected)
                .ThenByDescending(e => e.Percent is not null)
                .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase),
        ];
    }
}
