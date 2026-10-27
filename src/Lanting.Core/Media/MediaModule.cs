using Lanting.Core.Island;
using Lanting.Core.Lyrics;
using Lanting.Core.Modules;

namespace Lanting.Core.Media;

/// <summary>
/// Menampilkan lagu yang sedang diputar (Spotify, YouTube Music, dan pemutar lain yang terdaftar di SMTC)
/// sebagai konten latar di island, lengkap dengan baris lirik yang sedang dinyanyikan bila ada.
/// </summary>
/// <remarks>
/// Lirik maju tanpa polling: setiap kali baris berganti, satu timer dipasang tepat untuk baris berikutnya.
/// Posisi lagu diambil dari pemutar; bila pemutar tidak memberi posisi (Firefox/Zen), posisi dihitung sendiri
/// sejak lagu terlihat dimulai. Cara kedua ini tidak tahu kalau lagu di-seek.
/// </remarks>
public sealed class MediaModule : IIslandModule
{
    public const string ModuleId = "media";
    public const string NowPlayingKey = "now-playing";

    /// <summary>Ditampilkan untuk baris kosong di LRC (bagian instrumental).</summary>
    public const string InstrumentalLyric = "♪";

    /// <summary>
    /// Jeda sebelum mencari lirik lagu baru: memberi waktu pemutar melaporkan durasi (untuk memilih versi lagu
    /// yang cocok), dan lagu yang langsung di-skip tidak ikut dicari.
    /// </summary>
    public static readonly TimeSpan LyricsSearchDelay = TimeSpan.FromMilliseconds(400);

    private readonly IMediaSource _source;
    private readonly ILyricsProvider _lyricsProvider;
    private readonly TimeProvider _time;
    private readonly PlaybackClock _clock;
    private readonly Lock _gate = new();
    private IIslandHost? _host;
    private NowPlaying? _song;
    private NowPlaying? _shown;
    private SyncedLyrics? _lyrics;
    private bool _positionKnown;
    private bool _lyricsEnabled = true;
    private ITimer? _lineTimer;
    private ITimer? _searchTimer;
    private TimeSpan? _duration;
    private CancellationTokenSource? _fetch;

    public MediaModule(IMediaSource source, ILyricsProvider lyricsProvider, TimeProvider time)
    {
        _source = source;
        _lyricsProvider = lyricsProvider;
        _time = time;
        _clock = new PlaybackClock(time);
    }

    /// <summary>Pencarian lirik gagal (misalnya tidak ada internet). Lagu tetap tampil tanpa lirik.</summary>
    public event EventHandler<Exception>? LyricsFailed;

    public string Id => ModuleId;

    /// <summary>Cari dan tampilkan lirik. Bisa diubah kapan saja dari pengaturan.</summary>
    public bool LyricsEnabled
    {
        get
        {
            lock (_gate)
            {
                return _lyricsEnabled;
            }
        }

        set
        {
            lock (_gate)
            {
                if (_lyricsEnabled == value)
                {
                    return;
                }

                _lyricsEnabled = value;
                if (_host is null || _song is null)
                {
                    return;
                }

                if (value)
                {
                    StartSearchTimer();
                }
                else
                {
                    _fetch?.Cancel();
                    _fetch = null;
                    _lyrics = null;
                }

                Publish();
            }
        }
    }

