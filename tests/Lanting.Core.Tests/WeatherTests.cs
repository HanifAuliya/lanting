using System.Net;
using System.Text;
using Lanting.Core.Clock;
using Lanting.Core.Weather;
using Microsoft.Extensions.Time.Testing;

namespace Lanting.Core.Tests;

public class WeatherTests
{
    private const string IpJson = """{ "success": true, "city": "Banjarmasin", "country": "Indonesia", "latitude": -3.32, "longitude": 114.59 }""";
    private const string ForecastJson = """
        { "current": { "temperature_2m": 31.6, "weather_code": 3 },
          "daily": { "temperature_2m_max": [32.4], "temperature_2m_min": [24.9] } }
        """;

    private readonly FakeTimeProvider _time = new();
    private readonly FakeHandler _handler = new();

    [Fact]
    public async Task Weather_is_read_for_the_location_of_the_ip()
    {
        _handler.Reply("ipwho.is", IpJson);
        _handler.Reply("open-meteo", ForecastJson);

        var report = await Service().GetAsync(CancellationToken.None);

        Assert.NotNull(report);
        Assert.Equal("Banjarmasin", report.Place);
        Assert.Equal(31.6, report.TemperatureC);
        Assert.Equal(32.4, report.MaxC);
        Assert.Equal(24.9, report.MinC);
        Assert.Equal("Berawan", report.Description);
        Assert.Contains("latitude=-3.32&longitude=114.59", _handler.Requests.Last(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Fresh_weather_is_reused_without_new_requests()
    {
        _handler.Reply("ipwho.is", IpJson);
        _handler.Reply("open-meteo", ForecastJson);
        var service = Service();
        await service.GetAsync(CancellationToken.None);

        _time.Advance(TimeSpan.FromMinutes(29));
        await service.GetAsync(CancellationToken.None);
        Assert.Equal(2, _handler.Requests.Count);

        // Lewat 30 menit: cuaca diambil lagi, lokasi masih dipakai ulang.
        _time.Advance(TimeSpan.FromMinutes(2));
        await service.GetAsync(CancellationToken.None);
        Assert.Equal(3, _handler.Requests.Count);
        Assert.Contains("open-meteo", _handler.Requests.Last(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Old_weather_is_kept_when_offline()
    {
        _handler.Reply("ipwho.is", IpJson);
        _handler.Reply("open-meteo", ForecastJson);
        var service = Service();
        var first = await service.GetAsync(CancellationToken.None);

        _time.Advance(TimeSpan.FromHours(1));
        _handler.Fail("open-meteo");
        var second = await service.GetAsync(CancellationToken.None);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task First_failure_is_reported_to_the_caller()
    {
        _handler.Fail("ipwho.is");

        await Assert.ThrowsAsync<HttpRequestException>(() => Service().GetAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(0, WeatherKind.Clear, "Cerah")]
    [InlineData(2, WeatherKind.PartlyCloudy, "Berawan sebagian")]
    [InlineData(45, WeatherKind.Fog, "Berkabut")]
    [InlineData(53, WeatherKind.Drizzle, "Gerimis")]
    [InlineData(81, WeatherKind.Rain, "Hujan")]
    [InlineData(95, WeatherKind.Thunderstorm, "Badai petir")]
    public void Weather_codes_are_translated(int code, WeatherKind kind, string text)
    {
        Assert.Equal(kind, WeatherCodes.Kind(code));
        Assert.Equal(text, WeatherCodes.Describe(code));
    }

    [Fact]
    public void Day_progress_counts_from_midnight()
    {
        var progress = DayProgress.At(new DateTime(2026, 10, 28, 6, 0, 0));

        Assert.Equal(25, progress.Percent);
        Assert.Equal(TimeSpan.FromHours(18), progress.Remaining);
    }

    private WeatherService Service() => new(new HttpClient(_handler), _time);

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Dictionary<string, string?> _replies = [];

        public List<string> Requests { get; } = [];

        public void Reply(string host, string json) => _replies[host] = json;

        public void Fail(string host) => _replies[host] = null;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var url = request.RequestUri!.ToString();
            Requests.Add(url);
            var json = _replies.First(r => url.Contains(r.Key, StringComparison.Ordinal)).Value;
            return Task.FromResult(json is null
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
    }
}
