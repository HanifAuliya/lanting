using Lanting.App.Shell;
using Lanting.Core.Island;
using Lanting.Core.Modules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;

namespace Lanting.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private ServiceProvider? _services;

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
                StartupLog.ShowMessage("Lanting sudah berjalan.\n\nKlik kanan pil di tengah atas layar untuk keluar.");
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
            window.Activate();
            StartupLog.Write("Window aktif.");

            var modules = _services.GetRequiredService<ModuleHost>();
            modules.ModuleFaulted += (_, e) => StartupLog.Write($"Modul '{e.ModuleId}' error: {e.Exception}");
            await modules.StartAsync(CancellationToken.None);
            StartupLog.Write($"{modules.Started.Count} modul berjalan.");
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
        services.AddSingleton<IslandController>();
        services.AddSingleton<IIslandHost>(sp => sp.GetRequiredService<IslandController>());
        services.AddSingleton<IslandStateMachine>();
        services.AddSingleton<ModuleHost>();

        // Modul fitur didaftarkan di sini mulai milestone 2, contoh:
        // services.AddSingleton<IIslandModule, MediaModule>();

        // UI
        services.AddSingleton<IslandWindow>();

        return services.BuildServiceProvider();
    }

    private async Task ShutdownAsync()
    {
        StartupLog.Write("Keluar.");
        if (_services is not null)
        {
            await _services.GetRequiredService<ModuleHost>().DisposeAsync();
            await _services.DisposeAsync();
            _services = null;
        }

        _singleInstance?.ReleaseMutex();
        _singleInstance?.Dispose();
        Exit();
    }
}
