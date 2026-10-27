using Lanting.App.Shell;
using Lanting.Core.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;
using PlaybackStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;
using Session = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using SessionManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;

namespace Lanting.App.Media;

/// <summary>
/// Membaca lagu yang sedang diputar dari System Media Transport Controls (SMTC), yaitu sumber yang sama
/// dengan kotak media saat tombol volume ditekan. Spotify, YouTube Music di browser, dan pemutar lain
/// otomatis terdeteksi. Semua berbasis event Windows, tanpa polling.
/// </summary>
internal sealed class SmtcMediaSource : IMediaSource, IMediaControls
{
    // Gambar album lebih besar dari ini dilewati supaya memori tetap kecil.
    private const int MaxThumbnailBytes = 2 * 1024 * 1024;

    // Pemutar kadang memicu event sebelum datanya benar-benar diganti (judul baru, gambar lama).
    // Karena itu properti dibaca sekali lagi sesaat setelah event terakhir.
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(700);

    // Satu refresh pada satu waktu; event SMTC sering datang beruntun dari beberapa thread.
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Lock _gate = new();

    // Hanya disentuh di dalam _refreshGate.
    private readonly Dictionary<string, string> _appNames = [];
    private string _thumbnailTrack = string.Empty;
    private byte[]? _thumbnailBytes;

    private SessionManager? _manager;
    private Session? _session;
    private NowPlaying? _last;
    private bool _propertiesChanged = true;
    private CancellationTokenSource? _settle;
    private bool _stopped;

    public event EventHandler<NowPlaying?>? Changed;

    public event EventHandler<MediaTimeline?>? TimelineChanged;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var manager = await SessionManager.RequestAsync().AsTask(cancellationToken).ConfigureAwait(false);
        lock (_gate)
        {
            _manager = manager;
            _stopped = false;
        }

