using Lanting.App.Shell;
using Lanting.App.Views;
using Lanting.App.Views.Dashboard;
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
/// Isi pil kecil berasal dari <see cref="IIslandView"/>: satu view per jenis konten, ditambah <see cref="IdleView"/> (jam).
/// Pil besar (saat di-hover) selalu berisi <see cref="DashboardView"/>.
/// </summary>
internal sealed partial class IslandWindow : Window
{
    // Island menempel langsung ke tepi atas layar.
    private const double TopMargin = 0;

    // Kira-kira waktu spring selesai; setelah itu window boleh mengecil ke bentuk pil tanpa memotong pantulannya.
    private static readonly TimeSpan SettleDelay = TimeSpan.FromMilliseconds(500);

    private readonly IslandController _controller;
    private readonly IslandStateMachine _stateMachine;
    private readonly IReadOnlyList<IIslandView> _views;
    private readonly IdleView _idle;
    private readonly DashboardView _dashboard;
    private readonly IslandTheme _theme;
    private readonly IslandWindowChrome _chrome;
    private readonly PillAnimator _animator;
    private readonly DispatcherQueue _dispatcher;
    private readonly DispatcherQueueTimer _settleTimer;
    private readonly Canvas _root;
    private readonly Grid _pill;
    private readonly Grid _compactHost;
    private readonly Grid _expandedHost;
    private readonly Grid _notificationHost;
    private IIslandView? _activeView;
    private IslandState _appliedState;
    private IslandShape _shape;
    private bool _pointerInside;
    private double _compactWidth = IslandShape.Compact.Width;

    public IslandWindow(
        IslandController controller,
        IslandStateMachine stateMachine,
        IEnumerable<IIslandView> views,
        IdleView idle,
        DashboardView dashboard,
        IslandTheme theme)
    {
        _theme = theme;
        _controller = controller;
        _stateMachine = stateMachine;
        _idle = idle;
        _dashboard = dashboard;
        _dashboard.SettingsRequested += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _views = [.. views, _idle];

        (_root, _pill, _compactHost, _expandedHost, _notificationHost) = BuildContent();
        Content = _root;
        ExtendsContentIntoTitleBar = true;

        // Latar window benar-benar transparan; yang terlihat hanya pil.
        SystemBackdrop = new TransparentTintBackdrop();

        _chrome = new IslandWindowChrome(AppWindow, WindowNative.GetWindowHandle(this));
        _chrome.Configure();

        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _settleTimer = _dispatcher.CreateTimer();
        _settleTimer.IsRepeating = false;
        _settleTimer.Interval = SettleDelay;
        _settleTimer.Tick += (_, _) => SetRegion(_shape, settled: true);
        _appliedState = _stateMachine.State;

        var initial = ShapeFor(_stateMachine.State);
        _animator = new PillAnimator(_pill, _root, initial);
        ApplyTheme();
        _theme.Changed += OnThemeChanged;
        PillAnimator.SetRevealed(_expandedHost, _stateMachine.State == IslandState.Expanded);
        PillAnimator.SetRevealed(_compactHost, _stateMachine.State != IslandState.Expanded);
        PillAnimator.SetRevealed(_notificationHost, false);
        _chrome.Place(IslandShape.Max.Width, IslandShape.Max.Height, TopMargin);
        _shape = initial;
        SetRegion(initial, settled: true);
        _chrome.SetVisible(_stateMachine.State != IslandState.Hidden);
        ShowContent(_controller.Current);

        _controller.CurrentChanged += OnContentChanged;
        _stateMachine.StateChanged += OnStateChanged;
        Closed += OnClosed;
    }

    /// <summary>Diminta keluar dari menu klik kanan; App yang mematikan modul dan menutup aplikasi.</summary>
    public event EventHandler? ExitRequested;

    /// <summary>"Pengaturan" dipilih dari tombol di dashboard atau menu klik kanan.</summary>
    public event EventHandler? SettingsRequested;

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
            _notificationHost.Children.Clear();
            _compactHost.Children.Add(view.Compact);
            if (view.Notification is { } notification)
            {
                _notificationHost.Children.Add(notification);
            }

