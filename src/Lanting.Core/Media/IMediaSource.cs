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

    /// <summary>
    /// Dipicu saat pemutar melaporkan posisi lagu (ganti lagu, seek, kadang berkala). <c>null</c> atau durasi nol
    /// berarti pemutar tidak memberi posisi (misalnya Firefox/Zen). Aturan thread sama dengan <see cref="Changed"/>.
    /// </summary>
    event EventHandler<MediaTimeline?>? TimelineChanged;

    /// <summary>Mulai mendengarkan; keadaan awal dikirim lewat <see cref="Changed"/>.</summary>
    Task StartAsync(CancellationToken cancellationToken);

    Task StopAsync();
}

/// <summary>Posisi lagu yang dilaporkan pemutar.</summary>
/// <param name="Position">Posisi dari awal lagu.</param>
/// <param name="Duration">Panjang lagu; nol bila tidak diketahui.</param>
/// <param name="UpdatedAt">Kapan posisi itu berlaku. Saat diputar, posisi sekarang = Position + (sekarang - UpdatedAt).</param>
public sealed record MediaTimeline(TimeSpan Position, TimeSpan Duration, DateTimeOffset UpdatedAt);

/// <summary>Perintah ke pemutar yang sedang aktif. Dipanggil dari tombol di island.</summary>
public interface IMediaControls
{
    Task PlayPauseAsync();

    Task NextAsync();

    Task PreviousAsync();
}
