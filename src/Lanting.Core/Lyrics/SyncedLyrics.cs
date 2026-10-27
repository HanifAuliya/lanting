namespace Lanting.Core.Lyrics;

/// <summary>Satu baris lirik dan waktu mulainya dihitung dari awal lagu.</summary>
public sealed record LyricLine(TimeSpan Time, string Text);

/// <summary>Lirik bertanda waktu, urut dari awal ke akhir lagu.</summary>
public sealed class SyncedLyrics
{
    public SyncedLyrics(IEnumerable<LyricLine> lines)
    {
        Lines = [.. lines.OrderBy(l => l.Time)];
    }

    public IReadOnlyList<LyricLine> Lines { get; }

    /// <summary>Indeks baris yang sedang dinyanyikan pada <paramref name="position"/>, atau -1 sebelum baris pertama.</summary>
    public int IndexAt(TimeSpan position)
    {
        // Cari baris terakhir yang waktunya sudah lewat (binary search).
        int low = 0, high = Lines.Count - 1, found = -1;
        while (low <= high)
        {
            var mid = (low + high) / 2;
            if (Lines[mid].Time <= position)
            {
                found = mid;
                low = mid + 1;
            }
            else
            {
                high = mid - 1;
            }
        }

        return found;
    }
}
