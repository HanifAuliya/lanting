using Lanting.Core.Island;
using Microsoft.UI.Xaml;

namespace Lanting.App.Views;

/// <summary>
/// Tampilan untuk satu jenis konten (misalnya lagu). <see cref="IslandWindow"/> memilih view pertama yang
/// <see cref="CanShow"/>, lalu menaruh <see cref="Compact"/> dan <see cref="Expanded"/> di dalam pil.
/// Semua anggota dipanggil di thread UI.
/// </summary>
internal interface IIslandView
{
    /// <summary>Isi pil kecil, berukuran <see cref="Shell.IslandShape.Compact"/>.</summary>
    UIElement Compact { get; }

    /// <summary>Lebar pil kecil (DIP) yang dibutuhkan isi saat ini; biasanya <see cref="Shell.IslandShape.Compact"/>.</summary>
    double CompactWidth { get; }

    /// <summary>Isi pil besar, berukuran <see cref="Shell.IslandShape.Expanded"/>.</summary>
    UIElement Expanded { get; }

    /// <summary>
    /// Isi pil notifikasi (<see cref="Shell.IslandShape.Notification"/>), untuk konten penting yang sementara.
    /// <c>null</c> bila view tidak punya; pil kecil yang dipakai.
    /// </summary>
    UIElement? Notification => null;

    /// <summary>
    /// Pil kecil view ini mulai/berhenti terlihat (pil besar dibuka, island disembunyikan, dsb.).
    /// Dipakai untuk menghentikan animasi berulang yang tidak terlihat.
    /// </summary>
    void SetCompactOnScreen(bool onScreen)
    {
    }

    bool CanShow(IslandContent content);

    void Show(IslandContent content);
}
