using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Lanting.Core.Location;

namespace Lanting.Core.Weather;

/// <summary>Jenis cuaca untuk memilih ikon.</summary>
public enum WeatherKind
{
    Clear,
    PartlyCloudy,
    Cloudy,
    Fog,
    Drizzle,
    Rain,
    Snow,
    Thunderstorm,
}

/// <summary>Cuaca saat ini di lokasi pengguna.</summary>
public sealed record WeatherReport(
    string Place,
    double TemperatureC,
    int Code,
    double? MaxC,
    double? MinC,
    DateTimeOffset FetchedAt)
{
    public WeatherKind Kind => WeatherCodes.Kind(Code);

    public string Description => WeatherCodes.Describe(Code);
}

/// <summary>Kode cuaca WMO yang dipakai Open-Meteo, diterjemahkan ke bahasa Indonesia.</summary>
public static class WeatherCodes
{
    public static WeatherKind Kind(int code) => code switch
    {
        0 => WeatherKind.Clear,
        1 or 2 => WeatherKind.PartlyCloudy,
        3 => WeatherKind.Cloudy,
        45 or 48 => WeatherKind.Fog,
        >= 51 and <= 57 => WeatherKind.Drizzle,
        >= 61 and <= 67 or >= 80 and <= 82 => WeatherKind.Rain,
        >= 71 and <= 77 or 85 or 86 => WeatherKind.Snow,
        >= 95 => WeatherKind.Thunderstorm,
        _ => WeatherKind.Cloudy,
    };

    public static string Describe(int code) => code switch
    {
        0 => "Cerah",
        1 => "Cerah berawan",
        2 => "Berawan sebagian",
        3 => "Berawan",
        45 or 48 => "Berkabut",
        >= 51 and <= 57 => "Gerimis",
        61 or 80 => "Hujan ringan",
        63 or 81 => "Hujan",
        65 or 82 => "Hujan lebat",
        66 or 67 => "Hujan es",
        >= 71 and <= 77 or 85 or 86 => "Salju",
        >= 95 => "Badai petir",
        _ => "Berawan",
    };
}

/// <summary>
/// Cuaca dari Open-Meteo (gratis, tanpa API key) untuk lokasi dari <see cref="LocationService"/>.
/// Tidak ada timer: data diambil saat diminta (misalnya saat island dibuka) dan dipakai ulang selama masih segar.
/// </summary>
public sealed class WeatherService
{
    /// <summary>Cuaca dianggap masih segar selama ini.</summary>
    public static readonly TimeSpan FreshFor = TimeSpan.FromMinutes(30);

    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly LocationService _location;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private WeatherReport? _report;
    private GeoLocation? _reportLocation;

    public WeatherService(HttpClient http, TimeProvider time, LocationService location)
    {
        _http = http;
        _time = time;
        _location = location;
    }

    /// <summary>Cuaca terakhir yang diketahui, walau mungkin sudah tidak segar.</summary>
    public WeatherReport? Latest => _report;

    /// <summary>
    /// Cuaca saat ini. Memakai data tersimpan bila belum lewat <see cref="FreshFor"/>.
    /// Bila gagal (tidak ada internet), mengembalikan data lama bila ada; error tetap dilempar bila belum pernah berhasil.
    /// </summary>
    public async Task<WeatherReport?> GetAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _time.GetUtcNow();

            // Kota yang dipilih di Pengaturan langsung berlaku, walau cuaca lama masih segar.
            if (_report is not null && now - _report.FetchedAt < FreshFor && _reportLocation == _location.Current)
            {
                return _report;
            }

            try
            {
                var location = await _location.GetAsync(cancellationToken).ConfigureAwait(false);
                _report = await FetchWeatherAsync(location, now, cancellationToken).ConfigureAwait(false);
                _reportLocation = location;
            }
            catch (Exception ex) when (_report is not null
                && ex is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
            {
                // Tetap tampilkan data lama daripada kosong; dicoba lagi saat dibuka berikutnya.
            }

            return _report;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<WeatherReport> FetchWeatherAsync(GeoLocation location, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var url = $"https://api.open-meteo.com/v1/forecast?{LocationService.Coordinates(location)}&current=temperature_2m,weather_code&daily=temperature_2m_max,temperature_2m_min&timezone=auto&forecast_days=1";
        var forecast = await _http.GetFromJsonAsync<OpenMeteoForecast>(url, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Jawaban Open-Meteo kosong.");
        if (forecast.Current is not { } current)
        {
            throw new InvalidOperationException("Jawaban Open-Meteo tanpa cuaca saat ini.");
        }

        return new WeatherReport(
            location.Place,
            current.Temperature,
            current.WeatherCode,
            forecast.Daily?.Max?.FirstOrDefault(),
            forecast.Daily?.Min?.FirstOrDefault(),
            now);
    }

    internal sealed record OpenMeteoForecast(
        [property: JsonPropertyName("current")] OpenMeteoCurrent? Current,
        [property: JsonPropertyName("daily")] OpenMeteoDaily? Daily);

    internal sealed record OpenMeteoCurrent(
        [property: JsonPropertyName("temperature_2m")] double Temperature,
        [property: JsonPropertyName("weather_code")] int WeatherCode);

    internal sealed record OpenMeteoDaily(
        [property: JsonPropertyName("temperature_2m_max")] double?[]? Max,
        [property: JsonPropertyName("temperature_2m_min")] double?[]? Min);
}
