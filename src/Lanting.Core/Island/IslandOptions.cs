namespace Lanting.Core.Island;

/// <summary>Pengaturan perilaku island. Nanti diisi dari halaman settings.</summary>
public sealed record IslandOptions
{
    /// <summary>Tampilkan pil kecil walau tidak ada konten (seperti iPhone).</summary>
    public bool ShowWhenIdle { get; init; } = true;

    /// <summary>Mouse harus diam di atas pil selama ini sebelum pil terbuka, supaya tidak terbuka saat sekadar lewat.</summary>
    public TimeSpan ExpandDelay { get; init; } = TimeSpan.FromMilliseconds(250);

    /// <summary>Jeda sebelum island mengecil setelah mouse keluar, supaya tidak berkedip.</summary>
    public TimeSpan CollapseDelay { get; init; } = TimeSpan.FromMilliseconds(300);
}
