using Lanting.Core.Location;
using Lanting.Core.Prayer;
using Lanting.Core.Settings;
using Microsoft.Extensions.Time.Testing;

namespace Lanting.Core.Tests;

public class PrayerTests
{
    // Jadwal resmi Kemenag (lewat api.myquran.com), urutan: imsak, subuh, terbit, dzuhur, ashar, maghrib, isya.
    [Theory]
    [InlineData("Jakarta", -6.1754, 106.8272, 7, "2026-11-02", "03:58 04:08 05:23 11:40 14:55 17:49 19:01")]
    [InlineData("Jakarta", -6.1754, 106.8272, 7, "2026-06-21", "04:30 04:40 05:58 11:58 15:19 17:51 19:05")]
    [InlineData("Makassar", -5.1477, 119.4327, 8, "2026-11-02", "04:09 04:19 05:34 11:49 15:06 17:58 19:09")]
    [InlineData("Surabaya", -7.2575, 112.7521, 7, "2026-11-02", "03:33 03:43 04:58 11:16 14:31 17:27 18:39")]
    public void Matches_Kemenag_schedule_within_one_minute(string city, double latitude, double longitude, int zone, string date, string expected)
    {
        var times = PrayerCalculator.For(DateOnly.Parse(date), latitude, longitude, TimeSpan.FromHours(zone));
        var official = expected.Split(' ').Select(TimeOnly.Parse).ToArray();

        Assert.Equal(7, times.Count);
        for (var i = 0; i < official.Length; i++)
        {
            var diff = Math.Abs((TimeOnly.FromDateTime(times[i].Time).ToTimeSpan() - official[i].ToTimeSpan()).TotalMinutes);
            Assert.True(diff <= 1, $"{city} {date} {times[i].Label}: {times[i].Time:HH:mm}, Kemenag {official[i]:HH:mm}");
        }
    }

    [Fact]
    public void Next_skips_imsak_and_sunrise_and_rolls_over_to_tomorrow()
    {
        var zone = TimeSpan.FromHours(7);

        var morning = PrayerCalculator.Next(new DateTime(2026, 11, 2, 5, 0, 0), -6.1754, 106.8272, zone);
        var night = PrayerCalculator.Next(new DateTime(2026, 11, 2, 21, 0, 0), -6.1754, 106.8272, zone);

        Assert.Equal(PrayerName.Dzuhur, morning.Name);
        Assert.Equal(PrayerName.Subuh, night.Name);
        Assert.Equal(new DateOnly(2026, 11, 3), DateOnly.FromDateTime(night.Time));
    }
}

public sealed class PrayerModuleTests : IDisposable
{
    private static readonly GeoLocation Jakarta = new("Jakarta", -6.1754, 106.8272, "Asia/Jakarta");

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "lanting-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 11, 2, 11, 30, 0, TimeSpan.FromHours(7)));
    private readonly MediaModuleTests.RecordingHost _host = new();
    private readonly SettingsStore _store;

    public PrayerModuleTests()
    {
        _store = new SettingsStore(Path.Combine(_folder, "settings.json"));
        _store.Update(s => s with { City = Jakarta });
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Prayer_time_is_announced_once()
    {
        await StartAsync();

        _time.Advance(TimeSpan.FromMinutes(10)); // 11:40, Dzuhur
        _time.Advance(TimeSpan.FromMinutes(1));

        var content = Assert.Single(_host.Shown);
        var notice = Assert.IsType<PrayerNotice>(content.Payload);
        Assert.Equal(PrayerName.Dzuhur, notice.Time.Name);
        Assert.Equal("Jakarta", notice.Place);
        Assert.True(content.IsNotification);
    }

    [Fact]
    public async Task Prayer_that_started_before_launch_is_not_announced()
    {
        _time.Advance(TimeSpan.FromMinutes(12)); // 11:42, Dzuhur sudah masuk

        await StartAsync();
        _time.Advance(TimeSpan.FromMinutes(1));

        Assert.Empty(_host.Shown);
    }

    [Fact]
    public async Task Turned_off_notifications_stay_quiet()
    {
        var module = await StartAsync();
        module.NotificationsEnabled = false;

        _time.Advance(TimeSpan.FromMinutes(11));

        Assert.Empty(_host.Shown);
    }

    [Fact]
    public async Task Schedule_for_today_uses_the_location_time()
    {
        var module = await StartAsync();

        var today = module.Today();

        Assert.NotNull(today);
        Assert.Equal(new DateTime(2026, 11, 2, 11, 30, 0), today.Now);
        Assert.Equal(PrayerName.Dzuhur, today.Next.Name);
    }

    private async Task<PrayerModule> StartAsync()
    {
        var module = new PrayerModule(new LocationService(new HttpClient(), _time, _store), _time);
        await module.StartAsync(_host, CancellationToken.None);
        return module;
    }
}
