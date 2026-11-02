using Lanting.Core.Island;
using Lanting.Core.Location;
using Lanting.Core.Modules;

namespace Lanting.Core.Prayer;

/// <summary>Payload notifikasi "Waktunya Dzuhur".</summary>
public sealed record PrayerNotice(PrayerTime Time, string Place);

/// <summary>Jadwal hari ini di satu lokasi, untuk halaman Sholat.</summary>
/// <param name="Now">Jam sekarang di zona waktu lokasi.</param>
public sealed record PrayerSchedule(GeoLocation Location, DateTime Now, IReadOnlyList<PrayerTime> Today, PrayerTime Next);

/// <summary>
/// Memberi tahu lewat island saat masuk waktu sholat. Timer berbunyi paling lama setiap 30 menit lalu menghitung ulang,
/// jadi tetap tepat setelah laptop tidur (sleep) atau jam Windows berubah. Notifikasi yang terlambat lebih dari
/// beberapa menit (misalnya laptop baru bangun) tidak ditampilkan.
/// </summary>
public sealed class PrayerModule : IIslandModule, IDisposable
{
    public const string ModuleId = "prayer";

    private static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan LateLimit = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan Duration = TimeSpan.FromSeconds(8);

    private readonly LocationService _location;
    private readonly TimeProvider _time;
    private readonly Lock _gate = new();
    private IIslandHost? _host;
    private ITimer? _timer;
    private DateTime? _announced;

    public PrayerModule(LocationService location, TimeProvider time)
    {
        _location = location;
        _time = time;
    }

    public string Id => ModuleId;

    /// <summary>Dari pengaturan: notifikasi waktu sholat aktif.</summary>
    public bool NotificationsEnabled { get; set; } = true;

    /// <summary>Jadwal hari ini, atau <c>null</c> bila lokasi belum diketahui (belum pernah online).</summary>
    public PrayerSchedule? Today()
    {
        if (_location.Current is not { } location)
        {
            return null;
        }

        var now = Now(location);
        return new PrayerSchedule(
            location,
            now,
            PrayerCalculator.For(DateOnly.FromDateTime(now), location.Latitude, location.Longitude, location.UtcOffset(now)),
            PrayerCalculator.Next(now, location.Latitude, location.Longitude, location.UtcOffset(now)));
    }

    public Task StartAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _host = host;
            _timer = _time.CreateTimer(_ => Tick(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

            // Waktu sholat yang sudah lewat saat Lanting baru dinyalakan tidak diumumkan.
            _announced = Recent(Today())?.Time;
        }

        Tick();

        // Lokasi otomatis dicari di belakang; tidak menahan startup bila internet lambat.
        _ = RefreshLocationAsync();
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _host = null;
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>Lokasi berubah (kota dipilih di Pengaturan): hitung ulang jadwal.</summary>
    public void Reschedule() => Tick();

    private async Task RefreshLocationAsync()
    {
        try
        {
            await _location.GetAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
        {
            // Offline: dicoba lagi saat cuaca atau halaman Sholat dibuka.
        }

        lock (_gate)
        {
            _announced ??= Recent(Today())?.Time;
        }

        Tick();
    }

    private void Tick()
    {
        lock (_gate)
        {
            if (_host is null || _timer is null)
            {
                return;
            }

            var schedule = Today();
            if (schedule is null)
            {
                _timer.Change(MaxWait, Timeout.InfiniteTimeSpan);
                return;
            }

            if (Recent(schedule) is { } recent && recent.Time != _announced)
            {
                _announced = recent.Time;
                if (NotificationsEnabled)
                {
                    _host.Show(new IslandContent(ModuleId, "time", IslandPriority.Alert, new PrayerNotice(recent, schedule.Location.Place), Duration));
                }
            }

            // Bangun tepat sedetik setelah waktu berikutnya, atau lebih awal untuk menghitung ulang.
            var wait = schedule.Next.Time - schedule.Now + TimeSpan.FromSeconds(1);
            _timer.Change(wait < MaxWait ? wait : MaxWait, Timeout.InfiniteTimeSpan);
        }
    }

    // Waktu sholat yang baru saja masuk (belum lewat beberapa menit).
    private static PrayerTime? Recent(PrayerSchedule? schedule) =>
        schedule?.Today.LastOrDefault(t => t.IsPrayer && t.Time <= schedule.Now && schedule.Now - t.Time < LateLimit);

    private DateTime Now(GeoLocation location)
    {
        var utc = _time.GetUtcNow().UtcDateTime;
        return utc + location.UtcOffset(utc);
    }
}
