using Lanting.Core.Lyrics;
using Lanting.Core.Media;
using Microsoft.Extensions.Time.Testing;
using static Lanting.Core.Tests.MediaModuleTests;

namespace Lanting.Core.Tests;

public class MediaLyricsTests
{
    private readonly FakeTimeProvider _time = new();
    private readonly FakeSource _source = new();
    private readonly RecordingHost _host = new();
    private readonly FakeLyrics _lyrics = new();

    private static readonly SyncedLyrics Lagu = LrcParser.Parse("""
        [00:01.00]Baris satu
        [00:03.00]
        [00:05.00]Baris dua
        """)!;

    [Fact]
    public async Task Lyrics_follow_the_position_reported_by_the_player()
    {
        var module = await StartAsync();
        _source.Raise(Song("Spotify.exe", "Lagu A"));
        _source.RaiseTimeline(new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), _time.GetUtcNow()));
        WaitForSearch();
        _lyrics.Complete(Lagu);

        Assert.True(Last.HasLyrics);
        Assert.Null(Last.Lyric);

        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal("Baris satu", Last.Lyric);

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal(MediaModule.InstrumentalLyric, Last.Lyric);

        _time.Advance(TimeSpan.FromSeconds(2));
        Assert.Equal("Baris dua", Last.Lyric);
        await module.StopAsync();
    }

    [Fact]
    public async Task Seek_reported_by_the_player_jumps_to_the_right_line()
    {
        await StartAsync();
        _source.Raise(Song("Spotify.exe", "Lagu A"));
        _source.RaiseTimeline(new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), _time.GetUtcNow()));
        WaitForSearch();
        _lyrics.Complete(Lagu);

        _source.RaiseTimeline(new MediaTimeline(TimeSpan.FromSeconds(6), TimeSpan.FromMinutes(3), _time.GetUtcNow()));

        Assert.Equal("Baris dua", Last.Lyric);
    }

    [Fact]
    public async Task Paused_song_does_not_advance_lyrics()
    {
        await StartAsync();
        _source.Raise(Song("Spotify.exe", "Lagu A"));
        _source.RaiseTimeline(new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(3), _time.GetUtcNow()));
        WaitForSearch();
        _lyrics.Complete(Lagu);
        _time.Advance(TimeSpan.FromSeconds(2));

        _source.Raise(Song("Spotify.exe", "Lagu A") with { IsPlaying = false });
        _time.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal("Baris satu", Last.Lyric);

        _source.Raise(Song("Spotify.exe", "Lagu A"));
        _time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(MediaModule.InstrumentalLyric, Last.Lyric);
    }

    [Fact]
    public async Task Without_player_position_a_song_seen_starting_is_timed_from_zero()
    {
        await StartAsync();
        _source.Raise(Song("Zen", "Lagu A"));
        WaitForSearch();
        _lyrics.Complete(null);

        // Zen tidak memberi posisi; Lagu B terlihat dimulai, jadi dihitung dari 0.
        _source.Raise(Song("Zen", "Lagu B"));
        _source.RaiseTimeline(new MediaTimeline(TimeSpan.Zero, TimeSpan.Zero, _time.GetUtcNow()));
        WaitForSearch();
        _lyrics.Complete(Lagu);
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal("Lagu B", Last.Title);
        Assert.Equal("Baris dua", Last.Lyric);
    }

    [Fact]
    public async Task Song_already_playing_at_startup_without_position_shows_no_lyrics()
    {
        await StartAsync();
        _source.Raise(Song("Zen", "Lagu A"));
        WaitForSearch();
        _lyrics.Complete(Lagu);
        _time.Advance(TimeSpan.FromSeconds(5));

        Assert.False(Last.HasLyrics);
        Assert.Null(Last.Lyric);
    }

    [Fact]
    public async Task Lyrics_arriving_for_a_previous_song_are_ignored()
    {
        await StartAsync();
        _source.Raise(Song("Spotify.exe", "Lagu A"));
        WaitForSearch();
        var oldFetch = _lyrics.Pending!;
        _source.Raise(Song("Spotify.exe", "Lagu B"));

        Resolve(oldFetch, Lagu);

        Assert.Equal("Lagu B", Last.Title);
        Assert.False(Last.HasLyrics);
    }

    [Fact]
    public async Task Quickly_skipped_songs_are_not_searched()
    {
        await StartAsync();

        _source.Raise(Song("Spotify.exe", "Lagu A"));
        _time.Advance(TimeSpan.FromMilliseconds(100));
        _source.Raise(Song("Spotify.exe", "Lagu B"));
        WaitForSearch();

        Assert.Equal("Lagu B", _lyrics.LastQuery?.Title);
        Assert.Equal(1, _lyrics.Searches);
    }

    [Fact]
    public async Task Lyrics_search_uses_the_cleaned_title()
    {
        await StartAsync();

        _source.Raise(Song("Zen", "Olivia Rodrigo - happier (Lyric Video)") with { Artist = "OliviaRodrigoVEVO" });
        _source.RaiseTimeline(new MediaTimeline(TimeSpan.Zero, TimeSpan.FromSeconds(179), _time.GetUtcNow()));
        WaitForSearch();

        Assert.Equal(new TrackQuery("happier", "Olivia Rodrigo", "Album", TimeSpan.FromSeconds(179)), _lyrics.LastQuery);
    }

    private NowPlaying Last => (NowPlaying)_host.Shown[^1].Payload!;

    private async Task<MediaModule> StartAsync()
    {
        var module = new MediaModule(_source, _lyrics, _time);
        await module.StartAsync(_host, CancellationToken.None);
        return module;
    }

    private sealed class FakeLyrics : ILyricsProvider
    {
        public TaskCompletionSource<SyncedLyrics?>? Pending { get; private set; }

        public TrackQuery? LastQuery { get; private set; }

        public int Searches { get; private set; }

        public Task<SyncedLyrics?> FindAsync(TrackQuery query, CancellationToken cancellationToken)
        {
            LastQuery = query;
            Searches++;
            Pending = new TaskCompletionSource<SyncedLyrics?>();
            return Pending.Task;
        }

        public void Complete(SyncedLyrics? lyrics) => Resolve(Pending!, lyrics);
    }

    // Pencarian lirik baru dimulai setelah jeda singkat.
    private void WaitForSearch()
    {
        _time.Advance(MediaModule.LyricsSearchDelay);
    }

    // .NET hanya menjalankan lanjutan await langsung di dalam TrySetResult bila thread ini tidak punya
    // SynchronizationContext khusus. xUnit memasang satu, jadi dilepas sesaat; setelah method ini kembali,
    // hasil pencarian sudah diproses modul.
    private static void Resolve(TaskCompletionSource<SyncedLyrics?> pending, SyncedLyrics? lyrics)
    {
        var context = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(null);
        try
        {
            pending.TrySetResult(lyrics);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(context);
        }
    }
}
