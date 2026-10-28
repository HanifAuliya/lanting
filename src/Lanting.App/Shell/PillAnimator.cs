using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;

namespace Lanting.App.Shell;

/// <summary>
/// Menganimasikan bentuk pil dengan spring di compositor Windows (GPU, thread terpisah),
/// jadi thread UI dan CPU hampir tidak terpakai selama animasi.
/// Caranya: elemen pil selalu berukuran maksimum, lalu clip bersudut bulat di atasnya yang dianimasikan.
/// Telinga di kiri-kanan atas dan garis tepi tipis mengikuti clip itu lewat expression animation,
/// jadi ketiganya selalu bergerak bersamaan tanpa kode yang berjalan tiap frame.
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

    // Ukuran telinga dalam expression; harus sama dengan IslandShape.EarRatio. Ditulis langsung (bukan diformat
    // dari angka) supaya tidak menjadi "0,5" di Windows berbahasa Indonesia.
    private const string EarSize = "(clip.CornerRadius.X * 0.5)";

    private readonly Compositor _compositor;
    private readonly CompositionRoundedRectangleGeometry _clip;
    private readonly CompositionColorBrush _earFill;
    private readonly CompositionColorBrush _outline;
    private IslandShape _shape;

    /// <param name="pill">Elemen pil (berlatar, berukuran <see cref="IslandShape.Max"/>) yang di-clip.</param>
    /// <param name="overlay">Induk pil; telinga dan garis tepi digambar di atasnya.</param>
    public PillAnimator(UIElement pill, UIElement overlay, IslandShape initial)
    {
        var visual = ElementCompositionPreview.GetElementVisual(pill);
        _compositor = visual.Compositor;

        _clip = _compositor.CreateRoundedRectangleGeometry();
        Apply(initial);
        visual.Clip = _compositor.CreateGeometricClip(_clip);

        _earFill = _compositor.CreateColorBrush();
        _outline = _compositor.CreateColorBrush();
        var decorations = _compositor.CreateContainerVisual();
        decorations.Children.InsertAtTop(CreateOutline());
        decorations.Children.InsertAtTop(CreateEar(left: true));
        decorations.Children.InsertAtTop(CreateEar(left: false));
        ElementCompositionPreview.SetElementChildVisual(overlay, decorations);
    }

    public void AnimateTo(IslandShape shape)
    {
        var spring = shape.Width * shape.Height >= _shape.Width * _shape.Height ? Grow : Shrink;
        _shape = shape;
        StartSpring(nameof(CompositionRoundedRectangleGeometry.Size), SizeOf(shape), spring);
        StartSpring(nameof(CompositionRoundedRectangleGeometry.Offset), OffsetOf(shape), spring);
        StartSpring(nameof(CompositionRoundedRectangleGeometry.CornerRadius), RadiusOf(shape), spring);
    }

    /// <summary>Warna telinga (harus sama dengan latar pil) dan garis tepi.</summary>
    public void SetColors(Color fill, Color outline)
    {
        _earFill.Color = fill;
        _outline.Color = outline;
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

    /// <summary>
    /// Garis tepi: persegi bulat yang sama dengan clip, hanya digambar garisnya. Bagian paling atas (setinggi telinga)
    /// dipotong, karena di situ sisi pil bersambung dengan telinga dan tidak boleh ada garis.
    /// </summary>
    private ShapeVisual CreateOutline()
    {
        var geometry = _compositor.CreateRoundedRectangleGeometry();
        Follow(geometry, nameof(CompositionRoundedRectangleGeometry.Offset), "clip.Offset");
        Follow(geometry, nameof(CompositionRoundedRectangleGeometry.Size), "clip.Size");
        Follow(geometry, nameof(CompositionRoundedRectangleGeometry.CornerRadius), "clip.CornerRadius");

        var shape = _compositor.CreateSpriteShape(geometry);
        shape.StrokeBrush = _outline;
        shape.StrokeThickness = 1f;

        var visual = _compositor.CreateShapeVisual();
        visual.Size = new Vector2((float)IslandShape.Max.Width, (float)IslandShape.Max.Height);
        visual.Shapes.Add(shape);
        var clip = _compositor.CreateInsetClip();
        Follow(clip, nameof(InsetClip.TopInset), EarSize);
        visual.Clip = clip;
        return visual;
    }

    /// <summary>
    /// Telinga: kotak kecil di luar sudut atas pil, dikurangi seperempat lingkaran, sehingga tepi atas layar
    /// melengkung turun ke sisi pil. Lingkarannya digambar sebagai cincin tebal (bagian di luar seperempat
    /// lingkaran) lalu dipotong sebesar kotak; garis tepinya cincin tipis di jari-jari yang sama.
    /// </summary>
    private ShapeVisual CreateEar(bool left)
    {
        // Pusat lingkaran: sudut bawah kotak yang jauh dari pil.
        var center = left ? $"Vector2(0, {EarSize})" : $"Vector2({EarSize}, {EarSize})";

        var fill = _compositor.CreateEllipseGeometry();
        Follow(fill, nameof(CompositionEllipseGeometry.Center), center);
        Follow(fill, nameof(CompositionEllipseGeometry.Radius), $"Vector2({EarSize} * 1.25, {EarSize} * 1.25)");
        var fillShape = _compositor.CreateSpriteShape(fill);
        fillShape.StrokeBrush = _earFill;
        Follow(fillShape, nameof(CompositionSpriteShape.StrokeThickness), $"{EarSize} * 0.5");

        var line = _compositor.CreateEllipseGeometry();
        Follow(line, nameof(CompositionEllipseGeometry.Center), center);
        Follow(line, nameof(CompositionEllipseGeometry.Radius), $"Vector2({EarSize}, {EarSize})");
        var lineShape = _compositor.CreateSpriteShape(line);
        lineShape.StrokeBrush = _outline;
        lineShape.StrokeThickness = 1f;

        var visual = _compositor.CreateShapeVisual();
        visual.Shapes.Add(fillShape);
        visual.Shapes.Add(lineShape);
        visual.Clip = _compositor.CreateInsetClip();
        Follow(visual, nameof(Visual.Size), $"Vector2({EarSize}, {EarSize})");
        Follow(visual, nameof(Visual.Offset), left
            ? $"Vector3(clip.Offset.X - {EarSize}, 0, 0)"
            : "Vector3(clip.Offset.X + clip.Size.X, 0, 0)");
        return visual;
    }

    private void Follow(CompositionObject target, string property, string expression)
    {
        var animation = _compositor.CreateExpressionAnimation(expression);
        animation.SetReferenceParameter("clip", _clip);
        target.StartAnimation(property, animation);
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
