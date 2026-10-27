namespace Lanting.Core.Media;

/// <summary>
/// Lagu yang sedang diputar menurut Windows (SMTC). Dipakai sebagai <see cref="Island.IslandContent.Payload"/>.
/// </summary>
/// <param name="AppId">Id aplikasi pemutar dari Windows, misalnya "Spotify.exe" atau "MSEdge".</param>
/// <param name="AppName">Nama pemutar yang enak dibaca, misalnya "Spotify" atau "Microsoft Edge".</param>
/// <param name="Title">Judul lagu.</param>
/// <param name="Artist">Nama artis, bisa kosong.</param>
/// <param name="Album">Nama album, bisa kosong.</param>
/// <param name="IsPlaying"><c>true</c> bila sedang diputar, <c>false</c> bila dijeda.</param>
/// <param name="CanPlayPause">Pemutar menerima perintah play/pause.</param>
/// <param name="CanNext">Pemutar menerima perintah lagu berikutnya.</param>
/// <param name="CanPrevious">Pemutar menerima perintah lagu sebelumnya.</param>
/// <param name="Thumbnail">
/// Gambar album (PNG/JPEG) atau <c>null</c>. Sumber memakai ulang array yang sama selama lagunya sama,
/// jadi perbandingan record (yang membandingkan referensi array) tetap murah dan benar.
/// </param>
public sealed record NowPlaying(
    string AppId,
    string AppName,
    string Title,
    string Artist,
    string Album,
    bool IsPlaying,
    bool CanPlayPause,
    bool CanNext,
    bool CanPrevious,
    byte[]? Thumbnail = null);
