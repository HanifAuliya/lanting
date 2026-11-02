using Lanting.App.Battery;
using Lanting.App.Face;
using Lanting.App.Media;
using Lanting.App.Shell;
using Lanting.App.Views;
using Lanting.App.Views.Dashboard;
using Lanting.Core.Battery;
using Lanting.Core.Face;
using Lanting.Core.Island;
using Lanting.Core.Location;
using Lanting.Core.Lyrics;
using Lanting.Core.Media;
using Lanting.Core.Modules;
using Lanting.Core.Prayer;
using Lanting.Core.Settings;
using Lanting.Core.Weather;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using WinRT.Interop;

namespace Lanting.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private ServiceProvider? _services;
    private FullscreenWatcher? _fullscreen;
    private SettingsWindow? _settingsWindow;

    public App()
    {
        StartupLog.Write($"Lanting mulai (versi {typeof(App).Assembly.GetName().Version}, {Environment.OSVersion})");

        UnhandledException += (_, e) => StartupLog.Fatal("UI", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => StartupLog.Fatal("AppDomain", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => StartupLog.Write($"Task error: {e.Exception}");

        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            // Cukup satu island: kalau Lanting sudah jalan, beri tahu lalu keluar.
            _singleInstance = new Mutex(initiallyOwned: true, @"Local\Lanting.SingleInstance", out var isFirst);
            if (!isFirst)
            {
                StartupLog.Write("Instance lain sudah berjalan, keluar.");
                StartupLog.ShowMessage("Lanting sudah berjalan.\n\nKlik ikon Lanting di system tray, atau klik kanan pil di tengah atas layar, untuk pengaturan atau keluar.");
                Exit();
                return;
            }

            if (XamlDiagnostics.IsEnabled)
            {
                XamlDiagnostics.Run();
            }

            _services = ConfigureServices();

            var window = _services.GetRequiredService<IslandWindow>();
            window.ExitRequested += async (_, _) => await ShutdownAsync();
            window.SettingsRequested += (_, _) => OpenSettings();
            window.Activate();
            StartupLog.Write("Window aktif.");

            var tray = _services.GetRequiredService<TrayMenu>();
            tray.ExitRequested += async (_, _) => await ShutdownAsync();
            tray.SettingsRequested += (_, _) => OpenSettings();

            _services.GetRequiredService<FaceSignals>().Start();

            _fullscreen = new FullscreenWatcher(window, WindowNative.GetWindowHandle(window));
            _services.GetRequiredService<SettingsApplier>().Attach(_fullscreen);

            _services.GetRequiredService<MediaModule>().LyricsFailed += (_, e) => StartupLog.Write($"Lirik gagal dicari: {e.Message}");

            var modules = _services.GetRequiredService<ModuleHost>();
            modules.ModuleFaulted += (_, e) => StartupLog.Write($"Modul '{e.ModuleId}' error: {e.Exception}");
            await modules.StartAsync(CancellationToken.None);
            StartupLog.Write($"{modules.Started.Count} modul berjalan.");

            // Smoke test CI: buka Pengaturan juga, supaya error yang hanya muncul di build Release ikut ketahuan.
            // Smoke test CI: buka dashboard juga (seolah di-hover), supaya error di sana ikut ketahuan dan terlihat di screenshot.
            if (Environment.GetEnvironmentVariable("LANTING_SMOKE_EXPANDED") == "1")
            {
                _services.GetRequiredService<IslandStateMachine>().OnPointerEntered();
                StartupLog.Write("Dashboard dibuka (smoke test).");
            }

            if (Environment.GetEnvironmentVariable("LANTING_SMOKE_SETTINGS") == "1")
            {
                OpenSettings();
                StartupLog.Write("Pengaturan dibuka (smoke test).");
            }

            // Tes: keluar sendiri setelah sebentar, seperti memilih "Keluar dari Lanting", supaya error saat keluar ketahuan.
            if (Environment.GetEnvironmentVariable("LANTING_SMOKE_EXIT") == "1")
            {
                var timer = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread().CreateTimer();
                timer.Interval = TimeSpan.FromSeconds(3);
                timer.IsRepeating = false;
                timer.Tick += async (_, _) => await ShutdownAsync();
                timer.Start();
            }

            // Tes: tampilkan notifikasi waktu sholat tanpa menunggu waktunya.
            if (Environment.GetEnvironmentVariable("LANTING_SMOKE_PRAYER") == "1"
                && _services.GetRequiredService<PrayerModule>().Today() is { } today)
            {
                _services.GetRequiredService<IIslandHost>().Show(new IslandContent(
                    PrayerModule.ModuleId, "time", IslandPriority.Alert, new PrayerNotice(today.Next, today.Location.Place), TimeSpan.FromSeconds(8)));
                StartupLog.Write("Notifikasi sholat ditampilkan (smoke test).");
            }
        }
        catch (Exception ex)
        {
            StartupLog.Fatal("OnLaunched", ex);
            Exit();
        }
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // Core
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(new IslandOptions());
        services.AddSingleton(new SettingsStore(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lanting", "settings.json")));
        services.AddSingleton<IslandController>();
        services.AddSingleton<IIslandHost>(sp => sp.GetRequiredService<IslandController>());
        services.AddSingleton<IslandStateMachine>();
        services.AddSingleton<ModuleHost>();

        // Warna island (tema Windows + pengaturan latar), dipakai semua view.
        services.AddSingleton<IslandTheme>();

        // Modul fitur: modul di Core + tampilannya (IIslandView) di App.
        services.AddSingleton<SmtcMediaSource>();
        services.AddSingleton<IMediaSource>(sp => sp.GetRequiredService<SmtcMediaSource>());
        services.AddSingleton<IMediaControls>(sp => sp.GetRequiredService<SmtcMediaSource>());
        services.AddSingleton<ILyricsProvider>(_ => new FallbackLyricsProvider(
            new LrcLibClient(new HttpClient
            {
                BaseAddress = new Uri("https://lrclib.net/"),
                Timeout = TimeSpan.FromSeconds(10),
                DefaultRequestHeaders =
                {
                    // LRCLIB meminta aplikasi memperkenalkan diri lewat User-Agent.
                    { "User-Agent", $"Lanting/{typeof(App).Assembly.GetName().Version?.ToString(3)} (https://github.com/HanifAuliya/lanting)" },
                },
            }),
            new NetEaseClient(new HttpClient
            {
                BaseAddress = new Uri("https://music.163.com/"),
                Timeout = TimeSpan.FromSeconds(10),
                DefaultRequestHeaders =
                {
                    {"Referer", "https://music.163.com/"},
                },
            })));
        services.AddSingleton<MediaModule>();
        // Tes wajah (LANTING_SMOKE_FACE): lagu yang sedang diputar tidak ditampilkan supaya wajah terlihat.
        if (Environment.GetEnvironmentVariable("LANTING_SMOKE_FACE") is null)
        {
            services.AddSingleton<IIslandModule>(sp => sp.GetRequiredService<MediaModule>());
        }
        services.AddSingleton<MediaView>();
        services.AddSingleton<IIslandView>(sp => sp.GetRequiredService<MediaView>());

        services.AddSingleton(new BatteryOptions());
        services.AddSingleton<IBatterySource, PnpBatterySource>();
        services.AddSingleton<BatteryModule>();
        services.AddSingleton<IIslandModule>(sp => sp.GetRequiredService<BatteryModule>());
        services.AddSingleton<IIslandView, BatteryView>();
        services.AddSingleton<InputDeviceWatcher>();

        // Lokasi (kota pilihan atau dari alamat internet), dipakai cuaca dan jadwal sholat.
        services.AddSingleton(new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
            DefaultRequestHeaders = { { "User-Agent", $"Lanting/{typeof(App).Assembly.GetName().Version?.ToString(3)} (https://github.com/HanifAuliya/lanting)" } },
        });
        services.AddSingleton(sp => new LocationService(
            sp.GetRequiredService<HttpClient>(), sp.GetRequiredService<TimeProvider>(), sp.GetRequiredService<SettingsStore>()));

        services.AddSingleton<PrayerModule>();
        services.AddSingleton<IIslandModule>(sp => sp.GetRequiredService<PrayerModule>());
        services.AddSingleton<IIslandView, PrayerView>();

        // Dashboard (isi island saat dibuka).
        services.AddSingleton<WeatherService>();
        services.AddSingleton<ClockTicker>();
        services.AddSingleton<FaceController>();
        services.AddSingleton<FaceSignals>();
        services.AddSingleton<FaceView>();
        services.AddSingleton<IdleView>();
        services.AddSingleton<DashboardView>();

        // UI
        services.AddSingleton<IslandWindow>();
        services.AddSingleton<TrayMenu>();
        services.AddSingleton<SettingsApplier>();

        return services.BuildServiceProvider();
    }

    private void OpenSettings()
    {
        if (_services is null)
        {
            return;
        }

        if (_settingsWindow is null)
        {
            _settingsWindow = new SettingsWindow(_services.GetRequiredService<SettingsStore>(), _services.GetRequiredService<LocationService>());
            _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        }

        _settingsWindow.Activate();
    }

    private async Task ShutdownAsync()
    {
        StartupLog.Write("Keluar.");
        _settingsWindow?.Close();
        _fullscreen?.Dispose();
        _fullscreen = null;
        if (_services is not null)
        {
            // Jendela island ditutup dulu dengan rapi. Tanpa ini, Exit() membongkar jendela yang timer dan ikon tray-nya
            // sudah dilepas, lalu crash di CoreMessagingXP (0xC000027B, "invalid window handle").
            _services.GetRequiredService<IslandWindow>().Close();
            await _services.GetRequiredService<ModuleHost>().DisposeAsync();
            await _services.DisposeAsync();
            _services = null;
        }

        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        Exit();
    }
}
