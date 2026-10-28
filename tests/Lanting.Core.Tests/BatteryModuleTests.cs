using Lanting.Core.Battery;
using Lanting.Core.Island;
using static Lanting.Core.Tests.MediaModuleTests;

namespace Lanting.Core.Tests;

public class BatteryModuleTests
{
    private readonly FakeBatterySource _source = new();
    private readonly RecordingHost _host = new();

    [Fact]
    public async Task Devices_already_connected_at_startup_are_not_announced()
    {
        _source.Initial = [Mouse(80, connected: true)];

        await StartAsync();

        Assert.Empty(_host.Shown);
    }

    [Fact]
    public async Task Low_battery_at_startup_is_still_warned()
    {
        _source.Initial = [Mouse(15, connected: true)];

        await StartAsync();

        Assert.Equal([BatteryNoticeKind.Low], Notices());
    }

    [Fact]
    public async Task Connecting_and_disconnecting_are_announced_briefly()
    {
        _source.Initial = [Mouse(80, connected: false)];
        await StartAsync();

        _source.Raise(Mouse(80, connected: true));
        _source.Raise(Mouse(80, connected: false));

        Assert.Equal([BatteryNoticeKind.Connected, BatteryNoticeKind.Disconnected], Notices());
        Assert.All(_host.Shown, c =>
        {
            Assert.Equal(IslandPriority.Normal, c.Priority);
            Assert.Equal(TimeSpan.FromSeconds(3), c.Duration);
            Assert.False(c.IsNotification);
        });
    }

    [Fact]
    public async Task Low_then_critical_are_each_warned_once()
    {
        _source.Initial = [Mouse(50, connected: true)];
        await StartAsync();

        foreach (var percent in new[] { 25, 20, 19, 18, 10, 9, 5 })
        {
            _source.Raise(Mouse(percent, connected: true));
        }

        Assert.Equal([BatteryNoticeKind.Low, BatteryNoticeKind.Critical], Notices());
        Assert.Equal(IslandPriority.Alert, _host.Shown[0].Priority);
        Assert.Equal(IslandPriority.Critical, _host.Shown[1].Priority);
        Assert.True(_host.Shown[1].IsNotification);
    }

    [Fact]
    public async Task Warnings_return_only_after_charging()
    {
        _source.Initial = [Mouse(50, connected: true)];
        await StartAsync();
        _source.Raise(Mouse(20, connected: true));

        // Naik sedikit lalu turun lagi: tidak diulang.
        _source.Raise(Mouse(25, connected: true));
        _source.Raise(Mouse(20, connected: true));
        Assert.Single(_host.Shown);

        // Sudah diisi sampai 30% ke atas: peringatan boleh muncul lagi.
        _source.Raise(Mouse(60, connected: true));
        _source.Raise(Mouse(20, connected: true));
        Assert.Equal([BatteryNoticeKind.Low, BatteryNoticeKind.Low], Notices());
    }

    [Fact]
    public async Task Disconnected_devices_are_not_warned_about()
    {
        _source.Initial = [Mouse(50, connected: false)];
        await StartAsync();

        _source.Raise(Mouse(5, connected: false));

        Assert.Empty(_host.Shown);
    }

    [Fact]
    public async Task Each_device_uses_its_own_key()
    {
        _source.Initial = [Mouse(50, connected: true), new BatteryDevice("kb", "MX Keys", 50, true)];
        await StartAsync();

        _source.Raise(Mouse(15, connected: true));
        _source.Raise(new BatteryDevice("kb", "MX Keys", 15, true));

        Assert.Equal(["mouse", "kb"], _host.Shown.Select(c => c.Key));
    }

    [Fact]
    public async Task Connection_notices_can_be_turned_off_separately()
    {
        _source.Initial = [Mouse(80, connected: false)];
        await StartAsync(new BatteryOptions { ConnectionNotices = false });

        _source.Raise(Mouse(80, connected: true));
        _source.Raise(Mouse(15, connected: true));

        Assert.Equal([BatteryNoticeKind.Low], Notices());
    }

    [Fact]
    public async Task Device_list_changes_are_reported()
    {
        var module = await StartAsync();
        var changes = 0;
        module.DevicesChanged += (_, _) => changes++;

        _source.Raise(Mouse(70, connected: true));

        Assert.Equal(1, changes);
        Assert.Equal(70, Assert.Single(module.Devices).Percent);
    }

    [Fact]
    public void Device_list_merges_input_devices_with_battery_reports()
    {
        var inputs = new[]
        {
            new InputDevice("dongle", "2.4G Wireless Device", BatteryDeviceKind.Mouse, DeviceConnection.Wireless),
            new InputDevice("bt-kb", "HID Keyboard", BatteryDeviceKind.Keyboard, DeviceConnection.Bluetooth),
        };
        var batteries = new[]
        {
            new BatteryDevice("bt-kb", "MX Keys", 60, true),
            new BatteryDevice("buds", "Galaxy Buds2", 40, false),
        };

        var list = DeviceList.Merge(inputs, batteries);

        Assert.Equal(["MX Keys", "2.4G Wireless Device", "Galaxy Buds2"], list.Select(d => d.Name));
        Assert.Equal([60, null, 40], list.Select(d => d.Percent));
        Assert.Equal([true, true, false], list.Select(d => d.Connected));
        Assert.Equal(DeviceConnection.Bluetooth, list[2].Connection);
    }

    [Theory]
    [InlineData("MX Master 3S", BatteryDeviceKind.Mouse)]
    [InlineData("Logitech Pebble Mouse 2", BatteryDeviceKind.Mouse)]
    [InlineData("MX Keys Mini", BatteryDeviceKind.Keyboard)]
    [InlineData("Keychron K2 Keyboard", BatteryDeviceKind.Keyboard)]
    [InlineData("Galaxy Buds2 Pro", BatteryDeviceKind.Headphones)]
    [InlineData("WH-1000XM5", BatteryDeviceKind.Headphones)]
    [InlineData("Xbox Wireless Controller", BatteryDeviceKind.Gamepad)]
    [InlineData("Speaker Kamar", BatteryDeviceKind.Other)]
    public void Device_kind_is_guessed_from_its_name(string name, BatteryDeviceKind expected)
    {
        Assert.Equal(expected, BatteryDeviceKinds.Guess(name));
    }

    private static BatteryDevice Mouse(int percent, bool connected) => new("mouse", "MX Master 3S", percent, connected);

    private List<BatteryNoticeKind> Notices() => [.. _host.Shown.Select(c => ((BatteryNotice)c.Payload!).Kind)];

    private async Task<BatteryModule> StartAsync(BatteryOptions? options = null)
    {
        var module = new BatteryModule(_source, options ?? new BatteryOptions());
        await module.StartAsync(_host, CancellationToken.None);
        return module;
    }

    private sealed class FakeBatterySource : IBatterySource
    {
        public event EventHandler<BatteryDevice>? Changed;

        public event EventHandler<string>? Removed;

        public BatteryDevice[] Initial { get; set; } = [];

        public void Raise(BatteryDevice device) => Changed?.Invoke(this, device);

        public Task StartAsync(CancellationToken cancellationToken)
        {
            foreach (var device in Initial)
            {
                Raise(device);
            }

            return Task.CompletedTask;
        }

        public Task StopAsync()
        {
            Removed?.Invoke(this, string.Empty);
            return Task.CompletedTask;
        }
    }
}
