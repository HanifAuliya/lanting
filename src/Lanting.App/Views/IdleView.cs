using System.Numerics;
using Lanting.Core.Face;
using Lanting.Core.Island;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;

namespace Lanting.App.Views;

/// <summary>
/// Tampilan saat tidak ada konten: pil kecil bergantian menampilkan wajah (<see cref="FaceView"/>) dan jam + tanggal
/// singkat ("13:48  Rab 28"), dengan animasi geser. Saat ekspresi wajah berubah, wajah langsung ditampilkan.
/// Wajah bisa dimatikan di Pengaturan; jam saja yang tampil.
/// </summary>
internal sealed class IdleView : IIslandView
{
    private static readonly TimeSpan FaceFor = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan ClockFor = TimeSpan.FromSeconds(4);

    private readonly FaceView _face;
    private readonly FaceController _faces;
    private readonly TextBlock _time;
    private readonly TextBlock _date;
    private readonly FrameworkElement _clock;
    private readonly DispatcherQueueTimer _swap;
    private bool _faceEnabled = true;
    private bool _showingFace = true;
    private bool _onScreen;
    private bool _smoke;

    public IdleView(ClockTicker clock, IslandTheme theme, FaceView face, FaceController faces)
    {
        _face = face;
        _faces = faces;
        _time = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = theme.Primary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _date = new TextBlock
        {
            FontSize = 12,
            Foreground = theme.Secondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Show(DateTime.Now);
        clock.Tick += (_, now) => Show(now);

        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        line.Children.Add(_time);
        line.Children.Add(_date);
        _clock = line;

        face.Root.HorizontalAlignment = HorizontalAlignment.Center;
        face.Root.VerticalAlignment = VerticalAlignment.Center;
        ElementCompositionPreview.SetIsTranslationEnabled(face.Root, true);
        ElementCompositionPreview.SetIsTranslationEnabled(_clock, true);
        Compact = new Grid { Children = { _clock, face.Root } };

        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _swap = dispatcher.CreateTimer();
        _swap.IsRepeating = false;
        _swap.Tick += (_, _) => ShowFace(!_showingFace);

        faces.Changed += (_, expression) => dispatcher.TryEnqueue(() =>
        {
            if (_smoke)
            {
                return;
            }

            face.SetExpression(expression);
            if (expression != Expression.Neutral && _faceEnabled)
            {
                ShowFace(true);
            }
        });
        face.SetExpression(faces.Current);

        // Tes: LANTING_SMOKE_FACE=Happy (dsb.) memaksa satu ekspresi, dan wajah tidak bergantian dengan jam.
        if (Enum.TryParse<Expression>(Environment.GetEnvironmentVariable("LANTING_SMOKE_FACE"), out var forced))
        {
            face.SetExpression(forced);
            _faceEnabled = true;
            _smoke = true;
        }
        Place(face.Root, visible: true, animate: false);
        Place(_clock, visible: false, animate: false);
    }

    public UIElement Compact { get; }

    public FaceView Face => _face;

    public double CompactWidth => 150;

    public bool CanShow(IslandContent content) => true;

    public void Show(IslandContent content)
    {
    }

    public void SetCompactOnScreen(bool onScreen)
    {
        // Wajah muncul lagi (dashboard ditutup, lagu selesai): senang sebentar.
        if (onScreen && !_onScreen && _showingFace)
        {
            _faces.Cheer();
        }

        _onScreen = onScreen;
        _face.SetOnScreen(onScreen && _showingFace);
        if (onScreen && _faceEnabled)
        {
            Restart();
        }
        else
        {
            _swap.Stop();
        }
    }

    /// <summary>Dari pengaturan: wajah ditampilkan bergantian dengan jam, atau jam saja.</summary>
    public void SetFaceEnabled(bool enabled)
    {
        _faceEnabled = enabled;
        ShowFace(enabled);
        if (!enabled)
        {
            _swap.Stop();
        }
    }

    private void ShowFace(bool face)
    {
        face &= _faceEnabled;
        if (face != _showingFace)
        {
            _showingFace = face;
            Place(_face.Root, face, animate: true);
            Place(_clock, !face, animate: true);
            _face.SetOnScreen(_onScreen && face);
        }

        if (_onScreen && _faceEnabled)
        {
            Restart();
        }
    }

    private void Restart()
    {
        _swap.Stop();
        if (_smoke)
        {
            return;
        }

        _swap.Interval = _showingFace ? FaceFor : ClockFor;
        _swap.Start();
    }

    // Yang muncul naik dari bawah, yang pergi naik keluar ke atas.
    private static void Place(UIElement element, bool visible, bool animate)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        var target = visible ? Vector3.Zero : new Vector3(0, -10, 0);
        if (!animate)
        {
            visual.Properties.InsertVector3("Translation", target);
            visual.Opacity = visible ? 1 : 0;
            return;
        }

        var compositor = visual.Compositor;
        var ease = compositor.CreateCubicBezierEasingFunction(new(0.2f, 0.9f), new(0.3f, 1f));
        var slide = compositor.CreateVector3KeyFrameAnimation();
        if (visible)
        {
            slide.InsertKeyFrame(0f, new Vector3(0, 10, 0));
        }

        slide.InsertKeyFrame(1f, target, ease);
        slide.Duration = TimeSpan.FromMilliseconds(360);
        visual.StartAnimation("Translation", slide);
        Motion.FadeTo(element, visible ? 1f : 0f, 300);
    }

    private void Show(DateTime now)
    {
        _time.Text = ClockTicker.Time(now);
        _date.Text = ClockTicker.ShortDate(now);
    }
}
