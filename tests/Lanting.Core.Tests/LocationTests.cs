using Lanting.Core.Location;
using Lanting.Core.Settings;
using Microsoft.Extensions.Time.Testing;
using static Lanting.Core.Tests.WeatherTests;

namespace Lanting.Core.Tests;

public sealed class LocationTests : IDisposable
{
    private const string IpJson = """
        { "success": true, "city": "Banjarmasin", "country": "Indonesia", "latitude": -3.32, "longitude": 114.59,
          "timezone": { "id": "Asia/Makassar" } }
        """;

    private readonly string _folder = Path.Combine(Path.GetTempPath(), "lanting-tests", Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new();
    private readonly FakeHandler _handler = new();

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task Chosen_city_is_used_without_asking_the_internet()
    {
        var store = Store();
        var city = new GeoLocation("Makassar, Sulawesi Selatan", -5.14, 119.43, "Asia/Makassar");
        store.Update(s => s with { City = city });

        var location = await Service(store).GetAsync(CancellationToken.None);

        Assert.Equal(city, location);
        Assert.Empty(_handler.Requests);
    }

    [Fact]
    public async Task Automatic_location_is_remembered_for_offline_use()
    {
        _handler.Reply("ipwho.is", IpJson);
        var store = Store();
        var first = await Service(store).GetAsync(CancellationToken.None);

        // Aplikasi dibuka lagi tanpa internet: lokasi terakhir dari pengaturan yang dipakai.
        _handler.Fail("ipwho.is");
        var offline = await Service(new SettingsStore(Path.Combine(_folder, "settings.json"))).GetAsync(CancellationToken.None);

        Assert.Equal("Banjarmasin", first.Place);
        Assert.Equal("Asia/Makassar", first.TimeZone);
        Assert.Equal(first, offline);
    }

    [Fact]
    public async Task Cities_are_searched_with_their_province()
    {
        _handler.Reply("geocoding", """
            { "results": [
              { "name": "Banjarmasin", "latitude": -3.31, "longitude": 114.59, "timezone": "Asia/Makassar", "country_code": "ID", "country": "Indonesia", "admin1": "Kalimantan Selatan" },
              { "name": "Banjar", "latitude": 43.2, "longitude": 21.1, "timezone": "Europe/Belgrade", "country_code": "RS", "country": "Serbia", "admin1": "Central Serbia" } ] }
            """);

        var cities = await Service(null).SearchAsync("banjar", CancellationToken.None);

        Assert.Equal(["Banjarmasin, Kalimantan Selatan", "Banjar, Serbia"], cities.Select(c => c.Place));
    }

    [Fact]
    public void Utc_offset_comes_from_the_location_time_zone()
    {
        var makassar = new GeoLocation("Makassar", -5.14, 119.43, "Asia/Makassar");

        Assert.Equal(TimeSpan.FromHours(8), makassar.UtcOffset(new DateTime(2026, 11, 2)));
    }

    private SettingsStore Store() => new(Path.Combine(_folder, "settings.json"));

    private LocationService Service(SettingsStore? store) => new(new HttpClient(_handler), _time, store);
}
