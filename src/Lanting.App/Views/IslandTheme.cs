using Lanting.Core.Settings;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Lanting.App.Views;

/// <summary>
/// Warna island: terang atau gelap mengikuti tema Windows, aksen mengikuti warna aksen Windows, dan latar sesuai
/// <see cref="IslandBackground"/> dari pengaturan. Semua view memakai kuas dari sini. Kuasnya selalu objek yang sama,
/// hanya warnanya yang diganti, jadi tema Windows yang berubah langsung terlihat tanpa membangun ulang tampilan.
/// Dibuat dan dipakai di thread UI.
/// </summary>
internal sealed class IslandTheme
{
    private readonly UISettings _system = new();
    private readonly DispatcherQueue _dispatcher;
    private IslandBackground _background = IslandBackground.Translucent;

    public IslandTheme()
    {
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        // Dipicu dari thread lain saat tema atau warna aksen Windows diganti.
        _system.ColorValuesChanged += (_, _) => _dispatcher.TryEnqueue(Apply);
        Apply();
    }

    /// <summary>Warna berubah (tema Windows atau pengaturan latar).</summary>
    public event EventHandler? Changed;

    public bool IsDark { get; private set; }

    /// <summary>Untuk tombol bawaan WinUI (efek hover, tooltip, menu klik kanan).</summary>
    public ElementTheme ElementTheme => IsDark ? ElementTheme.Dark : ElementTheme.Light;

    /// <summary>Latar pil (bisa tembus pandang).</summary>
    public Color Fill { get; private set; }

    /// <summary>Garis tipis di tepi pil, supaya pil tetap terlihat di atas halaman yang warnanya mirip.</summary>
    public Color Outline { get; private set; }

    public SolidColorBrush FillBrush { get; } = new();

    /// <summary>Teks utama.</summary>
    public SolidColorBrush Primary { get; } = new();

    /// <summary>Teks kedua (artis, keterangan).</summary>
    public SolidColorBrush Secondary { get; } = new();

    /// <summary>Teks paling redup dan ikon kosong.</summary>
    public SolidColorBrush Tertiary { get; } = new();

    /// <summary>Kartu di dalam dashboard.</summary>
    public SolidColorBrush Surface { get; } = new();

    /// <summary>Tombol dan ubin saat diam, disorot, dan ditekan.</summary>
    public SolidColorBrush Control { get; } = new();

    public SolidColorBrush ControlHover { get; } = new();

    public SolidColorBrush ControlPressed { get; } = new();

    /// <summary>Warna aksen Windows, misalnya kontrol yang menyala.</summary>
    public SolidColorBrush Accent { get; } = new();

    public SolidColorBrush AccentHover { get; } = new();

    /// <summary>Teks dan ikon di atas <see cref="Accent"/>.</summary>
    public SolidColorBrush OnAccent { get; } = new();

    /// <summary>Teks dan ikon di atas <see cref="Primary"/> (misalnya tombol putar yang terisi).</summary>
    public SolidColorBrush Inverse { get; } = new();

    public SolidColorBrush Transparent { get; } = new(Colors.Transparent);

    /// <summary>Dari pengaturan.</summary>
    public void SetBackground(IslandBackground background)
    {
        if (background != _background)
        {
            _background = background;
            Apply();
        }
    }

    /// <summary>
    /// Warna dari gambar album dibuat cukup kontras untuk teks: di tema terang, warna yang terlalu terang digelapkan.
    /// </summary>
    public Color Readable(Color color)
    {
        if (IsDark)
        {
            return color;
        }

        var luminance = Luminance(color);
        var keep = luminance > 0.45 ? 0.45 / luminance : 1;
        return Color.FromArgb(255, (byte)(color.R * keep), (byte)(color.G * keep), (byte)(color.B * keep));
    }

    private void Apply()
    {
        // Latar Windows hitam berarti tema gelap untuk aplikasi. LANTING_THEME=light/dark memaksa salah satunya (untuk tes).
        var dark = _background == IslandBackground.Black || Environment.GetEnvironmentVariable("LANTING_THEME") switch
        {
            "light" => false,
            "dark" => true,
            _ => _system.GetColorValue(UIColorType.Background).R < 128,
        };
        IsDark = dark;

        Fill = (_background, dark) switch
        {
            (IslandBackground.Black, _) => Color.FromArgb(255, 0, 0, 0),
            (IslandBackground.Solid, true) => Color.FromArgb(255, 0x1F, 0x1F, 0x22),
            (IslandBackground.Solid, false) => Color.FromArgb(255, 0xF5, 0xF5, 0xF7),
            (_, true) => Color.FromArgb(0xD8, 0x14, 0x14, 0x16),
            (_, false) => Color.FromArgb(0xD8, 0xF7, 0xF7, 0xF9),
        };
        Outline = _background == IslandBackground.Black
            ? Colors.Transparent
            : dark ? Color.FromArgb(0x24, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x1C, 0x00, 0x00, 0x00);
        FillBrush.Color = Fill;

        var ink = dark ? Colors.White : Color.FromArgb(255, 0x1A, 0x1A, 0x1C);
        Primary.Color = ink;
        Secondary.Color = WithAlpha(ink, dark ? 0xB0 : 0xA0);
        Tertiary.Color = WithAlpha(ink, dark ? 0x70 : 0x68);
        Surface.Color = WithAlpha(ink, dark ? 0x12 : 0x0B);
        Control.Color = WithAlpha(ink, dark ? 0x1C : 0x10);
        ControlHover.Color = WithAlpha(ink, dark ? 0x2C : 0x1A);
        ControlPressed.Color = WithAlpha(ink, dark ? 0x14 : 0x0A);

        var accent = _system.GetColorValue(dark ? UIColorType.AccentLight2 : UIColorType.AccentDark1);
        Accent.Color = accent;
        AccentHover.Color = WithAlpha(accent, 0xD8);
        // Aksen Windows bisa terang atau gelap (misalnya biru tua), jadi warna teks di atasnya dipilih dari kecerahannya.
        OnAccent.Color = Luminance(accent) > 0.6 ? Colors.Black : Colors.White;
        Inverse.Color = dark ? Colors.Black : Colors.White;

        Changed?.Invoke(this, EventArgs.Empty);
    }

    private static double Luminance(Color color) => ((0.299 * color.R) + (0.587 * color.G) + (0.114 * color.B)) / 255;

    private static Color WithAlpha(Color color, int alpha) => Color.FromArgb((byte)alpha, color.R, color.G, color.B);
}
