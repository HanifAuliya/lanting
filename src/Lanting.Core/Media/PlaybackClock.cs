namespace Lanting.Core.Media;

/// <summary>
/// Posisi lagu yang sedang diputar, dihitung tanpa polling: dari titik acuan terakhir ditambah waktu yang sudah lewat
/// selama lagu diputar. Titik acuan diperbarui saat pemutar melapor posisi (seek, ganti lagu) atau saat putar/jeda.
/// </summary>
public sealed class PlaybackClock
{
    private readonly TimeProvider _time;
    private TimeSpan _base;
    private DateTimeOffset _baseAt;
    private bool _playing;

    public PlaybackClock(TimeProvider time)
    {
        _time = time;
        _baseAt = time.GetUtcNow();
    }

    public bool IsPlaying => _playing;

    public TimeSpan Position => _playing ? _base + (_time.GetUtcNow() - _baseAt) : _base;

    /// <summary>Mulai dari posisi tertentu, misalnya 0 saat lagu baru terlihat dimulai.</summary>
    public void Reset(TimeSpan position, bool playing)
    {
        _base = position;
        _baseAt = _time.GetUtcNow();
        _playing = playing;
    }

    /// <summary>Posisi yang dilaporkan pemutar pada waktu <paramref name="updatedAt"/> (bisa sedikit di masa lalu).</summary>
    public void Sync(TimeSpan position, DateTimeOffset updatedAt)
    {
        var now = _time.GetUtcNow();
        var elapsed = _playing && updatedAt < now ? now - updatedAt : TimeSpan.Zero;
        _base = position + elapsed;
        _baseAt = now;
    }

    public void SetPlaying(bool playing)
    {
        if (playing == _playing)
        {
            return;
        }

        _base = Position;
        _baseAt = _time.GetUtcNow();
        _playing = playing;
    }
}
