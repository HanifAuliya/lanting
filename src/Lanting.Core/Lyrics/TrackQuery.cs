using System.Text.RegularExpressions;

namespace Lanting.Core.Lyrics;

/// <summary>
/// Judul dan artis yang sudah dibersihkan untuk mencari lirik.
/// Video YouTube sering berjudul "Artis - Judul (Official Video)" dengan artis "ArtisVEVO" atau "Artis - Topic";
/// bentuk seperti itu tidak akan ketemu di LRCLIB kalau tidak dirapikan dulu.
/// </summary>
public sealed partial record TrackQuery(string Title, string Artist, string Album, TimeSpan? Duration)
{
    public static TrackQuery? Create(string title, string artist, string album, TimeSpan? duration)
    {
        var cleanArtist = ChannelSuffix().Replace(artist.Trim(), string.Empty).Trim();
        var cleanTitle = title.Trim();

        // "Artis - Judul": pakai bagian kiri sebagai artis bila cocok dengan nama channel atau artis kosong.
        var dash = cleanTitle.IndexOf(" - ", StringComparison.Ordinal);
        if (dash > 0)
        {
            var left = cleanTitle[..dash].Trim();
            if (cleanArtist.Length == 0 || Squash(left).Equals(Squash(cleanArtist), StringComparison.OrdinalIgnoreCase))
            {
                cleanArtist = left;
                cleanTitle = cleanTitle[(dash + 3)..].Trim();
            }
        }

        cleanTitle = VideoTag().Replace(cleanTitle, string.Empty).Trim();
        if (cleanTitle.Length == 0 || cleanArtist.Length == 0)
        {
            return null;
        }

        return new TrackQuery(cleanTitle, cleanArtist, album.Trim(), duration);
    }

    private static string Squash(string text) => string.Concat(text.Where(char.IsLetterOrDigit));

    // "OliviaRodrigoVEVO", "Skinnyfabs - Topic", "Artis Official".
    [GeneratedRegex(@"(\s*-\s*Topic|VEVO|\s+Official)$", RegexOptions.IgnoreCase)]
    private static partial Regex ChannelSuffix();

    // "(Official Video)", "[Lyric Video]", "(Audio)", "(Visualizer)", "(MV)", "| Official Video", dan sejenisnya.
    [GeneratedRegex(@"\s*([\(\[][^\)\]]*\b(official|video|lyrics?|audio|visuali[sz]er|mv|hd|4k|remaster(ed)?)\b[^\)\]]*[\)\]]|\|.*$)", RegexOptions.IgnoreCase)]
    private static partial Regex VideoTag();
}