        manager.CurrentSessionChanged += OnCurrentSessionChanged;
        AttachCurrentSession();
        await RefreshAsync().ConfigureAwait(false);
        await RefreshTimelineAsync().ConfigureAwait(false);
    }

    public Task StopAsync()
    {
        lock (_gate)
        {
            _stopped = true;
            if (_manager is not null)
            {
                _manager.CurrentSessionChanged -= OnCurrentSessionChanged;
                _manager = null;
            }

            Detach(_session);
            _session = null;
            _settle?.Cancel();
            _settle = null;
        }

        return Task.CompletedTask;
    }

    public Task PlayPauseAsync() => SendAsync(s => s.TryTogglePlayPauseAsync().AsTask(), "play/pause");

    public Task NextAsync() => SendAsync(s => s.TrySkipNextAsync().AsTask(), "next");

    public Task PreviousAsync() => SendAsync(s => s.TrySkipPreviousAsync().AsTask(), "previous");

    private async Task SendAsync(Func<Session, Task<bool>> command, string name)
    {
        Session? session;
        lock (_gate)
        {
            session = _session;
        }

        if (session is null)
        {
            return;
        }

        try
        {
            if (!await command(session).ConfigureAwait(false))
            {
                StartupLog.Write($"Media: pemutar menolak perintah {name}.");
            }
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Media: perintah {name} gagal: {ex.Message}");
        }
    }

    private void OnCurrentSessionChanged(SessionManager sender, CurrentSessionChangedEventArgs args)
    {
        AttachCurrentSession();
        OnPropertiesChanged();
        _ = RefreshTimelineAsync();
    }

    private void OnMediaPropertiesChanged(Session sender, MediaPropertiesChangedEventArgs args) => OnPropertiesChanged();

    private void OnPlaybackInfoChanged(Session sender, PlaybackInfoChangedEventArgs args) => _ = RefreshAsync();

    private void OnTimelinePropertiesChanged(Session sender, TimelinePropertiesChangedEventArgs args) => _ = RefreshTimelineAsync();

    private void OnPropertiesChanged()
    {
        CancellationToken token;
        lock (_gate)
        {
            if (_stopped)
            {
                return;
            }

            _propertiesChanged = true;
            _settle?.Cancel();
            _settle = new CancellationTokenSource();
            token = _settle.Token;
        }

        _ = RefreshAsync();
        _ = SettleAsync(token);
    }

    private async Task SettleAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(SettleDelay, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        lock (_gate)
        {
            _propertiesChanged = true;
        }

        await RefreshAsync().ConfigureAwait(false);
    }

    private void AttachCurrentSession()
    {
        lock (_gate)
        {
            if (_stopped || _manager is null)
            {
                return;
            }

            var current = _manager.GetCurrentSession();
            if (current is not null && _session is not null && current.SourceAppUserModelId == _session.SourceAppUserModelId)
            {
                return;
            }

            Detach(_session);
            _session = current;
            if (current is not null)
            {
                current.MediaPropertiesChanged += OnMediaPropertiesChanged;
                current.PlaybackInfoChanged += OnPlaybackInfoChanged;
                current.TimelinePropertiesChanged += OnTimelinePropertiesChanged;
            }

            StartupLog.Write($"Media: sesi aktif {current?.SourceAppUserModelId ?? "(tidak ada)"}");
        }
    }

    private void Detach(Session? session)
    {
        if (session is null)
        {
            return;
        }

        session.MediaPropertiesChanged -= OnMediaPropertiesChanged;
        session.PlaybackInfoChanged -= OnPlaybackInfoChanged;
        session.TimelinePropertiesChanged -= OnTimelinePropertiesChanged;
    }

    private async Task RefreshAsync()
    {
        try
        {
            await _refreshGate.WaitAsync().ConfigureAwait(false);
            try
            {
                Session? session;
                bool propertiesChanged;
                lock (_gate)
                {
                    if (_stopped)
                    {
                        return;
                    }

                    session = _session;
                    propertiesChanged = _propertiesChanged;
                    _propertiesChanged = false;
                }

                var next = session is null ? null : await ReadAsync(session, propertiesChanged).ConfigureAwait(false);

                lock (_gate)
                {
                    // Sesi bisa sudah berganti selama membaca; refresh berikutnya yang akan mengirim.
                    if (_stopped || !ReferenceEquals(session, _session) || next == _last)
                    {
                        return;
                    }

                    _last = next;
                }

                Changed?.Invoke(this, next);
            }
            finally
            {
                _refreshGate.Release();
            }
        }
        catch (Exception ex)
        {
            // Sesi yang baru saja ditutup sering melempar COMException; refresh berikutnya akan membereskan.
            StartupLog.Write($"Media: gagal membaca sesi: {ex.Message}");
        }
    }

    /// <summary>
    /// Kirim posisi lagu. Lewat _refreshGate juga, supaya tidak pernah bersamaan dengan <see cref="Changed"/>.
    /// Firefox/Zen tidak mengisi timeline (semuanya nol); modul yang memutuskan harus bagaimana.
    /// </summary>
    private async Task RefreshTimelineAsync()
    {
        try
        {
            await _refreshGate.WaitAsync().ConfigureAwait(false);
            try
            {
                Session? session;
                lock (_gate)
                {
                    session = _stopped ? null : _session;
                }

                if (session is null)
                {
                    return;
                }

                var timeline = session.GetTimelineProperties();
                TimelineChanged?.Invoke(this, new MediaTimeline(
                    timeline.Position - timeline.StartTime,
                    timeline.EndTime - timeline.StartTime,
                    timeline.LastUpdatedTime));
            }
            finally
            {
                _refreshGate.Release();
            }
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Media: gagal membaca posisi lagu: {ex.Message}");
        }
    }

    // Dipanggil di dalam _refreshGate.
    private async Task<NowPlaying> ReadAsync(Session session, bool propertiesChanged)
    {
        var props = await session.TryGetMediaPropertiesAsync().AsTask().ConfigureAwait(false);
        var playback = session.GetPlaybackInfo();
        var controls = playback.Controls;
        var appId = session.SourceAppUserModelId ?? string.Empty;

        var title = props?.Title ?? string.Empty;
        var artist = props?.Artist ?? string.Empty;
        var album = props?.AlbumTitle ?? string.Empty;
        var track = $"{appId}\n{title}\n{artist}\n{album}";

        // Play/pause saja tidak mengubah gambar, jadi tidak perlu dibaca ulang.
        var thumbnail = propertiesChanged || track != _thumbnailTrack
            ? await ReadThumbnailAsync(props?.Thumbnail).ConfigureAwait(false)
            : _thumbnailBytes;

        if (thumbnail is not null && SameBytes(thumbnail, _thumbnailBytes))
        {
            // Array yang sama dipakai ulang supaya view tidak men-decode gambar yang sama lagi.
            thumbnail = _thumbnailBytes;
        }
        else if (thumbnail is null && track == _thumbnailTrack)
        {
            // Gambar sering hilang sesaat di tengah update; selama lagunya sama, pakai yang lama.
            thumbnail = _thumbnailBytes;
        }

        _thumbnailTrack = track;
        _thumbnailBytes = thumbnail;

        if (!_appNames.TryGetValue(appId, out var appName))
        {
            appName = AppsFolder.DisplayName(appId) ?? MediaApps.DisplayName(appId);
            _appNames[appId] = appName;
        }

        return new NowPlaying(
            appId,
            appName,
            title,
            artist,
            album,
            playback.PlaybackStatus == PlaybackStatus.Playing,
            controls.IsPlayPauseToggleEnabled,
            controls.IsNextEnabled,
            controls.IsPreviousEnabled,
            thumbnail);
    }

    private static async Task<byte[]?> ReadThumbnailAsync(IRandomAccessStreamReference? reference)
    {
        if (reference is null)
        {
            return null;
        }

        using var stream = await reference.OpenReadAsync().AsTask().ConfigureAwait(false);
        if (stream.Size == 0 || stream.Size > MaxThumbnailBytes)
        {
            return null;
        }

        using var memory = new MemoryStream((int)stream.Size);
        await stream.AsStreamForRead().CopyToAsync(memory).ConfigureAwait(false);
        return memory.ToArray();
    }

    private static bool SameBytes(byte[] a, byte[]? b) => b is not null && a.AsSpan().SequenceEqual(b);
}
