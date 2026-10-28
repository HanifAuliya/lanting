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
    // Membesar: sedikit memantul supaya terasa "hidup". Mengecil: hampir tanpa pantulan supaya tenang.
    private static readonly Spring Grow = new(0.62f, TimeSpan.FromMilliseconds(62));
    private static readonly Spring Shrink = new(0.88f, TimeSpan.FromMilliseconds(52));

    private static readonly TimeSpan RevealDuration = TimeSpan.FromMilliseconds(220);
    private static readonly TimeSpan RevealDelay = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan HideDuration = TimeSpan.FromMilliseconds(90);
    private const float HiddenScale = 0.92f;

    private readonly Compositor _compositor;
    private readonly CompositionRoundedRectangleGeometry _clip;
    private IslandShape _shape;

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
        var spring = shape.Width * shape.Height >= _shape.Width * _shape.Height ? Grow : Shrink;
        _shape = shape;
        StartSpring(nameof(CompositionRoundedRectangleGeometry.Size), SizeOf(shape), spring);
        StartSpring(nameof(CompositionRoundedRectangleGeometry.Offset), OffsetOf(shape), spring);
        StartSpring(nameof(CompositionRoundedRectangleGeometry.CornerRadius), RadiusOf(shape), spring);
    }

    /// <summary>Set tampil/tidaknya isi langsung tanpa animasi (dipakai saat awal).</summary>
    public static void SetRevealed(FrameworkElement element, bool visible)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.Opacity = visible ? 1f : 0f;
        visual.Scale = visible ? Vector3.One : new Vector3(HiddenScale, HiddenScale, 1f);
    }

    /// <summary>
    /// Isi pil muncul/hilang mengikuti state: muncul sambil sedikit membesar dari tengah atas (seperti ikut
    /// "keluar" dari pil), hilang cepat sebelum bentuk pil banyak berubah supaya tidak terlihat terpotong clip.
    /// </summary>
    public void Reveal(FrameworkElement element, bool visible)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)(element.Width / 2), 0f, 0f);

        var fade = _compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, visible ? 1f : 0f);
        fade.Duration = visible ? RevealDuration : HideDuration;
        fade.DelayTime = visible ? RevealDelay : TimeSpan.Zero;
        visual.StartAnimation(nameof(Visual.Opacity), fade);

        var scale = _compositor.CreateVector3KeyFrameAnimation();
        var target = visible ? 1f : HiddenScale;
        scale.InsertKeyFrame(1f, new Vector3(target, target, 1f), _compositor.CreateCubicBezierEasingFunction(new(0.2f, 0.9f), new(0.3f, 1f)));
        scale.Duration = fade.Duration;
        scale.DelayTime = fade.DelayTime;
        visual.StartAnimation(nameof(Visual.Scale), scale);
    }

    private void Apply(IslandShape shape)
    {
        _shape = shape;
        _clip.Size = SizeOf(shape);
        _clip.Offset = OffsetOf(shape);
        _clip.CornerRadius = RadiusOf(shape);
    }

    private void StartSpring(string property, Vector2 target, Spring spring)
    {
        var animation = _compositor.CreateSpringVector2Animation();
        animation.DampingRatio = spring.DampingRatio;
        animation.Period = spring.Period;
        animation.FinalValue = target;
        _clip.StartAnimation(property, animation);
    }

    // Clip dibuat lebih tinggi sebesar jari-jari sudut dan digeser ke atas sejauh itu. Bagian di atas tepi elemen
    // terpotong, jadi sudut atas jadi rata (menempel ke tepi layar) dan hanya sudut bawah yang membulat.
    private static Vector2 SizeOf(IslandShape s) => new((float)s.Width, (float)(s.Height + s.CornerRadius));

    // Pil rata tengah secara horizontal dan menempel di atas.
    private static Vector2 OffsetOf(IslandShape s) => new((float)((IslandShape.Max.Width - s.Width) / 2), (float)-s.CornerRadius);

    private static Vector2 RadiusOf(IslandShape s) => new((float)s.CornerRadius, (float)s.CornerRadius);

    private readonly record struct Spring(float DampingRatio, TimeSpan Period);
}
