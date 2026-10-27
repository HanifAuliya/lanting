namespace Lanting.Core.Island;

/// <summary>
/// Menentukan konten mana yang menang saat beberapa modul ingin tampil bersamaan.
/// </summary>
public enum IslandPriority
{
    /// <summary>Konten latar yang boleh tampil terus, misalnya lagu yang sedang diputar.</summary>
    Ambient = 0,

    /// <summary>Informasi biasa, misalnya perangkat Bluetooth baru terhubung.</summary>
    Normal = 1,

    /// <summary>Perlu perhatian, misalnya baterai mouse tinggal 10%.</summary>
    Alert = 2,

    /// <summary>Harus terlihat sekarang, misalnya baterai kritis.</summary>
    Critical = 3,
}
