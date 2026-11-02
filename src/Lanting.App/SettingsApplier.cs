using Lanting.App.Shell;
using Lanting.App.Views;
using Lanting.App.Views.Dashboard;
using Lanting.Core.Battery;
using Lanting.Core.Island;
using Lanting.Core.Media;
using Lanting.Core.Messages;
using Lanting.Core.Prayer;
using Lanting.Core.Settings;
using Microsoft.UI.Dispatching;

namespace Lanting.App;

/// <summary>
/// Meneruskan <see cref="LantingSettings"/> ke bagian-bagian yang memakainya, saat awal dan setiap kali berubah,
/// jadi pengaturan langsung berlaku tanpa restart.
/// </summary>
internal sealed class SettingsApplier : IDisposable
{
    private readonly SettingsStore _store;
    private readonly IslandStateMachine _stateMachine;
    private readonly MediaModule _media;
    private readonly MediaView _mediaView;
    private readonly BatteryModule _battery;
    private readonly DashboardView _dashboard;
    private readonly IslandTheme _theme;
    private readonly PrayerModule _prayer;
    private readonly IslandWindow _window;
    private readonly IdleView _idle;
    private readonly MessageModule _messages;
    private readonly DispatcherQueue _dispatcher;
    private FullscreenWatcher? _fullscreen;
    private bool _fullscreenNow;
    private Lanting.Core.Location.GeoLocation? _city;

    public SettingsApplier(
        SettingsStore store,
        IslandStateMachine stateMachine,
        MediaModule media,
        MediaView mediaView,
        BatteryModule battery,
        DashboardView dashboard,
        IslandTheme theme,
        PrayerModule prayer,
        IslandWindow window,
        IdleView idle,
        MessageModule messages)
    {
        _messages = messages;
        _window = window;
        _idle = idle;
        _prayer = prayer;
        _dashboard = dashboard;
        _theme = theme;
        _store = store;
        _stateMachine = stateMachine;
        _media = media;
        _mediaView = mediaView;
        _battery = battery;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _mediaView.LyricsPanelToggled += OnLyricsPanelToggled;
        _store.Changed += OnChanged;
        Apply(store.Current);
    }

    /// <summary>Mulai mengikuti aplikasi layar penuh (butuh window island yang sudah ada).</summary>
    public void Attach(FullscreenWatcher fullscreen)
    {
        _fullscreen = fullscreen;
        _fullscreen.Changed += OnFullscreenChanged;
        _fullscreenNow = FullscreenWatcher.IsFullscreenNow();
        Apply(_store.Current);
    }

    public void Dispose()
    {
        _store.Changed -= OnChanged;
        _mediaView.LyricsPanelToggled -= OnLyricsPanelToggled;
        if (_fullscreen is not null)
        {
            _fullscreen.Changed -= OnFullscreenChanged;
        }
    }

    private void OnChanged(object? sender, LantingSettings settings) => _dispatcher.TryEnqueue(() => Apply(settings));

    private void OnLyricsPanelToggled(object? sender, bool visible) => _store.Update(s => s with { ShowLyricsPanel = visible });
    private void OnFullscreenChanged(object? sender, bool fullscreen)
    {
        _fullscreenNow = fullscreen;
        StartupLog.Write(fullscreen ? "Aplikasi layar penuh terdeteksi." : "Layar penuh selesai.");
        Apply(_store.Current);
    }

    private void Apply(LantingSettings settings)
    {
        _stateMachine.UpdateOptions(new IslandOptions { ShowWhenIdle = settings.ShowWhenIdle });
        _stateMachine.OnFullscreenChanged(settings.HideInFullscreen && _fullscreenNow);
        _media.LyricsEnabled = settings.ShowLyrics;
        _dashboard.SetWeatherEnabled(settings.ShowWeather);
        _theme.SetBackground(settings.Background);
        _window.SetMode(settings.Mode);
        _idle.SetFaceEnabled(settings.ShowFace);
        _mediaView.SetVisualizerEnabled(settings.ShowVisualizer);
        _mediaView.SetLyricsPanelVisible(settings.ShowLyricsPanel);
        _prayer.NotificationsEnabled = settings.PrayerNotifications;
        _messages.UpdateOptions(new MessageOptions { WhatsApp = settings.WhatsAppNotifications, Discord = settings.DiscordNotifications });

        // Kota diganti di Pengaturan: jadwal sholat dihitung ulang untuk kota itu.
        if (settings.City != _city)
        {
            _city = settings.City;
            _prayer.Reschedule();
        }

        var low = settings.LowBatteryPercent;
        _battery.UpdateOptions(new BatteryOptions
        {
            Enabled = settings.BatteryNotifications,
            ConnectionNotices = settings.ConnectionNotifications,
            LowPercent = low,
            CriticalPercent = Math.Min(10, low / 2),
            RearmPercent = low + 10,
        });
    }
}
