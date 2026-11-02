using System.Numerics;
using Lanting.App.Shell;
using Lanting.Core.Face;
using Microsoft.UI.Composition;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views;

/// <summary>
/// Sepasang mata ala robot EMO. Mata digambar dan dianimasikan di compositor (GPU); yang berjalan di thread UI hanya
/// timer kecil untuk berkedip, melirik, dan mengikuti kursor (10x per detik), dan semuanya berhenti saat wajah tidak terlihat.
/// Tiap mata punya dua bentuk: kotak membulat (mata biasa) dan lengkungan (^) untuk senang; ekspresi mengatur ukuran,
/// kemiringan, warna, dan bentuk mana yang terlihat.
/// Supaya terasa hidup: mata bercahaya, "bernapas" pelan, sedikit memipih saat melirik, dan sesekali melakukan
/// gerakan kecil acak (kedip dua kali, penasaran, melihat ke atas, tersenyum sebentar, melompat kecil).
/// </summary>
internal sealed class FaceView
{
    private const float EyeGap = 22;
    private const float Box = 24;
    private static readonly TimeSpan Morph = TimeSpan.FromMilliseconds(220);
    private static readonly Color Amber = Color.FromArgb(255, 0xF5, 0xB0, 0x0B);
    private static readonly Color Red = Color.FromArgb(255, 0xEF, 0x44, 0x44);
    private static readonly Color Green = Color.FromArgb(255, 0x34, 0xD3, 0x99);

    private readonly IslandTheme _theme;
    private readonly Compositor _compositor;
    private readonly Eye _left;
    private readonly Eye _right;
    private readonly ContainerVisual _eyes;
    private readonly ContainerVisual _breath;
    private readonly DropShadow _glow;
    private readonly CompositionColorBrush _brush;
    private readonly FontIcon _bolt;
    private readonly Microsoft.UI.Xaml.Media.SolidColorBrush _boltBrush = new();
    private readonly TextBlock _sleep;
    private readonly DispatcherQueueTimer _blinkTimer;
    private readonly DispatcherQueueTimer _gazeTimer;
    private readonly DispatcherQueueTimer _whimTimer;
    private readonly Random _random = new();
    private Vector2 _gaze;
    private Expression _expression = Expression.Neutral;
    private bool _onScreen;
    private bool _followingCursor;
    private DateTime _nextGlance;

    public FaceView(IslandTheme theme)
    {
        _theme = theme;
        var host = new Grid { Width = (EyeGap * 2) + Box, Height = Box };
        _compositor = ElementCompositionPreview.GetElementVisual(host).Compositor;
        _brush = _compositor.CreateColorBrush(theme.EyeColor);

        _eyes = _compositor.CreateContainerVisual();
        _eyes.Size = new Vector2((float)host.Width, Box);
        var middle = (float)host.Width / 2;
        _left = new Eye(_compositor, _brush, new Vector2(middle - (EyeGap / 2), Box / 2), mirror: false);
        _right = new Eye(_compositor, _brush, new Vector2(middle + (EyeGap / 2), Box / 2), mirror: true);
        _eyes.Children.InsertAtTop(_left.Root);
        _eyes.Children.InsertAtTop(_right.Root);

        // Napas: lapisan sendiri supaya tidak bentrok dengan lirikan (Offset) dan kedipan (Scale mata).
        _breath = _compositor.CreateContainerVisual();
        _breath.Size = _eyes.Size;
        _breath.CenterPoint = new Vector3(_eyes.Size / 2, 0);
        _breath.Children.InsertAtTop(_eyes);

        // Cahaya lembut di sekitar mata, warnanya sama dengan mata.
        _glow = _compositor.CreateDropShadow();
        _glow.BlurRadius = 9;
        _glow.Offset = Vector3.Zero;
        _glow.Opacity = 0.85f;
        _glow.Color = theme.EyeColor;

        // Bayangan mengikuti bentuk mata, bukan kotak lapisannya.
        _glow.SourcePolicy = CompositionDropShadowSourcePolicy.InheritFromVisualContent;
        var layer = _compositor.CreateLayerVisual();
        layer.Size = _eyes.Size;
        layer.Shadow = _glow;
        layer.Children.InsertAtTop(_breath);
        ElementCompositionPreview.SetElementChildVisual(host, layer);

        // Petir kecil saat charger dicolok, "z" saat tidur.
        _bolt = Icon("\uE945", 10);
        _bolt.Foreground = _boltBrush;
        _bolt.HorizontalAlignment = HorizontalAlignment.Right;
        _bolt.VerticalAlignment = VerticalAlignment.Top;
        _bolt.Margin = new Thickness(0, -2, -10, 0);
        _sleep = Text(theme.Secondary, 10, bold: true);
        _sleep.Text = "z z";
        _sleep.HorizontalAlignment = HorizontalAlignment.Right;
        _sleep.VerticalAlignment = VerticalAlignment.Top;
        _sleep.Margin = new Thickness(0, -6, -16, 0);
        host.Children.Add(_bolt);
        host.Children.Add(_sleep);

        // Disembunyikan lewat compositor (bukan Opacity XAML) karena dimunculkan dengan animasi compositor.
        ElementCompositionPreview.GetElementVisual(_bolt).Opacity = 0;
        ElementCompositionPreview.GetElementVisual(_sleep).Opacity = 0;
        Root = host;

        var dispatcher = DispatcherQueue.GetForCurrentThread();
        _blinkTimer = dispatcher.CreateTimer();
        _blinkTimer.IsRepeating = false;
        _blinkTimer.Tick += (_, _) => Blink();
        _gazeTimer = dispatcher.CreateTimer();
        _gazeTimer.Interval = TimeSpan.FromMilliseconds(100);
        _gazeTimer.Tick += (_, _) => UpdateGaze();
        _whimTimer = dispatcher.CreateTimer();
        _whimTimer.IsRepeating = false;
        _whimTimer.Tick += (_, _) => Whim();

        theme.Changed += (_, _) => Paint();
        Apply(Expression.Neutral, animate: false);
    }

