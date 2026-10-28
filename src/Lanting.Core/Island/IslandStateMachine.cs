namespace Lanting.Core.Island;

/// <summary>
/// Menerjemahkan kejadian (konten berganti, mouse masuk/keluar, app fullscreen) menjadi
/// <see cref="IslandState"/>. Tidak tahu apa-apa soal UI, jadi bisa dites tanpa Windows.
/// </summary>
public sealed class IslandStateMachine : IDisposable
{
    private IslandOptions _options;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private IslandContent? _content;
    private bool _pointerOver;
    private bool _suppressed;
    private ITimer? _collapseTimer;
    private ITimer? _expandTimer;
    private IslandState _state;

    public IslandStateMachine(IslandOptions options, TimeProvider time)
    {
        _options = options;
        _time = time;
        _state = Compute();
    }

    /// <summary>Dipicu dari thread mana pun saat state berubah.</summary>
    public event EventHandler<IslandState>? StateChanged;

    public IslandState State
    {
        get
        {
            lock (_gate)
            {
                return _state;
            }
        }
    }

    public IslandContent? Content
    {
        get
        {
            lock (_gate)
            {
                return _content;
            }
        }
    }

    public void OnContentChanged(IslandContent? content) => Update(() => _content = content);

    /// <summary>
    /// Mouse masuk ke pil. Pil baru terbuka setelah mouse diam di sana selama <see cref="IslandOptions.ExpandDelay"/>,
    /// supaya tidak terbuka saat mouse sekadar lewat (misalnya menuju tab browser di tepi atas layar).
    /// </summary>
    public void OnPointerEntered()
    {
        lock (_gate)
        {
            _collapseTimer?.Dispose();
            _collapseTimer = null;
            if (_pointerOver || _expandTimer is not null)
            {
                return;
            }

            if (_options.ExpandDelay > TimeSpan.Zero)
            {
                ITimer? timer = null;
                timer = _time.CreateTimer(
                    _ => Update(() =>
                    {
                        // Timer lama yang terlambat berbunyi setelah dibatalkan tidak boleh membuka pil.
                        if (!ReferenceEquals(_expandTimer, timer))
                        {
                            return;
                        }

                        timer?.Dispose();
                        _expandTimer = null;
                        _pointerOver = true;
                    }),
                    null,
                    _options.ExpandDelay,
                    Timeout.InfiniteTimeSpan);
                _expandTimer = timer;
                return;
            }
        }

        Update(() => _pointerOver = true);
    }

    public void OnPointerExited()
    {
        lock (_gate)
        {
            // Belum sempat terbuka: cukup batalkan.
            if (_expandTimer is not null)
            {
                _expandTimer.Dispose();
                _expandTimer = null;
                return;
            }

            _collapseTimer?.Dispose();
            _collapseTimer = _time.CreateTimer(
                _ => Update(() => _pointerOver = false),
                null,
                _options.CollapseDelay,
                Timeout.InfiniteTimeSpan);
        }
    }

    /// <summary>Pengaturan berubah (misalnya "tampilkan pil saat tidak ada lagu" dimatikan).</summary>
    public void UpdateOptions(IslandOptions options) => Update(() => _options = options);

    /// <summary>Sembunyikan island saat game, video, atau presentasi fullscreen.</summary>
    public void OnFullscreenChanged(bool isFullscreen) => Update(() => _suppressed = isFullscreen);

    public void Dispose()
    {
        lock (_gate)
        {
            _collapseTimer?.Dispose();
            _collapseTimer = null;
            _expandTimer?.Dispose();
            _expandTimer = null;
        }
    }

    private void Update(Action mutate)
    {
        IslandState next;
        lock (_gate)
        {
            mutate();
            next = Compute();
            if (next == _state)
            {
                return;
            }

            _state = next;
        }

        StateChanged?.Invoke(this, next);
    }

    // Dipanggil di dalam _gate.
    private IslandState Compute()
    {
        if (_suppressed)
        {
            return IslandState.Hidden;
        }

        if (_content is null)
        {
            if (!_options.ShowWhenIdle)
            {
                return IslandState.Hidden;
            }

            return _pointerOver ? IslandState.Expanded : IslandState.Compact;
        }

        if (_content.IsNotification)
        {
            return IslandState.Notification;
        }

        return _pointerOver ? IslandState.Expanded : IslandState.Compact;
    }
}
