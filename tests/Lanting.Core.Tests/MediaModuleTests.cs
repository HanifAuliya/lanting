using Lanting.Core.Island;
using Lanting.Core.Media;

namespace Lanting.Core.Tests;

public class MediaModuleTests
{
    private readonly FakeSource _source = new();
    private readonly RecordingHost _host = new();

    [Fact]
    public async Task Playing_song_is_shown_as_ambient_content()
    {
        var module = new MediaModule(_source);
        await module.StartAsync(_host, CancellationToken.None);
        var song = Song("Spotify.exe", "Lagu A");

        _source.Raise(song);

        var shown = Assert.Single(_host.Shown);
        Assert.Equal(MediaModule.ModuleId, shown.ModuleId);
        Assert.Equal(MediaModule.NowPlayingKey, shown.Key);
        Assert.Equal(IslandPriority.Ambient, shown.Priority);
        Assert.Null(shown.Duration);
        Assert.Same(song, shown.Payload);
    }

    [Fact]
    public async Task No_player_withdraws_the_song()
    {
        var module = new MediaModule(_source);
        await module.StartAsync(_host, CancellationToken.None);
        _source.Raise(Song("Spotify.exe", "Lagu A"));

        _source.Raise(null);

        Assert.Equal(["media/now-playing"], _host.Withdrawn);
    }

    [Fact]
    public async Task Identical_update_is_not_shown_twice()
    {
        var module = new MediaModule(_source);
        await module.StartAsync(_host, CancellationToken.None);
        var art = new byte[] { 1, 2, 3 };

        _source.Raise(Song("Spotify.exe", "Lagu A") with { Thumbnail = art });
        _source.Raise(Song("Spotify.exe", "Lagu A") with { Thumbnail = art });
        _source.Raise(Song("Spotify.exe", "Lagu A") with { Thumbnail = art, IsPlaying = false });

        Assert.Equal(2, _host.Shown.Count);
    }

    [Fact]
    public async Task Empty_title_while_changing_tracks_keeps_the_old_song()
    {
        var module = new MediaModule(_source);
        await module.StartAsync(_host, CancellationToken.None);
        _source.Raise(Song("Spotify.exe", "Lagu A"));

        _source.Raise(Song("Spotify.exe", string.Empty));
        _source.Raise(Song("Spotify.exe", "Lagu B"));

        Assert.Equal(["Lagu A", "Lagu B"], _host.Shown.Select(c => ((NowPlaying)c.Payload!).Title));
        Assert.Empty(_host.Withdrawn);
    }

    [Fact]
    public async Task Empty_title_from_another_app_withdraws()
    {
        var module = new MediaModule(_source);
        await module.StartAsync(_host, CancellationToken.None);
        _source.Raise(Song("Spotify.exe", "Lagu A"));

        _source.Raise(Song("MSEdge", " "));

        Assert.Equal(["media/now-playing"], _host.Withdrawn);
    }

    [Fact]
    public async Task Nothing_to_withdraw_when_nothing_was_shown()
    {
        var module = new MediaModule(_source);
        await module.StartAsync(_host, CancellationToken.None);

        _source.Raise(null);
        _source.Raise(Song("MSEdge", string.Empty));

        Assert.Empty(_host.Shown);
        Assert.Empty(_host.Withdrawn);
    }

    [Fact]
    public async Task Stop_withdraws_and_ignores_later_updates()
    {
        var module = new MediaModule(_source);
        await module.StartAsync(_host, CancellationToken.None);
        _source.Raise(Song("Spotify.exe", "Lagu A"));

        await module.StopAsync();
        _source.Raise(Song("Spotify.exe", "Lagu B"));

        Assert.True(_source.Stopped);
        Assert.Equal(["media/now-playing"], _host.Withdrawn);
        Assert.Single(_host.Shown);
    }

    [Fact]
    public async Task Failed_start_does_not_leave_the_module_listening()
    {
        _source.FailOnStart = true;
        var module = new MediaModule(_source);

        await Assert.ThrowsAsync<InvalidOperationException>(() => module.StartAsync(_host, CancellationToken.None));
        _source.Raise(Song("Spotify.exe", "Lagu A"));

        Assert.Empty(_host.Shown);
    }

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("MSEdge", "Edge")]
    [InlineData("Chrome", "Chrome")]
    [InlineData("music.youtube.com-1A2B3C", "YouTube Music")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Media Player")]
    [InlineData("Contoso.Player_abc123!PlayerApp", "PlayerApp")]
    [InlineData(@"C:\Program Files\Foo\foobar2000.exe", "foobar2000")]
    [InlineData("", "")]
    public void App_names_are_readable(string appId, string expected)
    {
        Assert.Equal(expected, MediaApps.DisplayName(appId));
    }

    private static NowPlaying Song(string appId, string title) =>
        new(appId, MediaApps.DisplayName(appId), title, "Artis", "Album", IsPlaying: true, CanPlayPause: true, CanNext: true, CanPrevious: true);

    private sealed class FakeSource : IMediaSource
    {
        public event EventHandler<NowPlaying?>? Changed;

        public bool FailOnStart { get; set; }

        public bool Stopped { get; private set; }

        public void Raise(NowPlaying? nowPlaying) => Changed?.Invoke(this, nowPlaying);

        public Task StartAsync(CancellationToken cancellationToken) =>
            FailOnStart ? throw new InvalidOperationException("SMTC tidak ada") : Task.CompletedTask;

        public Task StopAsync()
        {
            Stopped = true;
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingHost : IIslandHost
    {
        public List<IslandContent> Shown { get; } = [];

        public List<string> Withdrawn { get; } = [];

        public void Show(IslandContent content) => Shown.Add(content);

        public void Withdraw(string moduleId, string key) => Withdrawn.Add($"{moduleId}/{key}");
    }
}
