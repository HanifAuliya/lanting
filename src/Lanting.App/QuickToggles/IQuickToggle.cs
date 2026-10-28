namespace Lanting.App.QuickToggles;

/// <summary>Satu tombol kontrol cepat di dashboard (Wi-Fi, Bluetooth, suara, dan seterusnya).</summary>
internal interface IQuickToggle
{
    /// <summary>Nama pendek untuk tooltip, misalnya "Wi-Fi".</summary>
    string Label { get; }

    /// <summary>Ikon Segoe Fluent Icons.</summary>
    string Glyph { get; }

    /// <summary>Status sekarang; <c>null</c> bila tidak tersedia di PC ini (misalnya tidak ada adaptor Bluetooth).</summary>
    Task<bool?> ReadAsync();

    /// <summary>Nyalakan atau matikan. Mengembalikan status setelahnya.</summary>
    Task<bool?> WriteAsync(bool on);
}
