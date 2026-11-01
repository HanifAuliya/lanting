using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Lanting.Core.Lyrics;

public sealed partial class NetEaseClient : ILyricsProvider
{
    private static readonly TimeSpan DurationTolerance = TimeSpan.FromSeconds(4);
    private readonly HttpClient _http;

    public NetEaseClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<SyncedLyrics?> FindAsync(TrackQuery query, CancellationToken cancellationToken)
    {
        var search = await _http.GetFromJsonAsync<SearchResponse>(
            $"api/search/get?s={Uri.EscapeDataString(query.SearchText)}&type=1&limit=10", cancellationToken
        ).ConfigureAwait(false);

        var song = Pick(search?.Result?.Songs ?? [], query);
        if (song is null)
            return null;

        var response = await _http.GetFromJsonAsync<LyricResponse>(
            $"api/song/lyric?id={song.Id}&lv=1", cancellationToken
        ).ConfigureAwait(false);

        var lyrics = LrcParser.Parse(response?.Lrc?.Lyric);
        if (lyrics is null)
            return null;

        var lines = lyrics.Lines.Where(l => !CreditLine().IsMatch(l.Text)).ToList();
        return lines.Count == 0 ? null : new SyncedLyrics(lines);
    }

    internal static NetEaseSong? Pick(IReadOnlyList<NetEaseSong> songs, TrackQuery query)
    {
        var sameTitle = songs.Where(s => SameTitle(s.Name, query.Title));
        if (query.Duration is not { } duration || duration <= TimeSpan.Zero)
        {
            return sameTitle.FirstOrDefault();
        }

        return sameTitle
            .Select(s => (Song: s, Gap: (TimeSpan.FromMilliseconds(s.Duration) - duration).Duration()))
            .Where(s => s.Gap <= DurationTolerance)
            .OrderBy(s => s.Gap)
            .Select(s => s.Song)
            .FirstOrDefault();
    }

    private static bool SameTitle(string? name, string title) =>
        !string.IsNullOrWhiteSpace(name)
        && (name.Contains(title, StringComparison.OrdinalIgnoreCase)
            || title.Contains(name, StringComparison.OrdinalIgnoreCase));

    [GeneratedRegex(@"^[\p{IsCJKUnifiedIdeographs}\s/]+[:：]")]
    internal static partial Regex CreditLine();

    internal sealed record NetEaseSong(
        [property: JsonPropertyName("id")] long Id,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("duration")] long Duration);

    private sealed record SearchResponse([property: JsonPropertyName("result")] SearchResult? Result);

    private sealed record SearchResult([property: JsonPropertyName("songs")] List<NetEaseSong>? Songs);

    private sealed record LyricResponse([property: JsonPropertyName("lrc")] LyricText? Lrc);

    private sealed record LyricText([property: JsonPropertyName("lyric")] string? Lyric);
}
