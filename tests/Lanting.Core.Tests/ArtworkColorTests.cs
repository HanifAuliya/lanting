using Lanting.Core.Media;

namespace Lanting.Core.Tests;

public class ArtworkColorTests
{
    [Fact]
    public void Picks_the_dominant_vivid_color_not_the_average()
    {
        // 70% hitam, 20% merah, 10% biru: hasilnya merah, bukan campuran gelap keunguan.
        var pixels = Image((0, 0, 0, 70), (220, 30, 30, 20), (30, 30, 220, 10));

        var color = ArtworkColor.Pick(pixels);

        Assert.NotNull(color);
        Assert.True(color.Value.R > 180 && color.Value.G < 90 && color.Value.B < 90, $"Bukan merah: {color}");
    }

    [Fact]
    public void Dark_colors_are_brightened_so_they_show_on_black()
    {
        var pixels = Image((90, 10, 10, 100));

        var color = ArtworkColor.Pick(pixels);

        Assert.NotNull(color);
        Assert.True(color.Value.R >= 190, $"Terlalu gelap: {color}");
    }

    [Fact]
    public void Grayscale_artwork_has_no_accent()
    {
        var pixels = Image((0, 0, 0, 50), (128, 128, 128, 30), (255, 255, 255, 20));

        Assert.Null(ArtworkColor.Pick(pixels));
    }

    [Fact]
    public void Empty_image_has_no_accent()
    {
        Assert.Null(ArtworkColor.Pick([]));
    }

    // Buat piksel BGRA dari daftar (r, g, b, jumlah).
    private static byte[] Image(params (byte R, byte G, byte B, int Count)[] runs)
    {
        var bytes = new List<byte>();
        foreach (var (r, g, b, count) in runs)
        {
            for (var i = 0; i < count; i++)
            {
                bytes.AddRange([b, g, r, 255]);
            }
        }

        return [.. bytes];
    }
}
