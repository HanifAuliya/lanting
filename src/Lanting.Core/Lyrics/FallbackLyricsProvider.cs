using System.Runtime.ExceptionServices;

namespace Lanting.Core.Lyrics;

public sealed class FallbackLyricsProvider : ILyricsProvider
{
    private readonly ILyricsProvider[] _providers;

    public FallbackLyricsProvider(params ILyricsProvider[] providers)
    {
        _providers = providers;
    }

    public async Task<SyncedLyrics?> FindAsync(TrackQuery query, CancellationToken cancellationToken)
    {
        Exception? lastError = null;
        var failures = 0;

        foreach (var provider in _providers)
        {
            try
            {
                var lyrics = await provider.FindAsync(query, cancellationToken).ConfigureAwait(false);
                if (lyrics is not null)
                    return lyrics;
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

        return null;
    }
}
