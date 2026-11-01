using System.Runtime.ExceptionServices;

namespace Lanting.Core.Lyrics;

public sealed class FallbackLyricsProvider : ILyricsProvider
{
    private const int CacheSize = 50;

    private readonly ILyricsProvider[] _providers;
    private readonly Lock _gate = new();
    private readonly Dictionary<TrackQuery, SyncedLyrics?> _cache = [];
    private readonly Queue<TrackQuery> _cacheOrder = new();

    public FallbackLyricsProvider(params ILyricsProvider[] providers)
    {
        _providers = providers;
    }

    public async Task<SyncedLyrics?> FindAsync(TrackQuery query, CancellationToken cancellationToken)
    {
        lock (_gate)
        {
            if (_cache.TryGetValue(query, out var cached))
            {
                return cached;
            }
        }

        Exception? lastError = null;
        var failures = 0;

        foreach (var provider in _providers)
        {
            try
            {
                var lyrics = await provider.FindAsync(query, cancellationToken).ConfigureAwait(false);
                if (lyrics is not null)
                {
                    Remember(query, lyrics);
                    return lyrics;
                }
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
                failures++;
            }
        }

        if (lastError is not null && failures == _providers.Length)
        {
            ExceptionDispatchInfo.Throw(lastError);
        }

        if (failures == 0)
        {
            Remember(query, null);
        }

        return null;
    }

    private void Remember(TrackQuery query, SyncedLyrics? lyrics)
    {
        lock (_gate)
        {
            if (_cache.TryAdd(query, lyrics))
            {
                _cacheOrder.Enqueue(query);
                if (_cacheOrder.Count > CacheSize)
                {
                    _cache.Remove(_cacheOrder.Dequeue());
                }
            }
        }
    }
}