    public async Task StartAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _host = host;
        }

        _source.Changed += OnChanged;
        _source.TimelineChanged += OnTimelineChanged;
        try
        {
            await _source.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _source.Changed -= OnChanged;
            _source.TimelineChanged -= OnTimelineChanged;
            lock (_gate)
            {
                _host = null;
            }

            throw;
        }
    }

    public async Task StopAsync()
    {
        _source.Changed -= OnChanged;
        _source.TimelineChanged -= OnTimelineChanged;
        await _source.StopAsync().ConfigureAwait(false);

        lock (_gate)
        {
            ForgetTrack();
            if (_shown is not null)
            {
                _host?.Withdraw(ModuleId, NowPlayingKey);
            }

            _host = null;
            _song = null;
            _shown = null;
        }
    }

    private void OnChanged(object? sender, NowPlaying? nowPlaying)
    {
        lock (_gate)
        {
            if (_host is null || nowPlaying == _song)
            {
                return;
            }

            // Saat ganti lagu, pemutar sering mengirim judul kosong sesaat sebelum judul baru.
            // Abaikan supaya island tidak berkedip; tapi pemutar lain tanpa judul berarti memang tidak ada yang tampil.
            if (nowPlaying is not null && string.IsNullOrWhiteSpace(nowPlaying.Title))
            {
                if (_song?.AppId == nowPlaying.AppId)
                {
                    return;
                }

                nowPlaying = null;
            }

            var previous = _song;
            _song = nowPlaying;

            if (nowPlaying is null)
            {
                ForgetTrack();
            }
            else if (previous is null || !SameTrack(previous, nowPlaying))
            {
                ForgetTrack();

                // Lagu berganti saat kita mengamati: anggap mulai dari 0 sampai pemutar memberi posisi sebenarnya.
                // Saat Lanting baru dinyalakan di tengah lagu, posisinya belum diketahui.
                _positionKnown = previous is not null;
                _clock.Reset(TimeSpan.Zero, nowPlaying.IsPlaying);
                StartSearchTimer();
            }
            else
            {
                _clock.SetPlaying(nowPlaying.IsPlaying);
            }

            Publish();
        }
    }

    private void OnTimelineChanged(object? sender, MediaTimeline? timeline)
    {
        lock (_gate)
        {
            if (_host is null || _song is null || timeline is null || timeline.Duration <= TimeSpan.Zero)
            {
                return;
            }

            _clock.Sync(timeline.Position, timeline.UpdatedAt);
            _positionKnown = true;
            _duration = timeline.Duration;
            Publish();
        }
    }

    // Dipanggil di dalam _gate.
    private void StartSearchTimer()
    {
        _searchTimer?.Dispose();
        _searchTimer = _lyricsEnabled
            ? _time.CreateTimer(_ => OnSearchTimer(), null, LyricsSearchDelay, Timeout.InfiniteTimeSpan)
            : null;
    }

    private void OnSearchTimer()
    {
        lock (_gate)
        {
            _searchTimer?.Dispose();
            _searchTimer = null;
            if (_host is null || _song is null || !_lyricsEnabled)
            {
                return;
            }

            var query = TrackQuery.Create(_song.Title, _song.Artist, _song.Album, _duration);
            if (query is not null)
            {
                FetchLyrics(query);
            }
        }
    }

    // Dipanggil di dalam _gate.
    private void FetchLyrics(TrackQuery query)
    {
        var fetch = new CancellationTokenSource();
        _fetch = fetch;
        _ = FetchLyricsAsync(query, fetch);
    }

    private async Task FetchLyricsAsync(TrackQuery query, CancellationTokenSource fetch)
    {
        SyncedLyrics? lyrics;
        try
        {
            lyrics = await _lyricsProvider.FindAsync(query, fetch.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (fetch.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            LyricsFailed?.Invoke(this, ex);
            return;
        }

        lock (_gate)
        {
            // Lagu sudah berganti selama mencari: hasil ini untuk lagu lama.
            if (fetch.IsCancellationRequested || _host is null || !_lyricsEnabled)
            {
                return;
            }

            _lyrics = lyrics;
            Publish();
        }
    }

    // Dipanggil di dalam _gate.
    private void ForgetTrack()
    {
        _fetch?.Cancel();
        _fetch = null;
        _searchTimer?.Dispose();
        _searchTimer = null;
        _duration = null;
        _lineTimer?.Dispose();
        _lineTimer = null;
        _lyrics = null;
        _positionKnown = false;
    }

    // Dipanggil di dalam _gate. Host dipanggil di dalam lock supaya urutan update dari beberapa thread
    // (event pemutar, timer lirik, hasil pencarian) tidak tertukar; host tidak pernah memanggil balik modul.
    private void Publish()
    {
        _lineTimer?.Dispose();
        _lineTimer = null;

        if (_song is null)
        {
            if (_shown is not null)
            {
                _shown = null;
                _host?.Withdraw(ModuleId, NowPlayingKey);
            }

            return;
        }

        var hasLyrics = _lyrics is not null && _positionKnown;
        string? lyric = null;
        if (hasLyrics)
        {
            var position = _clock.Position;
            var index = _lyrics!.IndexAt(position);
            if (index >= 0)
            {
                var text = _lyrics.Lines[index].Text;
                lyric = text.Length == 0 ? InstrumentalLyric : text;
            }

            // Pasang timer tepat untuk baris berikutnya; saat dijeda tidak perlu, nanti dipasang ulang saat diputar.
            if (_clock.IsPlaying && index + 1 < _lyrics.Lines.Count)
            {
                var due = _lyrics.Lines[index + 1].Time - position;
                _lineTimer = _time.CreateTimer(_ => OnLineTimer(), null, Max(due, TimeSpan.Zero), Timeout.InfiniteTimeSpan);
            }
        }

        var shown = _song with { Lyric = lyric, HasLyrics = hasLyrics };
        if (shown == _shown)
        {
            return;
        }

        _shown = shown;
        _host?.Show(new IslandContent(ModuleId, NowPlayingKey, IslandPriority.Ambient, shown));
    }

    private void OnLineTimer()
    {
        lock (_gate)
        {
            if (_host is not null)
            {
                Publish();
            }
        }
    }

    private static bool SameTrack(NowPlaying a, NowPlaying b) =>
        a.AppId == b.AppId && a.Title == b.Title && a.Artist == b.Artist;

    private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