    public FrameworkElement Root { get; }

    /// <summary>Posisi tengah island di layar (piksel), untuk mengikuti kursor. Diisi oleh <see cref="IslandWindow"/>.</summary>
    public Func<NativeMethods.POINT?>? Anchor { get; set; }

    /// <summary>Wajah terlihat di layar atau tidak; timer hanya jalan saat terlihat.</summary>
    public void SetOnScreen(bool onScreen)
    {
        if (onScreen == _onScreen)
        {
            return;
        }

        _onScreen = onScreen;
        if (onScreen)
        {
            ScheduleBlink();
            ScheduleWhim();
            _gazeTimer.Start();
            Breathe(_expression);
        }
        else
        {
            _blinkTimer.Stop();
            _whimTimer.Stop();
            _gazeTimer.Stop();
            _breath.StopAnimation(nameof(Visual.Scale));
        }
    }

    public void SetExpression(Expression expression)
    {
        if (expression != _expression)
        {
            Apply(expression, animate: true);
        }
    }

    private void Apply(Expression expression, bool animate)
    {
        _expression = expression;
        var duration = animate ? Morph : TimeSpan.FromMilliseconds(1);
        var shape = expression switch
        {
            Expression.Happy or Expression.Full or Expression.Charging => EyeShape.Arc,
            Expression.Surprised => new EyeShape(13, 17, 6.5f, 0, 0, false),
            Expression.Sleepy => new EyeShape(11, 4, 2, 0, 3, false),
            Expression.Asleep => new EyeShape(12, 2, 1, 0, 4, false),
            Expression.Worried => new EyeShape(12, 8, 3, 20, 2, false),
            _ => EyeShape.Normal,
        };
        _left.Morph(shape, duration);
        _right.Morph(shape, duration);
        Paint();

        Motion.FadeTo(_bolt, expression == Expression.Charging ? 1f : 0f);
        Motion.FadeTo(_sleep, expression == Expression.Asleep ? 1f : 0f);

        // Hampir habis: mata merah berkedip pelan terus-menerus.
        if (expression == Expression.Alarm)
        {
            var pulse = _compositor.CreateScalarKeyFrameAnimation();
            pulse.InsertKeyFrame(0f, 1f);
            pulse.InsertKeyFrame(0.5f, 0.25f);
            pulse.InsertKeyFrame(1f, 1f);
            pulse.Duration = TimeSpan.FromMilliseconds(1100);
            pulse.IterationBehavior = AnimationIterationBehavior.Forever;
            _eyes.StartAnimation(nameof(Visual.Opacity), pulse);
        }
        else
        {
            _eyes.StopAnimation(nameof(Visual.Opacity));
            _eyes.Opacity = 1f;
        }

        if (expression is not Expression.Neutral)
        {
            Look(Vector2.Zero, 200);
        }

        if (_onScreen)
        {
            Breathe(expression);
        }

        // Ekspresi baru datang dengan lompatan kecil.
        if (animate && expression is Expression.Happy or Expression.Surprised or Expression.Charging or Expression.Full)
        {
            Hop();
        }
    }

