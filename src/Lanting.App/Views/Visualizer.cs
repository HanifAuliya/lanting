using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Lanting.App.Views;

/// <summary>
/// Bar kecil yang naik-turun saat lagu diputar, seperti di Dynamic Island iPhone.
/// Gerakannya bukan dari suara sungguhan (itu butuh merekam audio terus-menerus dan memakan CPU),
/// melainkan animasi berulang di compositor (GPU). Saat dijeda, animasi dihentikan dan bar jadi titik diam.
/// Biaya terukur: sekitar 8% satu core CPU selama lagu diputar (compositor menggambar 60 fps), 0% saat dijeda.
/// </summary>
internal sealed class Visualizer
{
    private const double BarWidth = 3;
    private const double BarHeight = 14;
    private const float RestScale = 0.22f;

    // Tiap bar punya irama sendiri supaya tidak terlihat seragam.
    private static readonly (double Ms, float[] Heights)[] Rhythms =
    [
        (900, [0.35f, 0.9f, 0.5f, 0.75f, 0.35f]),
        (700, [0.6f, 0.3f, 1f, 0.45f, 0.6f]),
        (1100, [0.45f, 0.8f, 0.3f, 0.95f, 0.45f]),
        (800, [0.8f, 0.4f, 0.7f, 0.3f, 0.8f]),
    ];

    private readonly Rectangle[] _bars;
    private readonly SolidColorBrush _brush;
    private bool _playing;
    private bool _enabled = true;
    private bool _onScreen = true;
    private bool? _animating;

    public Visualizer()
    {
        _brush = new SolidColorBrush(Color.FromArgb(255, 0x2D, 0xD4, 0xBF));
        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Height = BarHeight,
            VerticalAlignment = VerticalAlignment.Center,
        };

        _bars = new Rectangle[Rhythms.Length];
        for (var i = 0; i < _bars.Length; i++)
        {
            _bars[i] = new Rectangle
            {
                Width = BarWidth,
                Height = BarHeight,
                RadiusX = BarWidth / 2,
                RadiusY = BarWidth / 2,
                Fill = _brush,
            };
            var visual = ElementCompositionPreview.GetElementVisual(_bars[i]);
            visual.CenterPoint = new Vector3((float)(BarWidth / 2), (float)(BarHeight / 2), 0f);
            visual.Scale = new Vector3(1f, RestScale, 1f);
            panel.Children.Add(_bars[i]);
        }

        Root = panel;
    }

    public FrameworkElement Root { get; }

    public void SetColor(Color color)
    {
        _brush.Color = color;
    }

    /// <summary>Lagu sedang diputar.</summary>
    public void SetPlaying(bool playing)
    {
        _playing = playing;
        Apply();
    }

    /// <summary>Dimatikan dari pengaturan: bar disembunyikan dan animasi berhenti.</summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        Root.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        Apply();
    }

    /// <summary>Pil kecil sedang tidak terlihat (pil besar terbuka, atau island disembunyikan): hemat CPU.</summary>
    public void SetOnScreen(bool onScreen)
    {
        _onScreen = onScreen;
        Apply();
    }

    private void Apply()
    {
        var animate = _playing && _enabled && _onScreen;
        if (_animating == animate)
        {
            return;
        }

        _animating = animate;
        for (var i = 0; i < _bars.Length; i++)
        {
            var visual = ElementCompositionPreview.GetElementVisual(_bars[i]);
            var compositor = visual.Compositor;
            var animation = compositor.CreateScalarKeyFrameAnimation();
            if (animate)
            {
                var (ms, heights) = Rhythms[i];
                for (var k = 0; k < heights.Length; k++)
                {
                    animation.InsertKeyFrame(k / (float)(heights.Length - 1), heights[k]);
                }

                animation.Duration = TimeSpan.FromMilliseconds(ms);
                animation.IterationBehavior = AnimationIterationBehavior.Forever;
            }
            else
            {
                // Turun pelan ke posisi diam, lalu tidak ada animasi yang berjalan sama sekali.
                animation.InsertKeyFrame(1f, RestScale);
                animation.Duration = TimeSpan.FromMilliseconds(250);
            }

            visual.StartAnimation("Scale.Y", animation);
        }
    }
}