            _activeView?.SetCompactOnScreen(false);
            _activeView = view;
            view.SetCompactOnScreen(_stateMachine.State is IslandState.Compact);
            Motion.Pop(view.Compact);
        }

        if (content is not null)
        {
            view.Show(content);
        }

        _dashboard.Show(content);

        // Misalnya lagu dengan lirik: pil kecil melebar supaya baris liriknya muat.
        if (view.CompactWidth != _compactWidth)
        {
            _compactWidth = view.CompactWidth;
            _compactHost.Width = _compactWidth;
            if (_stateMachine.State != IslandState.Hidden)
            {
                Reshape(ShapeFor(_stateMachine.State));
            }
        }
    }

    private void ApplyState(IslandState state)
    {
        var opened = state == IslandState.Expanded && _appliedState != IslandState.Expanded;
        _appliedState = state;
        if (state == IslandState.Hidden)
        {
            _chrome.SetVisible(false);
            _activeView?.SetCompactOnScreen(false);
            return;
        }

        if (opened)
        {
            _dashboard.OnOpened();
        }

        Reshape(ShapeFor(state));
        _chrome.SetVisible(true);
        // Pil notifikasi memakai isi khusus bila view punya; kalau tidak, isi pil kecil yang tampil.
        var notification = state == IslandState.Notification && _notificationHost.Children.Count > 0;
        _animator.Reveal(_expandedHost, state == IslandState.Expanded);
        _animator.Reveal(_notificationHost, notification);
        _animator.Reveal(_compactHost, state != IslandState.Expanded && !notification);
        _activeView?.SetCompactOnScreen(state != IslandState.Expanded && !notification);
    }

    private IslandShape ShapeFor(IslandState state) =>
        state is IslandState.Expanded or IslandState.Notification
            ? IslandShape.For(state)
            : IslandShape.Compact with { Width = _compactWidth };

    /// <summary>Animasikan pil ke bentuk baru dan atur bagian window yang terlihat mengikutinya.</summary>
    private void Reshape(IslandShape target)
    {
        _settleTimer.Stop();

        // Biasanya tidak melakukan apa-apa; hanya bila resolusi atau skala layar berubah.
        _chrome.Place(IslandShape.Max.Width, IslandShape.Max.Height, TopMargin);

        // Selama spring berjalan, window memuat bentuk lama dan baru plus ruang pantulan, supaya tepi pil
        // tidak terpotong kotak window. Setelah spring selesai, window kembali sepas bentuk pil.
        var previous = _shape;
        _shape = target;
        var both = IslandShape.Union(target, previous) with { CornerRadius = Math.Max(target.CornerRadius, previous.CornerRadius) };
        SetRegion(both, settled: false);
        _settleTimer.Start();
        _animator.AnimateTo(target);
        UpdatePointer();
    }

    private void SetRegion(IslandShape shape, bool settled)
    {
        var area = settled ? shape : shape.WithOvershoot();
        _chrome.SetRegion(area.Width, area.Height, shape.Ear, (_shape.Width, _shape.Height));
    }

    private void OnThemeChanged(object? sender, EventArgs e) => ApplyTheme();

    private void ApplyTheme()
    {
        _root.RequestedTheme = _theme.ElementTheme;
        _animator.SetColors(_theme.Fill, _theme.Outline);
    }

    private (Canvas Root, Grid Pill, Grid CompactHost, Grid ExpandedHost, Grid NotificationHost) BuildContent()
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
            Children = { _dashboard.Root },
        };

        var notificationHost = new Grid
        {
            Width = IslandShape.Notification.Width,
            Height = IslandShape.Notification.Height,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
        };

        // Pil selalu sebesar window; bentuk yang terlihat diatur oleh clip yang dianimasikan.
        var pill = new Grid
        {
            Width = IslandShape.Max.Width,
            Height = IslandShape.Max.Height,
            Background = _theme.FillBrush,
        };
        pill.Children.Add(expandedHost);
        pill.Children.Add(compactHost);
        pill.Children.Add(notificationHost);

        var settings = new MenuFlyoutItem { Text = "Pengaturan" };
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        var exit = new MenuFlyoutItem { Text = "Keluar dari Lanting" };
        exit.Click += OnExitClick;
        var menu = new MenuFlyout();
        menu.Items.Add(settings);
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(exit);

        var root = new Canvas
        {
            Background = new SolidColorBrush(Colors.Transparent),
            ContextFlyout = menu,
        };
        root.Children.Add(pill);

        // Event dari tombol di dalam pil juga ikut, walau tombolnya sudah menanganinya.
        root.AddHandler(UIElement.PointerEnteredEvent, new PointerEventHandler(OnPointer), handledEventsToo: true);
        root.AddHandler(UIElement.PointerMovedEvent, new PointerEventHandler(OnPointer), handledEventsToo: true);
        root.AddHandler(UIElement.PointerExitedEvent, new PointerEventHandler(OnPointer), handledEventsToo: true);

        return (root, pill, compactHost, expandedHost, notificationHost);
    }

    private void OnPointer(object sender, PointerRoutedEventArgs e) => UpdatePointer();

    /// <summary>
    /// Mouse dianggap di atas pil hanya bila benar-benar di dalam bentuk pil saat ini, bukan sekadar di dalam window:
    /// selama animasi window sengaja lebih besar (ruang pantulan), dan mouse di ruang itu tidak boleh membuka pil.
    /// </summary>
    private void UpdatePointer()
    {
        var inside = _chrome.ContainsCursor();
        if (inside == _pointerInside)
        {
            return;
        }

        _pointerInside = inside;
        if (inside)
        {
            _stateMachine.OnPointerEntered();
        }
        else
        {
            _stateMachine.OnPointerExited();
        }
    }

    private void OnExitClick(object sender, RoutedEventArgs e) => ExitRequested?.Invoke(this, EventArgs.Empty);

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _controller.CurrentChanged -= OnContentChanged;
        _stateMachine.StateChanged -= OnStateChanged;
        _theme.Changed -= OnThemeChanged;
        _settleTimer.Stop();
    }
}
