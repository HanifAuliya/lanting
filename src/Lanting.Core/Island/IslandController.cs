namespace Lanting.Core.Island;

/// <summary>
/// Menyimpan semua konten yang sedang diminta modul dan memilih satu yang tampil:
/// prioritas tertinggi menang, dan bila sama, yang paling baru menang.
/// Konten dengan durasi otomatis hilang setelah waktunya habis.
/// </summary>
public sealed class IslandController : IIslandHost, IDisposable
{
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Dictionary<(string ModuleId, string Key), Entry> _entries = [];
    private long _sequence;
    private IslandContent? _current;

    public IslandController(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>
    /// Dipicu setiap kali konten yang tampil berganti (termasuk menjadi <c>null</c>).
    /// Bisa dipicu dari thread mana pun; UI wajib pindah ke thread-nya sendiri.
    /// </summary>
    public event EventHandler<IslandContent?>? CurrentChanged;

    public IslandContent? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    public void Show(IslandContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var id = (content.ModuleId, content.Key);

        bool changed;
        IslandContent? current;
        lock (_gate)
        {
            if (_entries.Remove(id, out var previous))
            {
                previous.Timer?.Dispose();
            }

            var sequence = ++_sequence;
            ITimer? timer = content.Duration is { } duration
                ? _time.CreateTimer(_ => Expire(id, sequence), null, duration, Timeout.InfiniteTimeSpan)
                : null;

            _entries[id] = new Entry(content, sequence, timer);
            changed = Recompute(out current);
        }

        if (changed)
        {
            CurrentChanged?.Invoke(this, current);
        }
    }

    public void Withdraw(string moduleId, string key)
    {
        bool changed;
        IslandContent? current;
        lock (_gate)
        {
            if (!_entries.Remove((moduleId, key), out var entry))
            {
                return;
            }

            entry.Timer?.Dispose();
            changed = Recompute(out current);
        }

        if (changed)
        {
            CurrentChanged?.Invoke(this, current);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            foreach (var entry in _entries.Values)
            {
                entry.Timer?.Dispose();
            }

            _entries.Clear();
            _current = null;
        }
    }

    private void Expire((string ModuleId, string Key) id, long sequence)
    {
        bool changed;
        IslandContent? current;
        lock (_gate)
        {
            // Konten bisa sudah diganti (Show ulang) sebelum timer lama berbunyi.
            if (!_entries.TryGetValue(id, out var entry) || entry.Sequence != sequence)
            {
                return;
            }

            _entries.Remove(id);
            entry.Timer?.Dispose();
            changed = Recompute(out current);
        }

        if (changed)
        {
            CurrentChanged?.Invoke(this, current);
        }
    }

    // Dipanggil di dalam _gate.
    private bool Recompute(out IslandContent? current)
    {
        Entry? best = null;
        foreach (var entry in _entries.Values)
        {
            if (best is null
                || entry.Content.Priority > best.Content.Priority
                || (entry.Content.Priority == best.Content.Priority && entry.Sequence > best.Sequence))
            {
                best = entry;
            }
        }

        current = best?.Content;
        if (ReferenceEquals(current, _current))
        {
            return false;
        }

        _current = current;
        return true;
    }

    private sealed record Entry(IslandContent Content, long Sequence, ITimer? Timer);
}
