using System.Numerics;
using Lanting.App.Battery;
using Lanting.App.QuickToggles;
using Lanting.Core.Battery;
using Lanting.Core.Island;
using Lanting.Core.Settings;
using Lanting.Core.Weather;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views.Dashboard;

/// <summary>
/// Isi island saat dibuka (hover). Baris atas: jam, menu (Musik, Cuaca, Kontrol, Perangkat, Aplikasi), dan tombol Pengaturan.
/// Di bawahnya satu halaman saja, sesuai menu yang dipilih. Berukuran <see cref="Shell.IslandShape.Expanded"/>.
/// </summary>
internal sealed class DashboardView
{
    private const double TabWidth = 84;
    private const double TabHeight = 28;
    private const double TabPadding = 3;

    private readonly IslandTheme _theme;
    private readonly MediaView _media;
    private readonly WeatherPage _weather;
    private readonly TogglesPage _toggles;
    private readonly TextBlock _time;
    private readonly List<Tab> _tabs = [];
    private readonly Border _highlight;
    private Tab? _selected;

    public DashboardView(
        IslandTheme theme,
        MediaView media,
        WeatherService weather,
        IEnumerable<IQuickToggle> toggles,
        BatteryModule battery,
        InputDeviceWatcher inputs,
        SettingsStore settings,
        ClockTicker clock)
    {
        _theme = theme;
        _media = media;
        _weather = new WeatherPage(theme, weather);
        _toggles = new TogglesPage(theme, toggles);
        var apps = new AppsPage(theme);
        var devices = new DevicesPage(theme, battery, inputs, settings);

        var pages = new Grid { Width = PageWidth, Height = PageHeight };
        _tabs.Add(new Tab("Musik", "\uEC4F", media.Page));
        _tabs.Add(new Tab("Cuaca", "\uE753", _weather.Root, _weather.Refresh));
        _tabs.Add(new Tab("Kontrol", "\uE9E9", _toggles.Root, _toggles.Refresh));
        _tabs.Add(new Tab("Perangkat", "\uE772", devices.Root, devices.Refresh));
        _tabs.Add(new Tab("Aplikasi", "\uE71D", apps.Root));

        // Menu: tombol-tombol sama lebar, dengan sorotan yang bergeser ke menu yang dipilih.
        _highlight = new Border
        {
            Width = TabWidth,
            Height = TabHeight,
            CornerRadius = new CornerRadius(TabHeight / 2),
            Background = theme.ControlHover,
            HorizontalAlignment = HorizontalAlignment.Left,
        };
        ElementCompositionPreview.SetIsTranslationEnabled(_highlight, true);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var tab in _tabs)
        {
            tab.Button = TabButton(tab);
            tab.Button.Click += (_, _) => Select(tab, animate: true);
            buttons.Children.Add(tab.Button);
            tab.Page.Visibility = Visibility.Collapsed;
            pages.Children.Add(tab.Page);
        }

        var menu = new Border
        {
            Background = theme.Surface,
            CornerRadius = new CornerRadius((TabHeight / 2) + TabPadding),
            Padding = new Thickness(TabPadding),
            HorizontalAlignment = HorizontalAlignment.Center,
            Child = new Grid { Children = { _highlight, buttons } },
        };

        _time = Text(theme.Primary, 15, bold: true);
        _time.VerticalAlignment = VerticalAlignment.Center;
        _time.Margin = new Thickness(4, 0, 0, 0);

        var settingsButton = RoundButton(theme, "\uE713", "Pengaturan", 30, 14);
        settingsButton.HorizontalAlignment = HorizontalAlignment.Right;
        settingsButton.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        var header = new Grid { Width = PageWidth };
        header.Children.Add(_time);
        header.Children.Add(menu);
        header.Children.Add(settingsButton);

        var root = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Spacing = 12, HorizontalAlignment = HorizontalAlignment.Center };
        root.Children.Add(header);
        root.Children.Add(pages);
        Root = root;

        Select(_tabs[0], animate: false);
        ShowTime(DateTime.Now);
        clock.Tick += (_, now) => ShowTime(now);
    }

    /// <summary>Tombol ⚙️ diklik.</summary>
    public event EventHandler? SettingsRequested;

    public FrameworkElement Root { get; }

    /// <summary>Dashboard baru saja dibuka: baca ulang halaman yang tidak punya event (cuaca, kontrol).</summary>
    public void OnOpened()
    {
        ShowTime(DateTime.Now);
        _selected?.Refresh?.Invoke();
        if (_selected is not null)
        {
            Motion.Pop(_selected.Page);
        }
    }

    /// <summary>Konten island berganti. Saat tidak ada konten sama sekali, berarti tidak ada lagu.</summary>
    public void Show(IslandContent? content)
    {
        if (content is null)
        {
            _media.ShowEmpty();
        }
    }

    public void SetWeatherEnabled(bool enabled) => _weather.SetEnabled(enabled);

    private void Select(Tab tab, bool animate)
    {
        if (ReferenceEquals(tab, _selected))
        {
            return;
        }

        var previous = _selected;
        _selected = tab;
        foreach (var t in _tabs)
        {
            t.Button!.Foreground = ReferenceEquals(t, tab) ? _theme.Primary : _theme.Secondary;
        }

        if (previous is not null)
        {
            previous.Page.Visibility = Visibility.Collapsed;
        }

        tab.Page.Visibility = Visibility.Visible;
        tab.Refresh?.Invoke();

        // Sorotan menu meluncur ke menu yang dipilih.
        var visual = ElementCompositionPreview.GetElementVisual(_highlight);
        var target = new Vector3((float)(_tabs.IndexOf(tab) * TabWidth), 0f, 0f);
        if (!animate)
        {
            visual.Properties.InsertVector3("Translation", target);
            return;
        }

        var compositor = visual.Compositor;
        var slide = compositor.CreateVector3KeyFrameAnimation();
        slide.InsertKeyFrame(1f, target, compositor.CreateCubicBezierEasingFunction(new(0.2f, 0.9f), new(0.3f, 1f)));
        slide.Duration = TimeSpan.FromMilliseconds(260);
        visual.StartAnimation("Translation", slide);
        Motion.Pop(tab.Page);
    }

    private Button TabButton(Tab tab)
    {
        var label = Text(_theme.Secondary, 12, bold: true);
        label.Text = tab.Label;
        // Warna teks ikut tombol (redup, atau terang saat dipilih/disorot).
        label.ClearValue(TextBlock.ForegroundProperty);
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        content.Children.Add(Icon(tab.Glyph, 13));
        content.Children.Add(label);

        var button = Pressable(content, tab.Label, TabHeight / 2, _theme.Transparent, _theme.Surface, _theme.Transparent, _theme.Secondary);
        button.Resources["ButtonForegroundPointerOver"] = _theme.Primary;
        button.Resources["ButtonForegroundPressed"] = _theme.Primary;
        button.Width = TabWidth;
        button.Height = TabHeight;
        return button;
    }

    private void ShowTime(DateTime now) => _time.Text = ClockTicker.Time(now);

    private sealed class Tab(string label, string glyph, FrameworkElement page, Action? refresh = null)
    {
        public string Label { get; } = label;

        public string Glyph { get; } = glyph;

        public FrameworkElement Page { get; } = page;

        /// <summary>Dipanggil setiap kali halaman ini mulai terlihat.</summary>
        public Action? Refresh { get; } = refresh;

        public Button? Button { get; set; }
    }
}
