using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;

namespace Lanting.App.Views;

/// <summary>Animasi kecil yang dipakai bersama oleh view. Semuanya berjalan di compositor (GPU).</summary>
internal static class Motion
{
    private static readonly TimeSpan PopDuration = TimeSpan.FromMilliseconds(280);
    private static readonly TimeSpan Stagger = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// Isi baru masuk: muncul pelan sambil naik sedikit. Beberapa elemen diberi jeda bertingkat
    /// (judul dulu, lalu artis, dan seterusnya) supaya terasa mengalir.
    /// </summary>
    public static void Pop(params UIElement[] elements)
    {
        for (var i = 0; i < elements.Length; i++)
        {
            var element = elements[i];
            ElementCompositionPreview.SetIsTranslationEnabled(element, true);
            var visual = ElementCompositionPreview.GetElementVisual(element);
            var compositor = visual.Compositor;
            var ease = compositor.CreateCubicBezierEasingFunction(new(0.2f, 0.9f), new(0.3f, 1f));
            var delay = Stagger * i;

            var fade = compositor.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0f, 0f);
            fade.InsertKeyFrame(1f, 1f, ease);
            fade.Duration = PopDuration;
            fade.DelayTime = delay;
            fade.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            visual.StartAnimation(nameof(Visual.Opacity), fade);

            var slide = compositor.CreateVector3KeyFrameAnimation();
            slide.InsertKeyFrame(0f, new Vector3(0f, 6f, 0f));
            slide.InsertKeyFrame(1f, Vector3.Zero, ease);
            slide.Duration = PopDuration;
            slide.DelayTime = delay;
            slide.DelayBehavior = AnimationDelayBehavior.SetInitialValueBeforeDelay;
            visual.StartAnimation("Translation", slide);
        }
    }

    /// <summary>Ubah opacity secara halus.</summary>
    public static void FadeTo(UIElement element, float opacity, double milliseconds = 240)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var fade = visual.Compositor.CreateScalarKeyFrameAnimation();
        fade.InsertKeyFrame(1f, opacity);
        fade.Duration = TimeSpan.FromMilliseconds(milliseconds);
        visual.StartAnimation(nameof(Visual.Opacity), fade);
    }
}
