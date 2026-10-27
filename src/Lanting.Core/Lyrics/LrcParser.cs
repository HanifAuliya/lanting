using System.Globalization;
using System.Text.RegularExpressions;

namespace Lanting.Core.Lyrics;

/// <summary>
/// Membaca format LRC, misalnya <c>[00:12.34]Baris lirik</c>. Mendukung beberapa tanda waktu di satu baris
/// (<c>[00:12.00][01:02.00]Reff</c>) dan tag <c>[offset:+250]</c>.
/// </summary>
public static partial class LrcParser
{
    public static SyncedLyrics? Parse(string? lrc)
    {
        if (string.IsNullOrWhiteSpace(lrc))
        {
            return null;
        }

        var offset = TimeSpan.Zero;
        var lines = new List<LyricLine>();
        foreach (var raw in lrc.Split('\n'))
        {
            var line = raw.Trim();
            var offsetMatch = OffsetTag().Match(line);
            if (offsetMatch.Success)
            {
                offset = TimeSpan.FromMilliseconds(int.Parse(offsetMatch.Groups[1].Value, CultureInfo.InvariantCulture));
                continue;
            }

            var times = new List<TimeSpan>();
            var rest = line;
            Match match;
            while ((match = TimeTag().Match(rest)).Success)
            {
                times.Add(ToTime(match));
                rest = rest[match.Length..];
            }

            var text = rest.Trim();
            foreach (var time in times)
            {
                lines.Add(new LyricLine(time, text));
            }
        }

        if (lines.Count == 0)
        {
            return null;
        }

        // Offset positif berarti lirik tampil lebih awal.
        return new SyncedLyrics(lines.Select(l => l with { Time = Max(TimeSpan.Zero, l.Time - offset) }));
    }

    private static TimeSpan ToTime(Match match)
    {
        var minutes = int.Parse(match.Groups["m"].Value, CultureInfo.InvariantCulture);
        var seconds = int.Parse(match.Groups["s"].Value, CultureInfo.InvariantCulture);
        var fraction = match.Groups["f"].Success ? match.Groups["f"].Value : "0";

        // ".5" = 500 ms, ".42" = 420 ms, ".123" = 123 ms.
        var milliseconds = int.Parse(fraction.PadRight(3, '0')[..3], CultureInfo.InvariantCulture);
        return new TimeSpan(0, 0, minutes, seconds, milliseconds);
    }

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

    [GeneratedRegex(@"^\[(?<m>\d{1,3}):(?<s>\d{1,2})(?:[.:](?<f>\d{1,3}))?\]")]
    private static partial Regex TimeTag();

    [GeneratedRegex(@"^\[offset:\s*([+-]?\d+)\s*\]$", RegexOptions.IgnoreCase)]
    private static partial Regex OffsetTag();
}
