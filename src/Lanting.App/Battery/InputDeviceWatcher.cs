using Lanting.App.Shell;
using Lanting.Core.Battery;
using Windows.Devices.Enumeration;

namespace Lanting.App.Battery;

/// <summary>
/// Keyboard dan mouse eksternal yang sedang tersambung (kabel, dongle, atau Bluetooth), untuk halaman Perangkat.
/// Keyboard dan touchpad bawaan laptop tidak ikut. Memakai DeviceWatcher, jadi Windows yang memberi tahu saat
/// perangkat dicolok atau dicabut. Baru mulai saat <see cref="Start"/> dipanggil (halaman Perangkat pertama kali dibuka).
/// </summary>
internal sealed class InputDeviceWatcher
{
    private const string KeyboardClass = "{4d36e96b-e325-11ce-bfc1-08002be10318}";
    private const string MouseClass = "{4d36e96f-e325-11ce-bfc1-08002be10318}";
    private const string ContainerKey = "System.Devices.ContainerId";
    private const string ClassKey = "System.Devices.ClassGuid";

    // Container "PC ini": semua perangkat bawaan laptop (keyboard, touchpad) ada di sini.
    private static readonly Guid LocalMachine = new("00000000-0000-0000-ffff-ffffffffffff");

    // Layanan HID Bluetooth: klasik dan Low Energy (HID over GATT).
    private static readonly string[] BluetoothHints = ["{00001124-", "{00001812-", "BTHENUM", "BTHLE"];

    // Nama dongle umumnya menyebut "wireless", "2.4G", atau nama receiver-nya.
    private static readonly string[] WirelessHints = ["wireless", "2.4g", "2,4g", "receiver", "dongle", "unifying", "lightspeed", "bolt"];

    private readonly Lock _gate = new();
    private readonly Dictionary<string, Node> _nodes = [];
    private readonly Dictionary<Guid, string> _containerNames = [];
    private DeviceWatcher? _watcher;

    /// <summary>Daftar perangkat berubah. Bisa dari thread mana pun.</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<InputDevice> Devices
    {
        get
        {
            lock (_gate)
            {
                return
                [
                    .. _nodes.Values
                        .GroupBy(n => n.Container)
                        .Select(g => Describe(g.Key, g.ToList())),
                ];
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_watcher is not null)
            {
                return;
            }

            _watcher = DeviceInformation.CreateWatcher(
                $"({ClassKey}:=\"{KeyboardClass}\" OR {ClassKey}:=\"{MouseClass}\") AND System.Devices.Present:=System.StructuredQueryType.Boolean#True",
                [ContainerKey, ClassKey],
                DeviceInformationKind.Device);
        }

        _watcher.Added += OnAdded;
        _watcher.Updated += (_, _) => { };
        _watcher.Removed += OnRemoved;
        _watcher.Start();
    }

    public void Stop()
    {
        DeviceWatcher? watcher;
        lock (_gate)
        {
            watcher = _watcher;
            _watcher = null;
            _nodes.Clear();
        }

        if (watcher?.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            watcher.Stop();
        }
    }

    private async void OnAdded(DeviceWatcher sender, DeviceInformation info)
    {
        try
        {
            if (!info.Properties.TryGetValue(ContainerKey, out var value) || value is not Guid container || container == LocalMachine)
            {
                return;
            }

            var mouse = info.Properties.TryGetValue(ClassKey, out var cls) && cls is Guid guid && guid == new Guid(MouseClass);
            var name = await ContainerNameAsync(container);
            lock (_gate)
            {
                _nodes[info.Id] = new Node(container, name, info.Id, mouse);
            }

            Changed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Perangkat: gagal membaca {info.Name}: {ex.Message}");
        }
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        bool removed;
        lock (_gate)
        {
            removed = _nodes.Remove(update.Id);
        }

        if (removed)
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    // Nama perangkat fisik (seperti di "Bluetooth & devices"), bukan nama driver seperti "HID Keyboard Device".
    private async Task<string> ContainerNameAsync(Guid container)
    {
        lock (_gate)
        {
            if (_containerNames.TryGetValue(container, out var cached))
            {
                return cached;
            }
        }

        var name = "Perangkat USB";
        try
        {
            var info = await DeviceInformation.CreateFromIdAsync($"{{{container}}}", [], DeviceInformationKind.DeviceContainer);
            if (!string.IsNullOrWhiteSpace(info.Name))
            {
                name = info.Name;
            }
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Perangkat: nama {container} tidak terbaca: {ex.Message}");
        }

        lock (_gate)
        {
            _containerNames[container] = name;
        }

        return name;
    }

    private static InputDevice Describe(Guid container, List<Node> nodes)
    {
        var name = nodes[0].Name;
        var kind = BatteryDeviceKinds.Guess(name);
        if (kind == BatteryDeviceKind.Other)
        {
            // Dongle mouse sering juga punya "keyboard" (untuk tombol makro), jadi mouse didahulukan.
            kind = nodes.Any(n => n.Mouse) ? BatteryDeviceKind.Mouse : BatteryDeviceKind.Keyboard;
        }

        var connection =
            nodes.Any(n => BluetoothHints.Any(h => n.InstanceId.Contains(h, StringComparison.OrdinalIgnoreCase))) ? DeviceConnection.Bluetooth
            : WirelessHints.Any(h => name.Contains(h, StringComparison.OrdinalIgnoreCase)) ? DeviceConnection.Wireless
            : DeviceConnection.Cable;

        return new InputDevice(container.ToString(), name, kind, connection);
    }

    private sealed record Node(Guid Container, string Name, string InstanceId, bool Mouse);
}
