using Lanting.Core.Battery;
using Lanting.Core.Island;
using Lanting.Core.Lyrics;
using Lanting.Core.Media;
using Lanting.Core.Settings;
using Microsoft.Extensions.Time.Testing;
using static Lanting.Core.Tests.MediaModuleTests;

namespace Lanting.Core.Tests;

public sealed class SettingsTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "lanting-tests", Guid.NewGuid().ToString("N"));

    private string SettingsPath => Path.Combine(_folder, "settings.json");

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var store = new SettingsStore(SettingsPath);

        Assert.Equal(new LantingSettings(), store.Current);
    }

    [Fact]
    public void Changes_are_saved_and_loaded_again()
    {
        var store = new SettingsStore(SettingsPath);
        LantingSettings? notified = null;
        store.Changed += (_, s) => notified = s;

        Assert.True(store.Update(s => s with { ShowLyrics = false, LowBatteryPercent = 15 }));

        Assert.False(notified?.ShowLyrics);
        var reloaded = new SettingsStore(SettingsPath).Current;
        Assert.False(reloaded.ShowLyrics);
        Assert.Equal(15, reloaded.LowBatteryPercent);
    }

    [Fact]
    public void Corrupt_file_falls_back_to_defaults()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, "{ ini bukan json");

        Assert.Equal(new LantingSettings(), new SettingsStore(SettingsPath).Current);
    }

    [Fact]
    public void Unknown_fields_are_ignored_and_bad_values_clamped()
    {
        Directory.CreateDirectory(_folder);
        File.WriteAllText(SettingsPath, """{ "ShowVisualizer": false, "FiturLama": 1, "LowBatteryPercent": 400 }""");

        var settings = new SettingsStore(SettingsPath).Current;

        Assert.False(settings.ShowVisualizer);
        Assert.Equal(50, settings.LowBatteryPercent);
    }

    [Fact]
    public void Update_without_change_does_not_notify()
    {
        var store = new SettingsStore(SettingsPath);
        var count = 0;
        store.Changed += (_, _) => count++;

        store.Update(s => s with { ShowWhenIdle = true });

        Assert.Equal(0, count);
    }

    [Fact]
    public void Turning_off_the_idle_pill_hides_it_immediately()
    {
        using var machine = new IslandStateMachine(new IslandOptions(), new FakeTimeProvider());

        machine.UpdateOptions(new IslandOptions { ShowWhenIdle = false });

        Assert.Equal(IslandState.Hidden, machine.State);
    }

    [Fact]
    public async Task Turning_off_lyrics_hides_them_and_stops_searching()
    {
        var time = new FakeTimeProvider();
        var source = new FakeSource();
        var host = new RecordingHost();
        var lyrics = new CountingLyrics();
        var module = new MediaModule(source, lyrics, time);
        await module.StartAsync(host, CancellationToken.None);

        module.LyricsEnabled = false;
        source.Raise(Song("Spotify.exe", "Lagu A"));
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(0, lyrics.Searches);

        module.LyricsEnabled = true;
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(1, lyrics.Searches);
    }

    [Fact]
    public async Task Disabled_battery_notifications_stay_quiet()
    {
        var source = new BatteryFakeSource();
        var host = new RecordingHost();
        var module = new BatteryModule(source, new BatteryOptions());
        await module.StartAsync(host, CancellationToken.None);
        source.Raise(new BatteryDevice("m", "Mouse", 50, true));

        module.UpdateOptions(new BatteryOptions { Enabled = false });
        source.Raise(new BatteryDevice("m", "Mouse", 5, true));

        Assert.Empty(host.Shown);
    }

    private sealed class CountingLyrics : ILyricsProvider
    {
        public int Searches { get; private set; }

        public Task<SyncedLyrics?> FindAsync(TrackQuery query, CancellationToken cancellationToken)
        {
            Searches++;
            return Task.FromResult<SyncedLyrics?>(null);
        }
    }

    private sealed class BatteryFakeSource : IBatterySource
    {
        public event EventHandler<BatteryDevice>? Changed;

        public event EventHandler<string>? Removed;

        public void Raise(BatteryDevice device) => Changed?.Invoke(this, device);

        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task StopAsync()
        {
            Removed?.Invoke(this, string.Empty);
            return Task.CompletedTask;
        }
    }
}
