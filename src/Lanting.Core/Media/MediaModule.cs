using Lanting.Core.Island;
using Lanting.Core.Modules;

namespace Lanting.Core.Media;

/// <summary>
/// Menampilkan lagu yang sedang diputar (Spotify, YouTube Music, dan pemutar lain yang terdaftar di SMTC)
/// sebagai konten latar di island.
/// </summary>
public sealed class MediaModule : IIslandModule
{
    public const string ModuleId = "media";
    public const string NowPlayingKey = "now-playing";

    private readonly IMediaSource _source;
    private readonly Lock _gate = new();
    private IIslandHost? _host;
    private NowPlaying? _shown;

    public MediaModule(IMediaSource source)
    {
        _source = source;
    }

    public string Id => ModuleId;

    public async Task StartAsync(IIslandHost host, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            _host = host;
        }

        _source.Changed += OnChanged;
        try
        {
            await _source.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _source.Changed -= OnChanged;
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
        await _source.StopAsync().ConfigureAwait(false);

        IIslandHost? host;
        lock (_gate)
        {
            host = _host;
            _host = null;
            _shown = null;
        }

        host?.Withdraw(ModuleId, NowPlayingKey);
    }

    private void OnChanged(object? sender, NowPlaying? nowPlaying)
    {
        IIslandHost? host;
        lock (_gate)
        {
            host = _host;
            if (host is null || nowPlaying == _shown)
            {
                return;
            }

            // Saat ganti lagu, pemutar sering mengirim judul kosong sesaat sebelum judul baru.
            // Abaikan supaya island tidak berkedip; tapi pemutar lain tanpa judul berarti memang tidak ada yang tampil.
            if (nowPlaying is not null && string.IsNullOrWhiteSpace(nowPlaying.Title))
            {
                if (_shown?.AppId == nowPlaying.AppId)
                {
                    return;
                }

                nowPlaying = null;
            }

            if (nowPlaying is null && _shown is null)
            {
                return;
            }

            _shown = nowPlaying;
        }

        if (nowPlaying is null)
        {
            host.Withdraw(ModuleId, NowPlayingKey);
        }
        else
        {
            host.Show(new IslandContent(ModuleId, NowPlayingKey, IslandPriority.Ambient, nowPlaying));
        }
    }
}
