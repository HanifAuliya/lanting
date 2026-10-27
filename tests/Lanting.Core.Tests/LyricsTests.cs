using Lanting.Core.Lyrics;
using Lanting.Core.Media;
using Microsoft.Extensions.Time.Testing;

namespace Lanting.Core.Tests;

public class LyricsTests
{
    [Fact]
    public void Lrc_lines_are_parsed_and_sorted()
    {
        var lyrics = LrcParser.Parse("""
            [ar:Olivia Rodrigo]
            [00:08.57]Your friends aren't mine
            [00:04.64]We broke up a month ago
            [01:02.5]
            """);

        Assert.NotNull(lyrics);
        Assert.Equal(
            [
                new LyricLine(TimeSpan.FromMilliseconds(4640), "We broke up a month ago"),
                new LyricLine(TimeSpan.FromMilliseconds(8570), "Your friends aren't mine"),
                new LyricLine(TimeSpan.FromMilliseconds(62500), string.Empty),
            ],
            lyrics.Lines);
    }

    [Fact]
    public void Repeated_chorus_with_several_timestamps_appears_at_each_time()
    {
        var lyrics = LrcParser.Parse("[00:10.00][01:10.00]Reff");

        Assert.Equal([TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(70)], lyrics!.Lines.Select(l => l.Time));
    }

    [Fact]
    public void Positive_offset_shows_lyrics_earlier()
    {
        var lyrics = LrcParser.Parse("[offset:+500]\n[00:02.00]Baris");

        Assert.Equal(TimeSpan.FromSeconds(1.5), lyrics!.Lines[0].Time);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Lirik tanpa tanda waktu")]
    public void Text_without_timestamps_is_not_synced_lyrics(string? lrc)
    {
        Assert.Null(LrcParser.Parse(lrc));
    }

    [Fact]
    public void Index_is_the_last_line_that_has_started()
    {
        var lyrics = LrcParser.Parse("[00:01.00]a\n[00:03.00]b\n[00:05.00]c")!;

        Assert.Equal(-1, lyrics.IndexAt(TimeSpan.FromSeconds(0.5)));
        Assert.Equal(0, lyrics.IndexAt(TimeSpan.FromSeconds(1)));
        Assert.Equal(1, lyrics.IndexAt(TimeSpan.FromSeconds(4.9)));
        Assert.Equal(2, lyrics.IndexAt(TimeSpan.FromMinutes(9)));
    }

    [Theory]
    [InlineData("happier", "Olivia Rodrigo", "happier", "Olivia Rodrigo")]
    [InlineData("Olivia Rodrigo - happier (Lyric Video)", "OliviaRodrigoVEVO", "happier", "Olivia Rodrigo")]
    [InlineData("Alex Crichton - What If I Call", "Alex Crichton", "What If I Call", "Alex Crichton")]
    [InlineData("happy", "Skinnyfabs - Topic", "happy", "Skinnyfabs")]
    [InlineData("blue (official video)", "yung kai", "blue", "yung kai")]
    [InlineData("Someone Like You [Official Music Video]", "Adele", "Someone Like You", "Adele")]
    [InlineData("Lagu - Bagian Dua", "Band Lain", "Lagu - Bagian Dua", "Band Lain")]
    [InlineData("Artis - Judul | Official Video", "", "Judul", "Artis")]
    public void Track_titles_are_cleaned_for_search(string title, string artist, string expectedTitle, string expectedArtist)
    {
        var query = TrackQuery.Create(title, artist, string.Empty, null);

        Assert.NotNull(query);
        Assert.Equal(expectedTitle, query.Title);
        Assert.Equal(expectedArtist, query.Artist);
    }

    [Theory]
    [InlineData("ANDMESH  - ANUGERAH TERINDAH (OFFICIAL MUSIC VIDEO)", "HITS Records", "ANDMESH ANUGERAH TERINDAH")]
    [InlineData("happier", "Olivia Rodrigo", "Olivia Rodrigo happier")]
    public void Free_text_search_ignores_label_channels(string title, string artist, string expected)
    {
        Assert.Equal(expected, TrackQuery.Create(title, artist, string.Empty, null)!.SearchText);
    }

    [Fact]
    public void Track_without_artist_or_title_cannot_be_searched()
    {
        Assert.Null(TrackQuery.Create("Judul tanpa artis", string.Empty, string.Empty, null));
        Assert.Null(TrackQuery.Create(" ", "Artis", string.Empty, null));
    }

    [Fact]
    public void Result_with_the_closest_duration_wins()
    {
        LrcLibClient.LrcLibTrack[] results =
        [
            new("Happier", "Olivia Rodrigo", 219, false, "[00:01.00]live"),
            new("happier", "Olivia Rodrigo", 176, false, null),
            new("happier", "Olivia Rodrigo", 179, false, "[00:01.00]video"),
            new("happier", "Olivia Rodrigo", 177, false, "[00:01.00]album"),
        ];

        var withDuration = LrcLibClient.Pick(results, new("happier", "Olivia Rodrigo", string.Empty, TimeSpan.FromSeconds(176)));
        var tooFar = LrcLibClient.Pick(results, new("happier", "Olivia Rodrigo", string.Empty, TimeSpan.FromSeconds(300)));
        var unknown = LrcLibClient.Pick(results, new("happier", "Olivia Rodrigo", string.Empty, null));

        Assert.Equal("[00:01.00]album", withDuration?.SyncedLyrics);
        Assert.Null(tooFar);
        Assert.Equal("[00:01.00]live", unknown?.SyncedLyrics);
    }

    [Fact]
    public void Clock_runs_only_while_playing()
    {
        var time = new FakeTimeProvider();
        var clock = new PlaybackClock(time);
        clock.Reset(TimeSpan.FromSeconds(10), playing: true);

        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(TimeSpan.FromSeconds(15), clock.Position);

        clock.SetPlaying(false);
        time.Advance(TimeSpan.FromSeconds(30));
        Assert.Equal(TimeSpan.FromSeconds(15), clock.Position);

        clock.SetPlaying(true);
        time.Advance(TimeSpan.FromSeconds(1));
        Assert.Equal(TimeSpan.FromSeconds(16), clock.Position);
    }

    [Fact]
    public void Clock_sync_accounts_for_report_age_while_playing()
    {
        var time = new FakeTimeProvider();
        var clock = new PlaybackClock(time);
        clock.Reset(TimeSpan.Zero, playing: true);
        var reportedAt = time.GetUtcNow();
        time.Advance(TimeSpan.FromSeconds(2));

        clock.Sync(TimeSpan.FromSeconds(60), reportedAt);

        Assert.Equal(TimeSpan.FromSeconds(62), clock.Position);
    }
}