    // Mata mengembang-mengempis sangat pelan; saat tidur lebih lambat dan dalam.
    private void Breathe(Expression expression)
    {
        var asleep = expression is Expression.Asleep or Expression.Sleepy;
        var breathe = _compositor.CreateVector3KeyFrameAnimation();
        breathe.InsertKeyFrame(0f, Vector3.One);
        breathe.InsertKeyFrame(0.5f, asleep ? new Vector3(1.04f, 0.86f, 1f) : new Vector3(1.02f, 0.94f, 1f));
        breathe.InsertKeyFrame(1f, Vector3.One);
        breathe.Duration = TimeSpan.FromMilliseconds(asleep ? 4200 : 3200);
        breathe.IterationBehavior = AnimationIterationBehavior.Forever;
        _breath.StartAnimation(nameof(Visual.Scale), breathe);
    }

    private void Hop()
    {
        var hop = _compositor.CreateVector3KeyFrameAnimation();
        hop.InsertKeyFrame(0f, new Vector3(_gaze, 0));
        hop.InsertKeyFrame(0.35f, new Vector3(_gaze.X, _gaze.Y - 4, 0));
        hop.InsertKeyFrame(1f, new Vector3(_gaze, 0), _compositor.CreateCubicBezierEasingFunction(new(0.3f, 1.4f), new(0.5f, 1f)));
        hop.Duration = TimeSpan.FromMilliseconds(380);
        _eyes.StartAnimation(nameof(Visual.Offset), hop);
    }

    private void ScheduleWhim()
    {
        _whimTimer.Interval = TimeSpan.FromMilliseconds(_random.Next(9000, 18000));
        _whimTimer.Start();
    }

    /// <summary>Gerakan kecil acak saat wajah sedang santai, supaya tidak terlihat seperti gambar diam.</summary>
    private void Whim()
    {
        ScheduleWhim();
        if (_expression != Expression.Neutral || _followingCursor)
        {
            return;
        }

        switch (_random.Next(5))
        {
            case 0:
                // Kedip dua kali.
                _left.Blink();
                _right.Blink();
                _left.Blink(delay: 260);
                _right.Blink(delay: 260);
                break;
            case 1:
                // Penasaran: satu mata membesar, satu mengecil, sedikit miring.
                var left = _random.Next(2) == 0;
                (left ? _left : _right).Pulse(1.25f, 1400);
                (left ? _right : _left).Pulse(0.85f, 1400);
                break;
            case 2:
                // Melihat ke atas sebentar.
                Look(new Vector2(_random.Next(-3, 4), -3), 220);
                _nextGlance = DateTime.UtcNow.AddMilliseconds(1100);
                break;
            case 3:
                // Tersenyum sebentar.
                _left.Morph(EyeShape.Arc, Morph);
                _right.Morph(EyeShape.Arc, Morph);
                var back = DispatcherQueue.GetForCurrentThread().CreateTimer();
                back.Interval = TimeSpan.FromMilliseconds(900);
                back.IsRepeating = false;
                back.Tick += (_, _) =>
                {
                    if (_expression == Expression.Neutral)
                    {
                        _left.Morph(EyeShape.Normal, Morph);
                        _right.Morph(EyeShape.Normal, Morph);
                    }
                };
                back.Start();
                break;
            default:
                Hop();
                break;
        }
    }

    private void Paint()
    {
        var color = _expression switch
        {
            Expression.Worried => Amber,
            Expression.Alarm => Red,
            Expression.Full => Green,
            _ => _theme.EyeColor,
        };
        _boltBrush.Color = _theme.EyeColor;
        var glow = _compositor.CreateColorKeyFrameAnimation();
        glow.InsertKeyFrame(1f, color);
        glow.Duration = TimeSpan.FromMilliseconds(250);
        _glow.StartAnimation(nameof(DropShadow.Color), glow);

        // Di latar terang, cahaya gelap terlihat seperti noda; dibuat jauh lebih tipis.
        _glow.Opacity = _theme.IsDark ? 0.85f : 0.25f;
        var animation = _compositor.CreateColorKeyFrameAnimation();
        animation.InsertKeyFrame(1f, color);
        animation.Duration = TimeSpan.FromMilliseconds(250);
        _brush.StartAnimation(nameof(CompositionColorBrush.Color), animation);
    }

    private void ScheduleBlink()
    {
        _blinkTimer.Interval = TimeSpan.FromMilliseconds(_random.Next(2500, 6000));
        _blinkTimer.Start();
    }

