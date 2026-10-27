namespace Lanting.Core.Battery;

/// <summary>Perangkat Bluetooth yang melaporkan baterai, misalnya keyboard atau mouse.</summary>
/// <param name="Id">Id stabil perangkat (satu per perangkat fisik).</param>
/// <param name="Name">Nama yang tampil di Settings Windows.</param>
/// <param name="Percent">Sisa baterai 0-100, atau <c>null</c> bila belum dilaporkan.</param>
/// <param name="Connected">Sedang terhubung.</param>
public sealed record BatteryDevice(string Id, string Name, int? Percent, bool Connected)
{
    public BatteryDeviceKind Kind => BatteryDeviceKinds.Guess(Name);
}

public enum BatteryDeviceKind
{
    Other,
    Keyboard,
    Mouse,
    Headphones,
    Gamepad,
}

/// <summary>Menebak jenis perangkat dari namanya, hanya untuk memilih ikon.</summary>
public static class BatteryDeviceKinds
{
    private static readonly (string Fragment, BatteryDeviceKind Kind)[] Hints =
    [
        ("keyboard", BatteryDeviceKind.Keyboard),
        ("keys", BatteryDeviceKind.Keyboard),
        ("keeb", BatteryDeviceKind.Keyboard),
        ("mouse", BatteryDeviceKind.Mouse),
        ("mx master", BatteryDeviceKind.Mouse),
        ("mx anywhere", BatteryDeviceKind.Mouse),
        ("trackball", BatteryDeviceKind.Mouse),
        ("trackpad", BatteryDeviceKind.Mouse),
        ("buds", BatteryDeviceKind.Headphones),
        ("airpods", BatteryDeviceKind.Headphones),
        ("headphone", BatteryDeviceKind.Headphones),
        ("headset", BatteryDeviceKind.Headphones),
        ("earphone", BatteryDeviceKind.Headphones),
        ("wh-", BatteryDeviceKind.Headphones),
        ("wf-", BatteryDeviceKind.Headphones),
        ("controller", BatteryDeviceKind.Gamepad),
        ("gamepad", BatteryDeviceKind.Gamepad),
        ("dualsense", BatteryDeviceKind.Gamepad),
    ];

    public static BatteryDeviceKind Guess(string name)
    {
        foreach (var (fragment, kind) in Hints)
        {
            if (name.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                return kind;
            }
        }

        return BatteryDeviceKind.Other;
    }
}

/// <summary>Jenis pemberitahuan baterai.</summary>
public enum BatteryNoticeKind
{
    Connected,
    Disconnected,
    Low,
    Critical,
}

/// <summary>Payload konten island untuk modul baterai.</summary>
public sealed record BatteryNotice(BatteryDevice Device, BatteryNoticeKind Kind);

/// <summary>Sumber info baterai (di aplikasi: DeviceWatcher Windows).</summary>
public interface IBatterySource
{
    /// <summary>Perangkat ditemukan atau baterai/status sambungannya berubah. Bisa dari thread mana pun.</summary>
    event EventHandler<BatteryDevice>? Changed;

    /// <summary>Perangkat dilepas (unpair). Argumen: <see cref="BatteryDevice.Id"/>.</summary>
    event EventHandler<string>? Removed;

    /// <summary>Selesai setelah daftar perangkat awal terkirim lewat <see cref="Changed"/>.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();
}
