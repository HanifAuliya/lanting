namespace Lanting.Core.Face;

/// <summary>Ekspresi wajah di pil kecil.</summary>
public enum Expression
{
    /// <summary>Mata biasa: berkedip, melirik, mengikuti kursor.</summary>
    Neutral,

    /// <summary>Mata melengkung (^ ^).</summary>
    Happy,

    /// <summary>Mata membesar.</summary>
    Surprised,

    /// <summary>Mata setengah tertutup.</summary>
    Sleepy,

    /// <summary>Mata tertutup jadi garis, dengan "z".</summary>
    Asleep,

    /// <summary>Baterai laptop lemah: mata kuning, cemas.</summary>
    Worried,

    /// <summary>Baterai laptop hampir habis: mata merah berkedip.</summary>
    Alarm,

    /// <summary>Baterai penuh saat dicas: senyum hijau.</summary>
    Full,

    /// <summary>Charger baru dicolok: senang, dengan petir.</summary>
    Charging,
}

/// <summary>Keadaan laptop yang memengaruhi ekspresi.</summary>
/// <param name="BatteryPercent">Baterai laptop, atau <c>null</c> bila tidak ada baterai (PC).</param>
/// <param name="PluggedIn">Charger tersambung.</param>
/// <param name="Idle">Lama tidak ada sentuhan keyboard/mouse.</param>
/// <param name="Now">Jam lokal.</param>
public sealed record FaceState(int? BatteryPercent, bool PluggedIn, TimeSpan Idle, DateTime Now);

/// <summary>
/// Memilih ekspresi wajah: ekspresi dasar dari keadaan laptop (baterai, lama tidak dipakai, jam malam),
/// ditambah ekspresi sesaat untuk kejadian (charger dicolok, pengguna kembali, dashboard ditutup).
/// Tidak tahu apa-apa soal UI, jadi bisa dites.
/// </summary>
public sealed class FaceController : IDisposable
{
    public static readonly TimeSpan SleepyAfter = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan AsleepAfter = TimeSpan.FromMinutes(20);

    // Dianggap "kembali" bila tadi sudah ngantuk dan sekarang baru saja ada sentuhan.
    private static readonly TimeSpan BackWithin = TimeSpan.FromMinutes(1);

    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private readonly Queue<(Expression Expression, TimeSpan Duration)> _moments = new();
    private ITimer? _timer;
    private FaceState? _state;
    private Expression _base = Expression.Neutral;
    private Expression? _moment;
    private Expression _current = Expression.Neutral;

    public FaceController(TimeProvider time)
    {
        _time = time;
    }

    /// <summary>Ekspresi berubah. Bisa dari thread mana pun.</summary>
    public event EventHandler<Expression>? Changed;

    public Expression Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Ekspresi dasar dari keadaan laptop. Urutan penting: baterai dulu, lalu ngantuk/tidur.</summary>
    public static Expression Base(FaceState state)
    {
        if (state.BatteryPercent is { } percent)
        {
            if (state.PluggedIn && percent >= 100)
            {
                return Expression.Full;
            }

            if (!state.PluggedIn && percent <= 10)
            {
                return Expression.Alarm;
            }

            if (!state.PluggedIn && percent <= 20)
            {
                return Expression.Worried;
            }
        }

        if (state.Idle >= AsleepAfter)
        {
            return Expression.Asleep;
        }

        return state.Idle >= SleepyAfter || state.Now.Hour >= 23 || state.Now.Hour < 4
            ? Expression.Sleepy
            : Expression.Neutral;
    }

    /// <summary>Keadaan laptop terbaru (dipanggil saat baterai berubah dan secara berkala untuk lama idle).</summary>
    public void Update(FaceState state)
    {
        lock (_gate)
        {
            var before = _state;
            _state = state;
            _base = Base(state);

            if (before is not null)
            {
                if (!before.PluggedIn && state.PluggedIn && state.BatteryPercent is not null)
                {
                    Queue(Expression.Charging, TimeSpan.FromSeconds(4));
                }

                if (before.Idle >= SleepyAfter && state.Idle < BackWithin)
                {
                    Queue(Expression.Surprised, TimeSpan.FromSeconds(1.2));
                    Queue(Expression.Happy, TimeSpan.FromSeconds(2));
                }
            }

            Publish();
        }
    }

    /// <summary>Senang sebentar (misalnya dashboard baru ditutup atau lagu selesai).</summary>
    public void Cheer()
    {
        lock (_gate)
        {
            if (_base is Expression.Neutral or Expression.Sleepy)
            {
                Queue(Expression.Happy, TimeSpan.FromSeconds(1.5));
                Publish();
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    // Dipanggil di dalam _gate.
    private void Queue(Expression expression, TimeSpan duration)
    {
        _moments.Enqueue((expression, duration));
        if (_moment is null)
        {
            Next();
        }
    }

    // Dipanggil di dalam _gate: mulai ekspresi sesaat berikutnya, atau kembali ke ekspresi dasar.
    private void Next()
    {
        _timer?.Dispose();
        _timer = null;
        if (!_moments.TryDequeue(out var moment))
        {
            _moment = null;
            return;
        }

        _moment = moment.Expression;
        _timer = _time.CreateTimer(_ => OnMomentEnded(), null, moment.Duration, Timeout.InfiniteTimeSpan);
    }

    private void OnMomentEnded()
    {
        lock (_gate)
        {
            Next();
            Publish();
        }
    }

    // Dipanggil di dalam _gate. Event dipicu di dalam lock supaya urutannya terjaga; pendengar hanya mengantrekan ke UI.
    private void Publish()
    {
        var next = _moment ?? _base;
        if (next == _current)
        {
            return;
        }

        _current = next;
        Changed?.Invoke(this, next);
    }
}
