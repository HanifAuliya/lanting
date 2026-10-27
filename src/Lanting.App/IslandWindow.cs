using Lanting.App.Shell;
using Lanting.App.Views;
using Lanting.Core.Island;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using WinRT.Interop;
using WinUIEx;

namespace Lanting.App;

/// <summary>
/// Window island. Tampilannya dibangun lewat kode (bukan XAML) supaya ringan dan
/// tidak bergantung pada pemuatan file XAML saat startup.
/// Isi pil berasal dari <see cref="IIslandView"/>: satu view per jenis konten, ditambah <see cref="IdleView"/>.
/// </summary>
internal sealed partial class IslandWindow : Window
{
    private const double TopMargin = 8;

    // Kira-kira waktu spring selesai; setelah itu area klik boleh mengecil tanpa memotong animasi.
    private static readonly TimeSpan ShrinkDelay = TimeSpan.FromMilliseconds(450);

    private readonly IslandController _controller;
    private readonly IslandStateMachine _stateMachine;
    private readonly IReadOnlyList<IIslandView> _views;
    private readonly IdleView _idle = new();
    private readonly IslandWindowChrome _chrome;
    private readonly PillAnimator _animator;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _shrinkTimer;
    private readonly Canvas _root;
    private readonly Grid _pill;
    private readonly Grid _compactHost;
    private readonly Grid _expandedHost;
    private IIslandView? _activeView;
    private IslandShape _hitShape;

    public IslandWindow(IslandController controller, IslandStateMachine stateMachine, IEnumerable<IIslandView> views)
    {
        _controller = controller;
        _stateMachine = stateMachine;
        _views = [.. views, _idle];

        (_root, _pill, _compactHost, _expandedHost) = BuildContent();
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
        _shrinkTimer.Tick += (_, _) => SetHitShape(IslandShape.For(_stateMachine.State));

        ShowContent(_controller.Current);

        var initial = IslandShape.For(_stateMachine.State);
        _animator = new PillAnimator(_pill, initial);
        PillAnimator.SetRevealed(_expandedHost, _stateMachine.State == IslandState.Expanded);
        PillAnimator.SetRevealed(_compactHost, _stateMachine.State != IslandState.Expanded);
        _chrome.Place(IslandShape.Max.Width, IslandShape.Max.Height, TopMargin);
        SetHitShape(initial);
        _chrome.SetVisible(_stateMachine.State != IslandState.Hidden);

        _controller.CurrentChanged += OnContentChanged;
        _stateMachine.StateChanged += OnStateChanged;
        Closed += OnClosed;
    }

    /// <summary>Diminta keluar dari menu klik kanan; App yang mematikan modul dan menutup aplikasi.</summary>
    public event EventHandler? ExitRequested;

    private void OnContentChanged(object? sender, IslandContent? content)
    {
        // Isi diantrikan sebelum state, supaya pil tidak sempat membesar dengan isi yang lama.
        _dispatcher.TryEnqueue(() => ShowContent(content));
        _stateMachine.OnContentChanged(content);
    }

    // StateChanged bisa datang dari thread timer, jadi selalu pindah ke thread UI dulu.
    private void OnStateChanged(object? sender, IslandState state) => _dispatcher.TryEnqueue(() => ApplyState(state));

    private void ShowContent(IslandContent? content)
    {
        var view = content is null ? _idle : _views.First(v => v.CanShow(content));
        if (!ReferenceEquals(view, _activeView))
        {
            _compactHost.Children.Clear();
            _expandedHost.Children.Clear();
            _compactHost.Children.Add(view.Compact);
            _expandedHost.Children.Add(view.Expanded);
            _activeView = view;
            Motion.Pop(view.Compact, view.Expanded);
        }

        if (content is not null)
        {
            view.Show(content);
        }
    }

    private void ApplyState(IslandState state)
    {
        if (state == IslandState.Hidden)
        {
            _chrome.SetVisible(false);
            return;
        }

        var target = IslandShape.For(state);
        _shrinkTimer.Stop();

        // Biasanya tidak melakukan apa-apa; hanya bila resolusi atau skala layar berubah.
        if (_chrome.Place(IslandShape.Max.Width, IslandShape.Max.Height, TopMargin))
        {
            SetHitShape(_hitShape);
        }

        if (!_hitShape.Contains(target))
        {
            // Membesar: area klik langsung ikut besar supaya mouse tidak "jatuh" keluar di tengah animasi.
            SetHitShape(IslandShape.Union(target, _hitShape));
        }

        if (!target.Contains(_hitShape))
        {
            // Mengecil: area klik menyusul setelah spring selesai.
            _shrinkTimer.Start();
        }

        _chrome.SetVisible(true);
        _animator.AnimateTo(target);
        _animator.Reveal(_expandedHost, state == IslandState.Expanded);
        _animator.Reveal(_compactHost, state != IslandState.Expanded);
    }

    private void SetHitShape(IslandShape shape)
    {
        _hitShape = shape;
        _chrome.SetHitArea(shape.Width, shape.Height);
    }

    private (Canvas Root, Grid Pill, Grid CompactHost, Grid ExpandedHost) BuildContent()
    {
        // Wadah isi pil kecil: rata tengah atas, persis di bawah clip Compact.
        var compactHost = new Grid
        {
            Width = IslandShape.Compact.Width,
            Height = IslandShape.Compact.Height,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var expandedHost = new Grid
        {
            Width = IslandShape.Expanded.Width,
            Height = IslandShape.Expanded.Height,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };

        // Pil selalu sebesar window; bentuk yang terlihat diatur oleh clip yang dianimasikan.
        var pill = new Grid
        {
            Width = IslandShape.Max.Width,
            Height = IslandShape.Max.Height,
            Background = new SolidColorBrush(Colors.Black),
        };
        pill.Children.Add(expandedHost);
        pill.Children.Add(compactHost);

        var exit = new MenuFlyoutItem { Text = "Keluar dari Lanting" };
        exit.Click += OnExitClick;
        var menu = new MenuFlyout();
        menu.Items.Add(exit);

        var root = new Canvas
        {
            Background = new SolidColorBrush(Colors.Transparent),
            ContextFlyout = menu,
            // Pil selalu hitam, jadi tombol dan menu memakai tema gelap apa pun tema Windows-nya.
            RequestedTheme = ElementTheme.Dark,
        };
        root.Children.Add(pill);
        root.PointerEntered += OnPointerEntered;
        root.PointerExited += OnPointerExited;

        return (root, pill, compactHost, expandedHost);
    }

    private void OnPointerEntered(object sender, PointerRoutedEventArgs e) => _stateMachine.OnPointerEntered();

    private void OnPointerExited(object sender, PointerRoutedEventArgs e)
    {
        // PointerExited dari tombol di dalam pil ikut naik ke sini; abaikan selama mouse masih di dalam window.
        if (!_chrome.ContainsCursor())
        {
            _stateMachine.OnPointerExited();
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _controller.CurrentChanged -= OnContentChanged;
        _stateMachine.StateChanged -= OnStateChanged;
        _shrinkTimer.Stop();
    }
}
