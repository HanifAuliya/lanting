using Lanting.Core.Island;

namespace Lanting.App.Shell;

/// <summary>
/// Ukuran pil (dalam DIP) untuk setiap state. Pil menempel di tepi atas layar, jadi
/// <see cref="CornerRadius"/> hanya berlaku untuk dua sudut bawah.
/// </summary>
internal readonly record struct IslandShape(double Width, double Height, double CornerRadius)
{
    public static readonly IslandShape Compact = new(200, 34, 16);
    public static readonly IslandShape Notification = new(360, 64, 26);

    /// <summary>Dashboard: jam, musik, cuaca, kontrol cepat, dan aplikasi.</summary>
    public static readonly IslandShape Expanded = new(800, 236, 28);

    // Spring sedikit melewati ukuran target sebelum kembali; ruang ini supaya pantulannya tidak terpotong.
    private const double Overshoot = 24;

    /// <summary>
    /// Ukuran window dan elemen pil. Tetap sebesar ini: window tidak pernah pindah atau berubah ukuran
    /// saat animasi (itu yang dulu membuat pil "loncat" sesaat), yang dianimasikan hanya clip-nya.
    /// </summary>
    public static readonly IslandShape Max = new(Expanded.Width + (2 * Overshoot), Expanded.Height + Overshoot, 0);

    public static IslandShape For(IslandState state) => state switch
    {
        IslandState.Expanded => Expanded,
        IslandState.Notification => Notification,
        _ => Compact,
    };

    /// <summary>Kotak terkecil yang memuat kedua bentuk.</summary>
    public static IslandShape Union(IslandShape a, IslandShape b) =>
        new(Math.Max(a.Width, b.Width), Math.Max(a.Height, b.Height), a.CornerRadius);

    public bool Contains(IslandShape other) => Width >= other.Width && Height >= other.Height;
}
