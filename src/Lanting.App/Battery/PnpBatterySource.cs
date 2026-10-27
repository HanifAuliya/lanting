using Lanting.App.Shell;
using Lanting.Core.Battery;
using Windows.Devices.Enumeration;

namespace Lanting.App.Battery;

/// <summary>
/// Membaca baterai perangkat Bluetooth dari properti perangkat Windows, sumber yang sama dengan halaman
/// "Bluetooth &amp; devices" di Settings. Memakai DeviceWatcher, jadi Windows yang memberi tahu saat
/// baterai atau status sambungan berubah; tidak ada polling.
/// </summary>
internal sealed class PnpBatterySource : IBatterySource
{
    // Kelas perangkat "Bluetooth" (node BTHENUM\DEV_... dan BTHLE\DEV_...).
    private const string BluetoothClass = "System.Devices.ClassGuid:=\"{e0cbf06c-cd8b-4647-bb8a-263b43f0f974}\"";

    // DEVPKEY_Bluetooth_Battery: sisa baterai (byte 0-100).
    private const string BatteryKey = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";

    // Status tersambung perangkat Bluetooth.
    private const string ConnectedKey = "{83DA6326-97A6-4088-9453-A1923F573B29} 15";

    // Satu perangkat fisik bisa punya beberapa node; ContainerId menyatukannya.
    private const string ContainerKey = "System.Devices.ContainerId";

    private readonly Lock _gate = new();
    private readonly Dictionary<string, DeviceInformation> _nodes = [];
    private DeviceWatcher? _watcher;
    private TaskCompletionSource? _enumerated;

    public event EventHandler<BatteryDevice>? Changed;

    public event EventHandler<string>? Removed;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var watcher = DeviceInformation.CreateWatcher(
            BluetoothClass,
            [BatteryKey, ConnectedKey, ContainerKey],
            DeviceInformationKind.Device);
        var enumerated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            _watcher = watcher;
            _enumerated = enumerated;
        }

        watcher.Added += OnAdded;
        watcher.Updated += OnUpdated;
        watcher.Removed += OnRemoved;
        watcher.EnumerationCompleted += OnEnumerationCompleted;
        watcher.Stopped += OnStopped;
        watcher.Start();

        using (cancellationToken.Register(() => enumerated.TrySetCanceled(cancellationToken)))
        {
            await enumerated.Task.ConfigureAwait(false);
        }
    }

    public Task StopAsync()
    {
        DeviceWatcher? watcher;
        lock (_gate)
        {
            watcher = _watcher;
            _watcher = null;
            _nodes.Clear();
        }

        if (watcher is not null)
        {
            watcher.Added -= OnAdded;
            watcher.Updated -= OnUpdated;
            watcher.Removed -= OnRemoved;
            watcher.EnumerationCompleted -= OnEnumerationCompleted;
            watcher.Stopped -= OnStopped;
            if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
            {
                watcher.Stop();
            }
        }

        return Task.CompletedTask;
    }

    private void OnAdded(DeviceWatcher sender, DeviceInformation info)
    {
        lock (_gate)
        {
            _nodes[info.Id] = info;
        }

        Report(info);
    }

    private void OnUpdated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        DeviceInformation? info;
        lock (_gate)
        {
            if (!_nodes.TryGetValue(update.Id, out info))
            {
                return;
            }

            info.Update(update);
        }

        Report(info);
    }

    private void OnRemoved(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        DeviceInformation? info;
        lock (_gate)
        {
            if (!_nodes.Remove(update.Id, out info))
            {
                return;
            }
        }

        if (Battery(info) is not null)
        {
            Removed?.Invoke(this, DeviceId(info));
        }
    }

    private void OnEnumerationCompleted(DeviceWatcher sender, object args)
    {
        int count;
        lock (_gate)
        {
            count = _nodes.Values.Count(n => Battery(n) is not null);
            _enumerated?.TrySetResult();
        }

        StartupLog.Write($"Baterai: {count} perangkat Bluetooth melaporkan baterai.");
    }

    private void OnStopped(DeviceWatcher sender, object args)
    {
        lock (_gate)
        {
            // Berhenti sebelum selesai (misalnya layanan Bluetooth mati): jangan biarkan StartAsync menunggu selamanya.
            _enumerated?.TrySetResult();
        }
    }

    private void Report(DeviceInformation info)
    {
        try
        {
            // Node tanpa baterai (layanan audio, dsb.) diabaikan.
            if (Battery(info) is not { } percent)
            {
                return;
            }

            var connected = info.Properties.TryGetValue(ConnectedKey, out var value) && value is true;
            StartupLog.Write($"Baterai: {info.Name} {percent}% ({(connected ? "tersambung" : "terputus")})");
            Changed?.Invoke(this, new BatteryDevice(DeviceId(info), info.Name, percent, connected));
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Baterai: gagal membaca {info.Name}: {ex.Message}");
        }
    }

    private static int? Battery(DeviceInformation info) =>
        info.Properties.TryGetValue(BatteryKey, out var value) && value is byte percent ? Math.Min((int)percent, 100) : null;

    private static string DeviceId(DeviceInformation info) =>
        info.Properties.TryGetValue(ContainerKey, out var container) && container is Guid id ? id.ToString() : info.Id;
}
