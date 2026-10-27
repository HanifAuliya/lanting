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
        InitializeComponent();
    }

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        // Cukup satu island: kalau Lanting sudah jalan, instance kedua langsung keluar.
        _singleInstance = new Mutex(initiallyOwned: true, @"Local\Lanting.SingleInstance", out var isFirst);
        if (!isFirst)
        {
            Exit();
            return;
        }

        _services = ConfigureServices();

        var window = _services.GetRequiredService<IslandWindow>();
        window.ExitRequested += async (_, _) => await ShutdownAsync();
        window.Activate();

        await _services.GetRequiredService<ModuleHost>().StartAsync(CancellationToken.None);
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
