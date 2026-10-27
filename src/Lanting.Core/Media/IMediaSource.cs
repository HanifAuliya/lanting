namespace Lanting.Core.Media;

/// <summary>
/// Sumber info lagu (di aplikasi: SMTC Windows). Dipisah dari modul supaya logika modul bisa dites tanpa Windows.
/// </summary>
public interface IMediaSource
{
    /// <summary>
    /// Dipicu saat lagu, status putar, atau gambar berubah; <c>null</c> berarti tidak ada pemutar aktif.
    /// Tidak pernah dipicu bersamaan dari dua thread, tapi bisa dari thread mana pun.
    /// </summary>
    event EventHandler<NowPlaying?>? Changed;

    /// <summary>Mulai mendengarkan; keadaan awal dikirim lewat <see cref="Changed"/>.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();
}

/// <summary>Perintah ke pemutar yang sedang aktif. Dipanggil dari tombol di island.</summary>
public interface IMediaControls
{
    Task PlayPauseAsync();

    Task NextAsync();

    Task PreviousAsync();
}
