using Lanting.Core.Island;

namespace Lanting.Core.Modules;

/// <summary>
/// Menyalakan dan mematikan semua modul. Modul yang error diisolasi:
/// dicatat lewat <see cref="ModuleFaulted"/>, lalu modul lain tetap jalan.
/// </summary>
public sealed class ModuleHost : IAsyncDisposable
{
    private readonly IReadOnlyList<IIslandModule> _modules;
    private readonly IIslandHost _host;
    private readonly List<IIslandModule> _started = [];

    public ModuleHost(IEnumerable<IIslandModule> modules, IIslandHost host)
    {
        _modules = modules.ToList();
        _host = host;
    }

    public event EventHandler<ModuleFaultedEventArgs>? ModuleFaulted;

    public IReadOnlyList<IIslandModule> Started => _started;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        foreach (var module in _modules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await module.StartAsync(_host, cancellationToken).ConfigureAwait(false);
                _started.Add(module);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                ModuleFaulted?.Invoke(this, new ModuleFaultedEventArgs(module.Id, ex));
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        // Matikan dengan urutan terbalik dari saat dinyalakan.
        for (var i = _started.Count - 1; i >= 0; i--)
        {
            var module = _started[i];
            try
            {
                await module.StopAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                ModuleFaulted?.Invoke(this, new ModuleFaultedEventArgs(module.Id, ex));
            }
        }

        _started.Clear();
    }
}

public sealed record ModuleFaultedEventArgs(string ModuleId, Exception Exception);