    private void Blink()
    {
        if (_expression is Expression.Neutral or Expression.Worried or Expression.Surprised)
        {
            _left.Blink();
            _right.Blink();
        }

        ScheduleBlink();
    }

    /// <summary>
    /// Kursor dekat island: mata mengikuti. Jauh: sesekali melirik ke kiri/kanan sendiri.
    /// </summary>
    private void UpdateGaze()
    {
        if (_expression != Expression.Neutral)
        {
            return;
        }

        if (Anchor?.Invoke() is { } anchor && NativeMethods.GetCursorPos(out var cursor))
        {
            var dx = cursor.X - anchor.X;
            var dy = cursor.Y - anchor.Y;
            if ((dx * dx) + (dy * dy) < 700 * 700)
            {
                _followingCursor = true;
                Look(new Vector2(Math.Clamp(dx / 250f, -1f, 1f) * 4f, Math.Clamp(dy / 200f, -1f, 1f) * 3f), 120);
                return;
            }
        }

        if (_followingCursor)
        {
            _followingCursor = false;
            Look(Vector2.Zero, 250);
        }

        var now = DateTime.UtcNow;
        if (now < _nextGlance)
        {
            return;
        }

        // Lirik ke satu sisi sebentar, lalu kembali ke tengah pada lirikan berikutnya.
        var side = _random.Next(3) - 1;
        Look(new Vector2(side * 4f, 0), 260);
        _nextGlance = now.AddMilliseconds(side == 0 ? _random.Next(3000, 7000) : _random.Next(900, 1600));
    }

    private void Look(Vector2 offset, double milliseconds)
    {
        // Lirikan jauh: mata sedikit memipih seperti bergerak cepat, lalu kembali.
        if (Math.Abs(offset.X - _gaze.X) > 2.5f)
        {
            _left.Squash();
            _right.Squash();
        }

        _gaze = offset;
        var animation = _compositor.CreateVector3KeyFrameAnimation();
        animation.InsertKeyFrame(1f, new Vector3(offset, 0), _compositor.CreateCubicBezierEasingFunction(new(0.2f, 0.8f), new(0.3f, 1f)));
        animation.Duration = TimeSpan.FromMilliseconds(milliseconds);
        _eyes.StartAnimation(nameof(Visual.Offset), animation);
    }

    /// <summary>Ukuran mata (DIP), kemiringan (derajat, sudut dalam naik), geser ke bawah, dan bentuk lengkung.</summary>
    private readonly record struct EyeShape(float Width, float Height, float Radius, float Tilt, float Drop, bool IsArc)
    {
        public static readonly EyeShape Normal = new(10, 14, 4, 0, 0, false);
        public static readonly EyeShape Arc = new(10, 14, 4, 0, 0, true);
    }

    /// <summary>Satu mata: kotak membulat dan lengkungan (^), di dalam kotak 24x24 yang berpusat di <c>center</c>.</summary>
    private sealed class Eye
    {
        private readonly Compositor _compositor;
        private readonly CompositionRoundedRectangleGeometry _body;
        private readonly ShapeVisual _bodyVisual;
        private readonly ShapeVisual _arcVisual;
        private readonly bool _mirror;

        public Eye(Compositor compositor, CompositionBrush brush, Vector2 center, bool mirror)
        {
            _compositor = compositor;
            _mirror = mirror;
            var size = new Vector2(Box, Box);

            _body = compositor.CreateRoundedRectangleGeometry();
            var bodyShape = compositor.CreateSpriteShape(_body);
            bodyShape.FillBrush = brush;
            _bodyVisual = compositor.CreateShapeVisual();
            _bodyVisual.Size = size;
            _bodyVisual.Shapes.Add(bodyShape);

            // Lengkungan: setengah atas lingkaran yang hanya digambar garisnya, ujungnya bulat.
            var arc = compositor.CreateEllipseGeometry();
            arc.Center = new Vector2(Box / 2, (Box / 2) + 3);
            arc.Radius = new Vector2(5.5f, 5.5f);
            // Jalur lingkaran mulai dari atas; digeser seperempat supaya potongan setengahnya jadi setengah atas (^).
            arc.TrimStart = 0.5f;
            arc.TrimEnd = 1f;
            arc.TrimOffset = 0.25f;
            var arcShape = compositor.CreateSpriteShape(arc);
            arcShape.StrokeBrush = brush;
            arcShape.StrokeThickness = 3.2f;
            arcShape.StrokeStartCap = CompositionStrokeCap.Round;
            arcShape.StrokeEndCap = CompositionStrokeCap.Round;
            _arcVisual = compositor.CreateShapeVisual();
            _arcVisual.Size = size;
            _arcVisual.Shapes.Add(arcShape);
            _arcVisual.Opacity = 0;

            Root = compositor.CreateContainerVisual();
            Root.Size = size;
            Root.Offset = new Vector3(center.X - (Box / 2), center.Y - (Box / 2), 0);
            Root.CenterPoint = new Vector3(Box / 2, Box / 2, 0);
            Root.Children.InsertAtTop(_bodyVisual);
            Root.Children.InsertAtTop(_arcVisual);
            _bodyVisual.CenterPoint = new Vector3(Box / 2, Box / 2, 0);
        }

