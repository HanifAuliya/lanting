using Lanting.App.Shell;
using Lanting.Core.Island;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using WinRT.Interop;
using WinUIEx;

namespace Lanting.App;

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
    private IslandShape _windowShape;

    public IslandWindow(IslandController controller, IslandStateMachine stateMachine)
    {
        _controller = controller;
        _stateMachine = stateMachine;

        InitializeComponent();

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
        _animator = new PillAnimator(Pill, initial);
        PillAnimator.SetOpacity(ExpandedContent, initial == IslandShape.Expanded ? 1f : 0f);
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
        _animator.Fade(ExpandedContent, state == IslandState.Expanded);
    }

    private void ResizeWindow(IslandShape shape)
    {
        _windowShape = shape;
        _chrome.Resize(shape.Width, shape.Height, TopMargin);

        // Pil (selebar Max) diletakkan di tengah window supaya clip yang rata tengah tetap di tengah layar.
        Canvas.SetLeft(Pill, (shape.Width - IslandShape.Max.Width) / 2);
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
