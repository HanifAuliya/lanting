namespace Lanting.Core.Island;

/// <summary>
/// Permintaan sebuah modul untuk tampil di island.
/// Identitasnya adalah pasangan <see cref="ModuleId"/> + <see cref="Key"/>: menampilkan ulang
/// dengan pasangan yang sama akan mengganti konten lama, bukan menambah antrean.
/// </summary>
/// <param name="ModuleId">Id modul pengirim, misalnya "media".</param>
/// <param name="Key">Kunci di dalam modul, misalnya "now-playing" atau "battery-mouse".</param>
/// <param name="Priority">Prioritas tampil.</param>
/// <param name="Payload">Data untuk view. Core tidak membacanya; UI yang menerjemahkan.</param>
/// <param name="Duration">Lama tampil. <c>null</c> berarti tampil sampai ditarik dengan Withdraw.</param>
public sealed record IslandContent(
    string ModuleId,
    string Key,
    IslandPriority Priority,
    object? Payload = null,
    TimeSpan? Duration = null)
{
    /// <summary>Konten sementara berprioritas tinggi tampil sebagai notifikasi, bukan pil biasa.</summary>
    public bool IsNotification => Duration is not null && Priority >= IslandPriority.Alert;
}