        public ContainerVisual Root { get; }

        public void Morph(EyeShape shape, TimeSpan duration)
        {
            var ease = _compositor.CreateCubicBezierEasingFunction(new(0.3f, 0.7f), new(0.3f, 1f));
            Animate(_body, nameof(CompositionRoundedRectangleGeometry.Size), new Vector2(shape.Width, shape.Height), duration, ease);
            Animate(_body, nameof(CompositionRoundedRectangleGeometry.Offset),
                new Vector2((Box - shape.Width) / 2, ((Box - shape.Height) / 2) + shape.Drop), duration, ease);
            Animate(_body, nameof(CompositionRoundedRectangleGeometry.CornerRadius), new Vector2(shape.Radius, shape.Radius), duration, ease);

            Fade(_bodyVisual, shape.IsArc ? 0f : 1f, duration);
            Fade(_arcVisual, shape.IsArc ? 1f : 0f, duration);

            // Cemas: sudut dalam mata naik (kiri miring ke kanan atas, kanan sebaliknya).
            var tilt = _compositor.CreateScalarKeyFrameAnimation();
            tilt.InsertKeyFrame(1f, _mirror ? shape.Tilt : -shape.Tilt, ease);
            tilt.Duration = duration;
            Root.StartAnimation(nameof(Visual.RotationAngleInDegrees), tilt);
        }

        public void Blink(int delay = 0)
        {
            var blink = _compositor.CreateVector3KeyFrameAnimation();
            blink.InsertKeyFrame(0f, Vector3.One);
            blink.InsertKeyFrame(0.45f, new Vector3(1.1f, 0.1f, 1f));
            blink.InsertKeyFrame(1f, Vector3.One);
            blink.Duration = TimeSpan.FromMilliseconds(170);
            blink.DelayTime = TimeSpan.FromMilliseconds(delay);
            _bodyVisual.StartAnimation(nameof(Visual.Scale), blink);
        }

        /// <summary>Melebar dan memendek sesaat (gerak cepat).</summary>
        public void Squash()
        {
            var squash = _compositor.CreateVector3KeyFrameAnimation();
            squash.InsertKeyFrame(0f, Vector3.One);
            squash.InsertKeyFrame(0.4f, new Vector3(1.18f, 0.84f, 1f));
            squash.InsertKeyFrame(1f, Vector3.One);
            squash.Duration = TimeSpan.FromMilliseconds(260);
            Root.StartAnimation(nameof(Visual.Scale), squash);
        }

        /// <summary>Membesar/mengecil sebentar lalu kembali (penasaran).</summary>
        public void Pulse(float scale, int milliseconds)
        {
            var pulse = _compositor.CreateVector3KeyFrameAnimation();
            pulse.InsertKeyFrame(0f, Vector3.One);
            pulse.InsertKeyFrame(0.15f, new Vector3(scale, scale, 1f));
            pulse.InsertKeyFrame(0.85f, new Vector3(scale, scale, 1f));
            pulse.InsertKeyFrame(1f, Vector3.One);
            pulse.Duration = TimeSpan.FromMilliseconds(milliseconds);
            Root.StartAnimation(nameof(Visual.Scale), pulse);
        }

        private void Animate(CompositionObject target, string property, Vector2 value, TimeSpan duration, CompositionEasingFunction ease)
        {
            var animation = _compositor.CreateVector2KeyFrameAnimation();
            animation.InsertKeyFrame(1f, value, ease);
            animation.Duration = duration;
            target.StartAnimation(property, animation);
        }

        private void Fade(Visual visual, float opacity, TimeSpan duration)
        {
            var animation = _compositor.CreateScalarKeyFrameAnimation();
            animation.InsertKeyFrame(1f, opacity);
            animation.Duration = duration;
            visual.StartAnimation(nameof(Visual.Opacity), animation);
        }
    }
}
