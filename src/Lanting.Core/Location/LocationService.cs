using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Lanting.Core.Settings;

namespace Lanting.Core.Location;

/// <summary>Lokasi untuk cuaca dan jadwal sholat.</summary>
/// <param name="Place">Nama yang ditampilkan, misalnya "Banjarmasin".</param>
/// <param name="TimeZone">Zona waktu IANA ("Asia/Makassar"), atau <c>null</c> bila tidak diketahui (dipakai zona waktu laptop).</param>
public sealed record GeoLocation(string Place, double Latitude, double Longitude, string? TimeZone)
{
    /// <summary>Selisih zona waktu lokasi ini dari UTC pada tanggal tertentu.</summary>
    public TimeSpan UtcOffset(DateTime date)
    {
        if (TimeZone is not null)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(TimeZone).GetUtcOffset(date);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
                // Zona tidak dikenal Windows: pakai zona waktu laptop.
            }
        }

        return TimeZoneInfo.Local.GetUtcOffset(date);
    }
}

/// <summary>
/// Menentukan lokasi pengguna: kota yang dipilih di Pengaturan, atau perkiraan dari alamat IP (ipwho.is).
/// Lokasi otomatis terakhir disimpan di pengaturan, jadi jadwal sholat tetap ada walau laptop sedang offline.
/// Pencarian kota memakai geocoding Open-Meteo (gratis, tanpa API key).
/// </summary>
public sealed class LocationService
{
    // Lokasi dari IP jarang berubah; cukup dicari ulang sesekali.
    private static readonly TimeSpan AutoFreshFor = TimeSpan.FromHours(6);

    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly SettingsStore? _settings;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GeoLocation? _auto;
    private DateTimeOffset _autoFetchedAt;

    /// <param name="settings">Tempat kota pilihan dan lokasi otomatis terakhir disimpan; boleh <c>null</c> (tes).</param>
    public LocationService(HttpClient http, TimeProvider time, SettingsStore? settings = null)
    {
        _http = http;
        _time = time;
        _settings = settings;
    }

    /// <summary>Lokasi yang diketahui sekarang tanpa ke internet: kota pilihan, atau lokasi otomatis terakhir.</summary>
    public GeoLocation? Current => _settings?.Current.City ?? _auto ?? _settings?.Current.LastAutoLocation;

    /// <summary>
    /// Lokasi saat ini. Kota pilihan langsung dipakai; lokasi otomatis dicari ulang paling sering 6 jam sekali.
    /// Bila gagal (tidak ada internet), lokasi terakhir yang dipakai; error dilempar bila belum pernah ada.
    /// </summary>
    public async Task<GeoLocation> GetAsync(CancellationToken cancellationToken)
    {
        if (_settings?.Current.City is { } city)
        {
            return city;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var now = _time.GetUtcNow();
            if (_auto is not null && now - _autoFetchedAt < AutoFreshFor)
            {
                return _auto;
            }

            try
            {
                _auto = await FetchFromIpAsync(cancellationToken).ConfigureAwait(false);
                _autoFetchedAt = now;
                var auto = _auto;
                _settings?.Update(s => s with { LastAutoLocation = auto });
            }
            catch (Exception ex) when (Current is not null
                && ex is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
            {
                // Pakai lokasi terakhir; dicoba lagi lain kali.
            }

            return Current ?? throw new InvalidOperationException("Lokasi belum diketahui.");
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Cari kota berdasarkan nama, untuk dipilih di Pengaturan.</summary>
    public async Task<IReadOnlyList<GeoLocation>> SearchAsync(string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return [];
        }

        var url = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(name.Trim())}&count=8&language=id&format=json";
        var result = await _http.GetFromJsonAsync<GeocodingResult>(url, cancellationToken).ConfigureAwait(false);
        return
        [
            .. (result?.Results ?? [])
                .Select(r => new GeoLocation(Describe(r), r.Latitude, r.Longitude, r.TimeZone)),
        ];
    }

    // "Banjarmasin, Kalimantan Selatan" (atau negara bila bukan Indonesia).
    private static string Describe(GeocodingPlace place)
    {
        var region = place.CountryCode == "ID" ? place.Admin1 : place.Country;
        return string.IsNullOrWhiteSpace(region) ? place.Name : $"{place.Name}, {region}";
    }

    private async Task<GeoLocation> FetchFromIpAsync(CancellationToken cancellationToken)
    {
        var ip = await _http.GetFromJsonAsync<IpWhoIs>(
            "https://ipwho.is/?fields=success,city,country,latitude,longitude,timezone", cancellationToken).ConfigureAwait(false);
        if (ip is not { Success: true, Latitude: { } latitude, Longitude: { } longitude })
        {
            throw new InvalidOperationException("Lokasi dari IP tidak ditemukan.");
        }

        var place = string.IsNullOrWhiteSpace(ip.City) ? ip.Country ?? string.Empty : ip.City;
        return new GeoLocation(place, latitude, longitude, ip.TimeZone?.Id);
    }

    /// <summary>Koordinat untuk URL, selalu dengan titik desimal.</summary>
    public static string Coordinates(GeoLocation location) => string.Create(
        CultureInfo.InvariantCulture,
        $"latitude={location.Latitude:0.####}&longitude={location.Longitude:0.####}");

    internal sealed record IpWhoIs(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("city")] string? City,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("latitude")] double? Latitude,
        [property: JsonPropertyName("longitude")] double? Longitude,
        [property: JsonPropertyName("timezone")] IpTimeZone? TimeZone);

    internal sealed record IpTimeZone([property: JsonPropertyName("id")] string? Id);

    internal sealed record GeocodingResult([property: JsonPropertyName("results")] GeocodingPlace[]? Results);

    internal sealed record GeocodingPlace(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("latitude")] double Latitude,
        [property: JsonPropertyName("longitude")] double Longitude,
        [property: JsonPropertyName("timezone")] string? TimeZone,
        [property: JsonPropertyName("country_code")] string? CountryCode,
        [property: JsonPropertyName("country")] string? Country,
        [property: JsonPropertyName("admin1")] string? Admin1);
}
