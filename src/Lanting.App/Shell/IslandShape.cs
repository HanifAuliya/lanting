using Lanting.Core.Island;

namespace Lanting.App.Shell;

/// <summary>
/// Ukuran pil (dalam DIP) untuk setiap state. Pil menempel di tepi atas layar, jadi
/// <see cref="CornerRadius"/> hanya berlaku untuk dua sudut bawah. Di dua sudut atas ada "telinga" melengkung ke luar
/// selebar <see cref="Ear"/>, supaya pil terlihat menyatu dengan tepi layar.
/// </summary>
internal readonly record struct IslandShape(double Width, double Height, double CornerRadius)
{
    public static readonly IslandShape Compact = new(200, 34, 16);
    public static readonly IslandShape Notification = new(360, 64, 26);

    /// <summary>Dashboard: baris menu di atas, lalu satu halaman (musik, cuaca, kontrol, perangkat, atau aplikasi).</summary>
    public static readonly IslandShape Expanded = new(560, 218, 30);

    /// <summary>
    /// Spring sedikit melewati ukuran target sebelum kembali; selama animasi, window menyediakan ruang sebesar ini
    /// di kiri, kanan, dan bawah pil supaya pantulannya tidak terpotong.
    /// </summary>
    public const double Overshoot = 32;

    /// <summary>Lebar dan tinggi telinga dibanding jari-jari sudut bawah (juga tertulis di PillAnimator.EarSize).</summary>
    public const double EarRatio = 0.5;

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

    /// <summary>Ukuran lengkungan di kiri dan kanan atas pil.</summary>
    public double Ear => CornerRadius * EarRatio;

    /// <summary>Bentuk ini ditambah ruang untuk pantulan spring, tidak lebih besar dari <see cref="Max"/>.</summary>
    public IslandShape WithOvershoot() =>
        new(Math.Min(Width + (2 * Overshoot), Max.Width), Math.Min(Height + Overshoot, Max.Height), CornerRadius);
}
