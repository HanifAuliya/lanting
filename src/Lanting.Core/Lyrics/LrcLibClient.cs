using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Lanting.Core.Lyrics;

/// <summary>Mencari lirik bertanda waktu untuk sebuah lagu.</summary>
public interface ILyricsProvider
{
    /// <summary><c>null</c> bila liriknya tidak ada (atau lagunya instrumental).</summary>
    Task<SyncedLyrics?> FindAsync(TrackQuery query, CancellationToken cancellationToken);
}

/// <summary>
/// Lirik dari LRCLIB (https://lrclib.net), layanan lirik gratis tanpa API key.
/// Hasil (termasuk "tidak ketemu") disimpan di memori supaya lagu yang sama tidak dicari berulang.
/// </summary>
public sealed class LrcLibClient : ILyricsProvider
{
    private const int CacheSize = 50;

    // Durasi di LRCLIB dibulatkan ke detik dan versi lagu bisa beda sedikit; di luar selisih ini dianggap versi lain.
    private static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(4);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    private readonly HttpClient _http;
    private readonly Lock _gate = new();
    private readonly Dictionary<TrackQuery, SyncedLyrics?> _cache = [];
    private readonly Queue<TrackQuery> _cacheOrder = new();

    /// <param name="http">HttpClient dengan BaseAddress https://lrclib.net/ dan User-Agent aplikasi.</param>
    public LrcLibClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<SyncedLyrics?> FindAsync(TrackQuery query, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(query, out var cached))
            {
                return cached;
            }
        }

        var match = Pick(await SearchAsync($"track_name={Uri.EscapeDataString(query.Title)}&artist_name={Uri.EscapeDataString(query.Artist)}", cancellationToken).ConfigureAwait(false), query)
            ?? Pick(await SearchAsync($"q={Uri.EscapeDataString(query.SearchText)}", cancellationToken).ConfigureAwait(false), query);
        var lyrics = LrcParser.Parse(match?.SyncedLyrics);

        lock (_gate)
        {
            if (_cache.TryAdd(query, lyrics))
            {
                _cacheOrder.Enqueue(query);
                if (_cacheOrder.Count > CacheSize)
                {
                    _cache.Remove(_cacheOrder.Dequeue());
                }
            }
        }

        return lyrics;
    }

    private async Task<List<LrcLibTrack>> SearchAsync(string parameters, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await _http.GetFromJsonAsync<List<LrcLibTrack>>($"api/search?{parameters}", cancellationToken).ConfigureAwait(false) ?? [];
            }
            catch (HttpRequestException ex) when (attempt == 1 && (int?)ex.StatusCode >= 500)
            {
                // Server LRCLIB kadang sibuk sesaat (503); coba sekali lagi.
                await Task.Delay(RetryDelay, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Pilih hasil yang punya lirik bertanda waktu. Bila durasi lagu diketahui, pilih yang durasinya paling dekat
    /// (dan tolak yang terlalu jauh, karena itu biasanya versi live atau remix).
    /// </summary>
    internal static LrcLibTrack? Pick(IReadOnlyList<LrcLibTrack> results, TrackQuery query)
    {
        var synced = results.Where(r => !r.Instrumental && !string.IsNullOrWhiteSpace(r.SyncedLyrics));
        if (query.Duration is not { } duration || duration <= TimeSpan.Zero)
        {
            return synced.FirstOrDefault();
        }

        return synced
            .Select(r => (Track: r, Gap: (TimeSpan.FromSeconds(r.Duration) - duration).Duration()))
            .Where(r => r.Gap <= DurationTolerance)
            .OrderBy(r => r.Gap)
            .Select(r => r.Track)
            .FirstOrDefault();
    }

    internal sealed record LrcLibTrack(
        [property: JsonPropertyName("trackName")] string? TrackName,
        [property: JsonPropertyName("artistName")] string? ArtistName,
        [property: JsonPropertyName("duration")] double Duration,
        [property: JsonPropertyName("instrumental")] bool Instrumental,
        [property: JsonPropertyName("syncedLyrics")] string? SyncedLyrics);
}
