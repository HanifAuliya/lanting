using Lanting.Core.Island;
using Lanting.Core.Modules;

namespace Lanting.Core.Battery;

/// <summary>Batas baterai untuk pemberitahuan.</summary>
public sealed record BatteryOptions
{
    /// <summary>Pemberitahuan baterai lemah dan kritis aktif.</summary>
    public bool Enabled { get; init; } = true;

    /// <summary>Pemberitahuan perangkat tersambung dan terputus aktif.</summary>
    public bool ConnectionNotices { get; init; } = true;

    /// <summary>Di bawah atau sama dengan ini: "baterai lemah".</summary>
    public int LowPercent { get; init; } = 20;

    /// <summary>Di bawah atau sama dengan ini: "baterai kritis".</summary>
    public int CriticalPercent { get; init; } = 10;

    /// <summary>Peringatan baru boleh muncul lagi setelah baterai naik ke sini (sudah diisi daya).</summary>
    public int RearmPercent { get; init; } = 30;
}

/// <summary>
/// Memberi tahu lewat island saat keyboard/mouse Bluetooth tersambung atau terputus, dan saat baterainya lemah.
/// Setiap peringatan hanya muncul sekali per "penurunan": baru bisa muncul lagi setelah baterai diisi.
/// </summary>
public sealed class BatteryModule : IIslandModule
{
    public const string ModuleId = "battery";

    private static readonly TimeSpan ConnectionDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan LowDuration = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CriticalDuration = TimeSpan.FromSeconds(8);

    private readonly IBatterySource _source;
    private BatteryOptions _options;
    private readonly Lock _gate = new();
    private readonly Dictionary<string, DeviceState> _devices = [];
    private IIslandHost? _host;
    private bool _starting;

    public BatteryModule(IBatterySource source, BatteryOptions options)
    {
        _source = source;
        _options = options;
    }

    public string Id => ModuleId;

    /// <summary>Pengaturan berubah. Berlaku untuk perubahan baterai berikutnya.</summary>
    public void UpdateOptions(BatteryOptions options)
    {
        lock (_gate)
        {
            _options = options;
        }
    }

    /// <summary>Daftar perangkat (atau baterainya) berubah. Bisa dari thread mana pun.</summary>
    public event EventHandler? DevicesChanged;

    /// <summary>Perangkat yang melaporkan baterai, misalnya untuk halaman Perangkat.</summary>
    public IReadOnlyList<BatteryDevice> Devices
    {
        get
        {
            lock (_gate)
            {
                return [.. _devices.Values.Select(d => d.Device)];
            }
        }
    }

    public async Task StartAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _host = host;
            _starting = true;
        }

        _source.Changed += OnChanged;
        _source.Removed += OnRemoved;
        try
        {
            await _source.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _source.Changed -= OnChanged;
            _source.Removed -= OnRemoved;
            lock (_gate)
            {
                _host = null;
            }

            throw;
        }
        finally
        {
            lock (_gate)
            {
                _starting = false;
            }
        }
    }

    public async Task StopAsync()
    {
        _source.Changed -= OnChanged;
        _source.Removed -= OnRemoved;
        await _source.StopAsync().ConfigureAwait(false);
        lock (_gate)
        {
            _host = null;
            _devices.Clear();
        }
    }

    private void OnChanged(object? sender, BatteryDevice device)
    {
        lock (_gate)
        {
            if (_host is null)
            {
                return;
            }

            _devices.TryGetValue(device.Id, out var state);
            var before = state?.Device;
            state ??= new DeviceState(device);
            state.Device = device;
            _devices[device.Id] = state;

            BatteryNoticeKind? notice = null;

            // Saat Lanting baru dinyalakan, perangkat yang sudah tersambung tidak perlu diumumkan.
            if (!_starting && before is not null && before.Connected != device.Connected)
            {
                notice = device.Connected ? BatteryNoticeKind.Connected : BatteryNoticeKind.Disconnected;
            }

            if (device.Connected && device.Percent is { } percent)
            {
                if (percent >= _options.RearmPercent)
                {
                    state.LowWarned = false;
                    state.CriticalWarned = false;
                }

                if (percent <= _options.CriticalPercent && !state.CriticalWarned)
                {
                    state.CriticalWarned = true;
                    state.LowWarned = true;
                    notice = BatteryNoticeKind.Critical;
                }
                else if (percent <= _options.LowPercent && !state.LowWarned)
                {
                    state.LowWarned = true;
                    notice = BatteryNoticeKind.Low;
                }
            }

            var allowed = notice is BatteryNoticeKind.Connected or BatteryNoticeKind.Disconnected
                ? _options.ConnectionNotices
                : _options.Enabled;
            if (notice is { } kind && allowed)
            {
                _host.Show(Content(new BatteryNotice(device, kind)));
            }
        }

        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnRemoved(object? sender, string id)
    {
        lock (_gate)
        {
            _devices.Remove(id);
        }

        DevicesChanged?.Invoke(this, EventArgs.Empty);
    }

    private static IslandContent Content(BatteryNotice notice)
    {
        var (priority, duration) = notice.Kind switch
        {
            BatteryNoticeKind.Critical => (IslandPriority.Critical, CriticalDuration),
            BatteryNoticeKind.Low => (IslandPriority.Alert, LowDuration),
            _ => (IslandPriority.Normal, ConnectionDuration),
        };

        // Kunci per perangkat: pemberitahuan baru untuk perangkat yang sama menggantikan yang lama.
        return new IslandContent(ModuleId, notice.Device.Id, priority, notice, duration);
    }

    private sealed class DeviceState(BatteryDevice device)
    {
        public BatteryDevice Device { get; set; } = device;

        public bool LowWarned { get; set; }

        public bool CriticalWarned { get; set; }
    }
}
