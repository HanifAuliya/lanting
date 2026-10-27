using Lanting.App.Shell;
using Lanting.Core.Island;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using WinRT.Interop;
using WinUIEx;

namespace Lanting.App;

/// <summary>
/// Window island. Tampilannya dibangun lewat kode (bukan XAML) supaya ringan dan
/// tidak bergantung pada pemuatan file XAML saat startup.
/// </summary>
public sealed partial class IslandWindow : Window
{
    private const double TopMargin = 8;

    // Kira-kira waktu spring selesai; setelah itu window boleh mengecil tanpa memotong animasi.
    private static readonly TimeSpan ShrinkDelay = TimeSpan.FromMilliseconds(450);

    private readonly IslandController _controller;
    private readonly IslandStateMachine _stateMachine;
    private readonly IslandWindowChrome _chrome;
    private readonly PillAnimator _animator;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _shrinkTimer;
    private readonly Canvas _root;
    private readonly Grid _pill;
    private readonly StackPanel _expandedContent;
    private IslandShape _windowShape;

    public IslandWindow(IslandController controller, IslandStateMachine stateMachine)
    {
        _controller = controller;
        _stateMachine = stateMachine;

        (_root, _pill, _expandedContent) = BuildContent();
        Content = _root;
        ExtendsContentIntoTitleBar = true;

        // Latar window benar-benar transparan; yang terlihat hanya pil hitam.
        SystemBackdrop = new TransparentTintBackdrop();

        _chrome = new IslandWindowChrome(AppWindow, WindowNative.GetWindowHandle(this));
        _chrome.Configure();

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _shrinkTimer = _dispatcher.CreateTimer();
        _shrinkTimer.IsRepeating = false;
        _shrinkTimer.Interval = ShrinkDelay;
        _shrinkTimer.Tick += (_, _) => ResizeWindow(IslandShape.For(_stateMachine.State));

        var initial = IslandShape.For(_stateMachine.State);
        _animator = new PillAnimator(_pill, initial);
        PillAnimator.SetOpacity(_expandedContent, initial == IslandShape.Expanded ? 1f : 0f);
        ResizeWindow(initial);
        _chrome.SetVisible(_stateMachine.State != IslandState.Hidden);

        _controller.CurrentChanged += OnContentChanged;
        _stateMachine.StateChanged += OnStateChanged;
        Closed += OnClosed;
    }

    /// <summary>Diminta keluar dari menu klik kanan; App yang mematikan modul dan menutup aplikasi.</summary>
    public event EventHandler? ExitRequested;

    private void OnContentChanged(object? sender, IslandContent? content) => _stateMachine.OnContentChanged(content);

    // StateChanged bisa datang dari thread timer, jadi selalu pindah ke thread UI dulu.
    private void OnStateChanged(object? sender, IslandState state) => _dispatcher.TryEnqueue(() => ApplyState(state));

    private void ApplyState(IslandState state)
    {
        if (state == IslandState.Hidden)
        {
            _chrome.SetVisible(false);
            return;
        }

        var target = IslandShape.For(state);
        _shrinkTimer.Stop();

        if (target.Width >= _windowShape.Width || target.Height >= _windowShape.Height)
        {
            // Membesar: perbesar window dulu, baru animasikan pil di dalamnya.
            ResizeWindow(Union(target, _windowShape));
        }

        if (target.Width < _windowShape.Width || target.Height < _windowShape.Height)
        {
            // Mengecil: animasikan dulu, window menyusul setelah spring selesai.
            _shrinkTimer.Start();
        }

        _chrome.SetVisible(true);
        _animator.AnimateTo(target);
        _animator.Fade(_expandedContent, state == IslandState.Expanded);
    }

    private void ResizeWindow(IslandShape shape)
    {
        _windowShape = shape;
        _chrome.Resize(shape.Width, shape.Height, TopMargin);

        // Pil (selebar Max) diletakkan di tengah window supaya clip yang rata tengah tetap di tengah layar.
        Canvas.SetLeft(_pill, (shape.Width - IslandShape.Max.Width) / 2);
    }

    private (Canvas Root, Grid Pill, StackPanel ExpandedContent) BuildContent()
    {
        var dot = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush(Color.FromArgb(255, 0x2D, 0xD4, 0xBF)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var title = new TextBlock
        {
            Text = "Lanting",
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        header.Children.Add(dot);
        header.Children.Add(title);

        var expanded = new StackPanel { Margin = new Thickness(28, 24, 28, 24), Spacing = 6 };
        expanded.Children.Add(header);
        expanded.Children.Add(new TextBlock
        {
            Text = "Belum ada modul aktif. Lagu, lirik, dan baterai menyusul.",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 0xA0, 0xA0, 0xA0)),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        });
        expanded.Children.Add(new TextBlock
        {
            Text = "Klik kanan untuk keluar.",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 0x70, 0x70, 0x70)),
            FontSize = 12,
        });

        // Pil selalu berukuran maksimum; bentuk yang terlihat diatur oleh clip yang dianimasikan.
        var pill = new Grid
        {
            Width = IslandShape.Max.Width,
            Height = IslandShape.Max.Height,
            Background = new SolidColorBrush(Colors.Black),
        };
        pill.Children.Add(expanded);

        var exit = new MenuFlyoutItem { Text = "Keluar dari Lanting" };
        exit.Click += OnExitClick;
        var menu = new MenuFlyout();
        menu.Items.Add(exit);

        var root = new Canvas
        {
            Background = new SolidColorBrush(Colors.Transparent),
            ContextFlyout = menu,
        };
        root.Children.Add(pill);
        root.PointerEntered += OnPointerEntered;
        root.PointerExited += OnPointerExited;

        return (root, pill, expanded);
    }

    private static IslandShape Union(IslandShape a, IslandShape b) =>
        new(Math.Max(a.Width, b.Width), Math.Max(a.Height, b.Height), a.CornerRadius);

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => _stateMachine.OnPointerEntered();

    private void OnPointerExited(object sender, PointerRoutedEventArgs e) => _stateMachine.OnPointerExited();

    private void OnExitClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _controller.CurrentChanged -= OnContentChanged;
        _stateMachine.StateChanged -= OnStateChanged;
        _shrinkTimer.Stop();
    }
}
