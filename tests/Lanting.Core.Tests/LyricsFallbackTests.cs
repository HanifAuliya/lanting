using Lanting.Core.Lyrics;

namespace Lanting.Core.Tests;

public class LyricsFallbackTests
{
    private static readonly TrackQuery Query = new("happier", "Olivia Rodrigo", string.Empty, null);

    [Fact]
    public void NetEase_song_with_the_closest_duration_wins()
    {
        NetEaseClient.NetEaseSong[] songs =
        [
            new(1, "happier (live)", 219_000),
            new(2, "happier", 179_000),
            new(3, "happier", 175_933),
        ];

        var withDuration = NetEaseClient.Pick(songs, Query with { Duration = TimeSpan.FromSeconds(176) });
        var tooFar = NetEaseClient.Pick(songs, Query with { Duration = TimeSpan.FromSeconds(300) });
        var unknown = NetEaseClient.Pick(songs, Query);

        Assert.Equal(3, withDuration?.Id);
        Assert.Null(tooFar);
        Assert.Equal(1, unknown?.Id);
    }

    [Fact]
    public void NetEase_song_with_another_title_is_rejected()
    {
        NetEaseClient.NetEaseSong[] songs = [new(1, "再见青春", 238_400)];

        var result = NetEaseClient.Pick(songs, new("Bunga Pagi", "Tokoh Fiksi", string.Empty, TimeSpan.FromSeconds(238.4)));

        Assert.Null(result);
    }


    [Theory]
    [InlineData("作词 : Olivia Rodrigo", true)]
    [InlineData("作曲：Dan Nigro", true)]
    [InlineData("We broke up a month ago", false)]
    [InlineData("Chorus: you're happier", false)]
    public void NetEase_credit_lines_are_recognized(string text, bool isCredit)
    {
        Assert.Equal(isCredit, NetEaseClient.CreditLine().IsMatch(text));
    }

    [Fact]
    public async Task Next_source_is_used_when_the_first_finds_nothing()
    {
        var first = new FakeProvider(() => null);
        var second = new FakeProvider(() => Lyrics("netease"));

        var result = await new FallbackLyricsProvider(first, second).FindAsync(Query, CancellationToken.None);

        Assert.Equal("netease", result?.Lines[0].Text);
    }

    [Fact]
    public async Task Next_source_is_skipped_when_the_first_finds_lyrics()
    {
        var first = new FakeProvider(() => Lyrics("lrclib"));
        var second = new FakeProvider(() => Lyrics("netease"));

        var result = await new FallbackLyricsProvider(first, second).FindAsync(Query, CancellationToken.None);

        Assert.Equal("lrclib", result?.Lines[0].Text);
        Assert.Equal(0, second.Calls);
    }

    [Fact]
    public async Task Next_source_is_used_when_the_first_fails_or_times_out()
    {
        var down = new FakeProvider(() => throw new HttpRequestException("502"));
        var timeout = new FakeProvider(() => throw new TaskCanceledException());
        var second = new FakeProvider(() => Lyrics("netease"));

        var result = await new FallbackLyricsProvider(down, timeout, second).FindAsync(Query, CancellationToken.None);

        Assert.Equal("netease", result?.Lines[0].Text);
    }

    [Fact]
    public async Task Error_is_passed_on_when_every_source_fails()
    {
        var first = new FakeProvider(() => throw new HttpRequestException("502"));
        var second = new FakeProvider(() => throw new HttpRequestException("503"));

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => new FallbackLyricsProvider(first, second).FindAsync(Query, CancellationToken.None));

        Assert.Equal("503", error.Message);
    }

    [Fact]
    public async Task Not_found_wins_over_error()
    {
        var first = new FakeProvider(() => throw new HttpRequestException("502"));
        var second = new FakeProvider(() => null);

        var result = await new FallbackLyricsProvider(first, second).FindAsync(Query, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Cancelling_stops_without_trying_the_next_source()
    {
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        var first = new FakeProvider(() => Lyrics("lrclib"));
        var second = new FakeProvider(() => Lyrics("netease"));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => new FallbackLyricsProvider(first, second).FindAsync(Query, cancel.Token));

        Assert.Equal(0, second.Calls);
    }

    private static SyncedLyrics Lyrics(string text) => new([new LyricLine(TimeSpan.Zero, text)]);

    /// <summary>Sumber lirik palsu: jawabannya ditentukan oleh fungsi yang diberikan test.</summary>
    private sealed class FakeProvider : ILyricsProvider
    {
        private readonly Func<SyncedLyrics?> _answer;

        public FakeProvider(Func<SyncedLyrics?> answer)
        {
            _answer = answer;
        }

        public int Calls { get; private set; }

        public Task<SyncedLyrics?> FindAsync(TrackQuery query, CancellationToken cancellationToken)
        {
            Calls++;
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(_answer());
        }
    }
}
