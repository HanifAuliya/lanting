namespace Lanting.Core.Media;

/// <summary>Warna RGB 8-bit.</summary>
public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>
/// Memilih warna aksen dari gambar album, misalnya untuk visualizer.
/// Yang dipilih adalah kelompok warna paling "berwarna" (jenuh dan cukup terang), bukan rata-rata,
/// karena rata-rata gambar hampir selalu jadi abu-abu kecokelatan.
/// </summary>
public static class ArtworkColor
{
    private const int HueBuckets = 12;

    // Di bawah ini warna dianggap abu-abu, hitam, atau putih: tidak ikut dipilih.
    private const double MinSaturation = 0.25;
    private const double MinValue = 0.2;

    // Kelompok warna pemenang minimal sebanyak ini dari seluruh piksel; kurang dari itu anggap gambar hitam-putih.
    private const double MinShare = 0.03;

    // Island berlatar hitam: warna aksen harus cukup terang supaya terbaca.
    private const double MinAccentValue = 0.75;

    /// <summary>
    /// Pilih warna dari piksel BGRA (4 byte per piksel, seperti keluaran BitmapDecoder Windows).
    /// <c>null</c> bila gambarnya hampir tanpa warna (hitam-putih), supaya pemanggil memakai warna bawaan.
    /// </summary>
    public static Rgb? Pick(ReadOnlySpan<byte> bgra)
    {
        Span<int> count = stackalloc int[HueBuckets];
        Span<double> weight = stackalloc double[HueBuckets];
        Span<double> sumR = stackalloc double[HueBuckets];
        Span<double> sumG = stackalloc double[HueBuckets];
        Span<double> sumB = stackalloc double[HueBuckets];
        var pixels = bgra.Length / 4;

        for (var i = 0; i < pixels; i++)
        {
            double b = bgra[i * 4], g = bgra[(i * 4) + 1], r = bgra[(i * 4) + 2];
            var (hue, saturation, value) = ToHsv(r / 255, g / 255, b / 255);
            if (saturation < MinSaturation || value < MinValue)
            {
                continue;
            }

            var bucket = Math.Min((int)(hue / 360 * HueBuckets), HueBuckets - 1);
            var w = saturation * value;
            count[bucket]++;
            weight[bucket] += w;
            sumR[bucket] += r * w;
            sumG[bucket] += g * w;
            sumB[bucket] += b * w;
        }

        var best = 0;
        for (var i = 1; i < HueBuckets; i++)
        {
            if (weight[i] > weight[best])
            {
                best = i;
            }
        }

        if (pixels == 0 || count[best] < pixels * MinShare)
        {
            return null;
        }

        return Brighten(sumR[best] / weight[best], sumG[best] / weight[best], sumB[best] / weight[best]);
    }

    private static Rgb Brighten(double r, double g, double b)
    {
        var (hue, saturation, value) = ToHsv(r / 255, g / 255, b / 255);
        value = Math.Max(value, MinAccentValue);
        saturation = Math.Min(saturation, 0.85);
        var (r2, g2, b2) = FromHsv(hue, saturation, value);
        return new Rgb(ToByte(r2), ToByte(g2), ToByte(b2));
    }

    private static byte ToByte(double channel) => (byte)Math.Clamp(Math.Round(channel * 255), 0, 255);

    private static (double Hue, double Saturation, double Value) ToHsv(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var delta = max - min;
        var saturation = max == 0 ? 0 : delta / max;
        if (delta == 0)
        {
            return (0, saturation, max);
        }

        double hue;
        if (max == r)
        {
            hue = 60 * (((g - b) / delta) % 6);
        }
        else if (max == g)
        {
            hue = 60 * (((b - r) / delta) + 2);
        }
        else
        {
            hue = 60 * (((r - g) / delta) + 4);
        }

        return (hue < 0 ? hue + 360 : hue, saturation, max);
    }

    private static (double R, double G, double B) FromHsv(double hue, double saturation, double value)
    {
        var c = value * saturation;
        var x = c * (1 - Math.Abs((hue / 60 % 2) - 1));
        var m = value - c;
        var (r, g, b) = (hue / 60) switch
        {
            < 1 => (c, x, 0.0),
            < 2 => (x, c, 0.0),
            < 3 => (0.0, c, x),
            < 4 => (0.0, x, c),
            < 5 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        return (r + m, g + m, b + m);
    }
}
