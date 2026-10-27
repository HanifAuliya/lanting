using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Lanting.App.Shell;

/// <summary>
/// Menganimasikan bentuk pil dengan spring di compositor Windows (GPU, thread terpisah),
/// jadi thread UI dan CPU hampir tidak terpakai selama animasi.
/// Caranya: elemen pil selalu berukuran maksimum, lalu clip bersudut bulat di atasnya yang dianimasikan.
/// </summary>
internal sealed class PillAnimator
{
    // Angka awal dari dokumen rencana; di-tuning di milestone 3.
    private const float DampingRatio = 0.72f;
    private static readonly TimeSpan Period = TimeSpan.FromMilliseconds(55);
    private static readonly TimeSpan FadeInDuration = TimeSpan.FromMilliseconds(180);
    private static readonly TimeSpan FadeInDelay = TimeSpan.FromMilliseconds(110);
    private static readonly TimeSpan FadeOutDuration = TimeSpan.FromMilliseconds(90);

    private readonly Compositor _compositor;
    private readonly CompositionRoundedRectangleGeometry _clip;

    public PillAnimator(UIElement pill, IslandShape initial)
    {
        var visual = ElementCompositionPreview.GetElementVisual(pill);
        _compositor = visual.Compositor;

        _clip = _compositor.CreateRoundedRectangleGeometry();
        Apply(initial);
        visual.Clip = _compositor.CreateGeometricClip(_clip);
    }

    public void AnimateTo(IslandShape shape)
    {
        StartSpring(nameof(CompositionRoundedRectangleGeometry.Size), SizeOf(shape));
        StartSpring(nameof(CompositionRoundedRectangleGeometry.Offset), OffsetOf(shape));
        StartSpring(nameof(CompositionRoundedRectangleGeometry.CornerRadius), RadiusOf(shape));
    }

    /// <summary>Set opacity langsung tanpa animasi (dipakai saat awal).</summary>
    public static void SetOpacity(UIElement element, float opacity) =>
        ElementCompositionPreview.GetElementVisual(element).Opacity = opacity;

    /// <summary>Isi pil memudar masuk/keluar mengikuti state.</summary>
    public void Fade(UIElement element, bool visible)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, visible ? 1f : 0f);
        // Isi lama hilang cepat sebelum bentuk pil banyak berubah, supaya tidak terlihat terpotong clip;
        // isi baru menunggu pil hampir sampai di ukuran barunya.
        fade.Duration = visible ? FadeInDuration : FadeOutDuration;
        fade.DelayTime = visible ? FadeInDelay : TimeSpan.Zero;
        visual.StartAnimation(nameof(Visual.Opacity), fade);
    }

    private void Apply(IslandShape shape)
    {
        _clip.Size = SizeOf(shape);
        _clip.Offset = OffsetOf(shape);
        _clip.CornerRadius = RadiusOf(shape);
    }

    private void StartSpring(string property, Vector2 target)
    {
        var spring = _compositor.CreateSpringVector2Animation();
        spring.DampingRatio = DampingRatio;
        spring.Period = Period;
        spring.FinalValue = target;
        _clip.StartAnimation(property, spring);
    }

    private static Vector2 SizeOf(IslandShape s) => new((float)s.Width, (float)s.Height);

    // Pil rata tengah secara horizontal dan menempel di atas.
    private static Vector2 OffsetOf(IslandShape s) => new((float)((IslandShape.Max.Width - s.Width) / 2), 0f);

    private static Vector2 RadiusOf(IslandShape s) => new((float)s.CornerRadius, (float)s.CornerRadius);
}
