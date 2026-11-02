using System.Text.Json;
using Lanting.Core.Location;

namespace Lanting.Core.Settings;

/// <summary>Pengaturan yang bisa diubah dari jendela Pengaturan. Disimpan sebagai JSON.</summary>
public sealed record LantingSettings
{
    /// <summary>Warna latar island.</summary>
    public IslandBackground Background { get; init; } = IslandBackground.Translucent;

    /// <summary>Bentuk island: menempel di tepi atas (notch) atau melayang (dynamic).</summary>
    public IslandMode Mode { get; init; } = IslandMode.Notch;

    /// <summary>Wajah (mata) bergantian dengan jam di pil kecil saat tidak ada lagu.</summary>
    public bool ShowFace { get; init; } = true;

    /// <summary>Tampilkan pil kecil walau tidak ada lagu.</summary>
    public bool ShowWhenIdle { get; init; } = true;

    /// <summary>Sembunyikan island saat game, video, atau presentasi layar penuh.</summary>
    public bool HideInFullscreen { get; init; } = true;

    /// <summary>Cari dan tampilkan lirik (judul lagu dikirim ke lrclib.net).</summary>
    public bool ShowLyrics { get; init; } = true;

    /// <summary>Kolom lirik di halaman Musik terlihat. Lirik tetap dicari dan tetap tampil di pil kecil.</summary>
    public bool ShowLyricsPanel { get; init;} = true;

    /// <summary>Cuaca di dashboard (lokasi kira-kira dari alamat IP lewat ipwho.is, cuaca dari Open-Meteo).</summary>
    public bool ShowWeather { get; init; } = true;

    /// <summary>Kota pilihan untuk cuaca dan jadwal sholat; <c>null</c> berarti otomatis dari alamat internet.</summary>
    public GeoLocation? City { get; init; }

    /// <summary>Lokasi otomatis terakhir, supaya jadwal sholat tetap ada saat offline.</summary>
    public GeoLocation? LastAutoLocation { get; init; }

    /// <summary>Notifikasi di island saat masuk waktu sholat.</summary>
    public bool PrayerNotifications { get; init; } = true;

    /// <summary>Visualizer bergerak saat lagu diputar (sekitar 8% satu core CPU).</summary>
    public bool ShowVisualizer { get; init; } = true;

    /// <summary>Pemberitahuan baterai lemah dan hampir habis.</summary>
    public bool BatteryNotifications { get; init; } = true;

    /// <summary>Pemberitahuan perangkat tersambung dan terputus.</summary>
    public bool ConnectionNotifications { get; init; } = true;

    /// <summary>Batas "baterai lemah" dalam persen.</summary>
    public int LowBatteryPercent { get; init; } = 20;

    /// <summary>Pilihan yang ditawarkan untuk <see cref="LowBatteryPercent"/>.</summary>
    public static IReadOnlyList<int> LowBatteryChoices { get; } = [10, 15, 20, 25, 30];

    /// <summary>Rapikan nilai yang tidak masuk akal (misalnya file diedit tangan).</summary>
    public LantingSettings Normalize() => this with
    {
        LowBatteryPercent = Math.Clamp(LowBatteryPercent, 5, 50),
        Background = Enum.IsDefined(Background) ? Background : IslandBackground.Translucent,
        Mode = Enum.IsDefined(Mode) ? Mode : IslandMode.Notch,
    };
}

/// <summary>Bentuk island.</summary>
public enum IslandMode
{
    /// <summary>Menempel di tepi atas layar seperti notch laptop, dengan lengkungan di kedua sudut atas.</summary>
    Notch,

    /// <summary>Kapsul melayang sedikit di bawah tepi atas, keempat sudutnya bulat, seperti Dynamic Island iPhone.</summary>
    Dynamic,
}

/// <summary>Pilihan warna latar island.</summary>
public enum IslandBackground
{
    /// <summary>Agak tembus pandang, terang atau gelap mengikuti tema Windows.</summary>
    Translucent,

    /// <summary>Tidak tembus pandang, terang atau gelap mengikuti tema Windows.</summary>
    Solid,

    /// <summary>Hitam pekat seperti Dynamic Island iPhone, apa pun tema Windows-nya.</summary>
    Black,
}

/// <summary>
/// Memuat dan menyimpan <see cref="LantingSettings"/> di satu file JSON.
/// File yang hilang atau rusak tidak pernah membuat aplikasi gagal: pengaturan bawaan yang dipakai.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private readonly Lock _gate = new();
    private LantingSettings _current;

    public SettingsStore(string path)
    {
        _path = path;
        _current = Load(path);
    }

    /// <summary>Dipicu setelah pengaturan berubah dan tersimpan. Bisa dari thread mana pun yang memanggil Update.</summary>
    public event EventHandler<LantingSettings>? Changed;

    public LantingSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <summary>Ubah pengaturan, simpan ke file, lalu beri tahu yang mendengarkan.</summary>
    /// <returns><c>false</c> bila gagal disimpan (pengaturan tetap berlaku sampai aplikasi ditutup).</returns>
    public bool Update(Func<LantingSettings, LantingSettings> change)
    {
        LantingSettings next;
        bool saved;
        lock (_gate)
        {
            next = change(_current).Normalize();
            if (next == _current)
            {
                return true;
            }

            _current = next;
            saved = TrySave(next);
        }

        Changed?.Invoke(this, next);
        return saved;
    }

    private static LantingSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                return (JsonSerializer.Deserialize<LantingSettings>(File.ReadAllText(path), Json) ?? new()).Normalize();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // File rusak atau tidak bisa dibaca: pakai bawaan, file akan ditimpa saat pengaturan diubah.
        }

        return new LantingSettings();
    }

    private bool TrySave(LantingSettings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);

            // Tulis ke file sementara dulu supaya file lama tidak rusak bila listrik mati di tengah jalan.
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, Json));
            File.Move(temp, _path, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
