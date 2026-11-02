using Lanting.App.Battery;
using Lanting.Core.Battery;
using Lanting.Core.Island;
using Lanting.Core.Location;
using Lanting.Core.Prayer;
using Lanting.Core.Settings;
using Lanting.Core.Weather;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views.Dashboard;

/// <summary>
/// Isi island saat dibuka (hover). Baris atas: jam, menu (Musik, Cuaca, Sholat, Perangkat, Aplikasi), dan tombol Pengaturan.
/// Menu berupa tombol ikon bulat; yang dipilih melebar dan menampilkan namanya. Di bawahnya satu halaman sesuai menu.
/// Berukuran <see cref="Shell.IslandShape.Expanded"/>.
/// </summary>
internal sealed class DashboardView
{
    private const double TabSize = 32;
    private const double IconSize = 14;
    private const double LabelGap = 6;
    private static readonly TimeSpan TabDuration = TimeSpan.FromMilliseconds(280);

    private readonly IslandTheme _theme;
    private readonly MediaView _media;
    private readonly WeatherPage _weather;
    private readonly TextBlock _time;
    private readonly List<Tab> _tabs = [];
    private Tab? _selected;
    private bool _onScreen;

    public DashboardView(
        IslandTheme theme,
        MediaView media,
        WeatherService weather,
        PrayerModule prayer,
        LocationService location,
        BatteryModule battery,
        InputDeviceWatcher inputs,
        SettingsStore settings,
        ClockTicker clock)
    {
        _theme = theme;
        _media = media;
        _weather = new WeatherPage(theme, weather);
        var prayerPage = new PrayerPage(theme, prayer, location, clock);
        var devices = new DevicesPage(theme, battery, inputs, settings);
        var apps = new AppsPage(theme);

        _tabs.Add(new Tab("Musik", "\uEC4F", media.Page));
        _tabs.Add(new Tab("Cuaca", "\uE753", _weather.Root, _weather.Refresh));
        _tabs.Add(new Tab("Sholat", PrayerView.Glyph, prayerPage.Root, prayerPage.Refresh));
        _tabs.Add(new Tab("Perangkat", "\uE772", devices.Root, devices.Refresh));
        _tabs.Add(new Tab("Aplikasi", "\uE71D", apps.Root));

        var pages = new Grid { Width = PageWidth, Height = PageHeight };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2 };
        foreach (var tab in _tabs)
        {
            BuildButton(tab);
            tab.Label!.Opacity = 0;
            tab.Button!.Click += (_, _) => Select(tab, animate: true);
            buttons.Children.Add(tab.Button);
            tab.Page.Visibility = Visibility.Collapsed;
            pages.Children.Add(tab.Page);
        }

        var menu = new Border
        {
            Background = theme.Surface,
            CornerRadius = new CornerRadius((TabSize / 2) + 3),
            Padding = new Thickness(3),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = buttons,
        };

        _time = Text(theme.Primary, 15, bold: true);
        _time.VerticalAlignment = VerticalAlignment.Center;
        _time.Margin = new Thickness(6, 0, 0, 0);

        var settingsButton = RoundButton(theme, "\uE713", "Pengaturan", TabSize + 6, IconSize);
        settingsButton.HorizontalAlignment = HorizontalAlignment.Right;
        settingsButton.VerticalAlignment = VerticalAlignment.Center;
        settingsButton.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);

        var header = new Grid { Width = PageWidth, Height = TabSize + 6 };
        header.Children.Add(_time);
        header.Children.Add(menu);
        header.Children.Add(settingsButton);

        var root = new StackPanel { Margin = new Thickness(0, 9, 0, 0), Spacing = 10, HorizontalAlignment = HorizontalAlignment.Center };
        root.Children.Add(header);
        root.Children.Add(pages);
        Root = root;

        theme.Changed += (_, _) => PaintTabs();
        // LANTING_SMOKE_TAB=Sholat (dsb.) membuka menu itu dulu, untuk screenshot tes.
        var start = Environment.GetEnvironmentVariable("LANTING_SMOKE_TAB");
        Select(_tabs.FirstOrDefault(t => t.Name.Equals(start, StringComparison.OrdinalIgnoreCase)) ?? _tabs[0], animate: false);
        ShowTime(DateTime.Now);
        clock.Tick += (_, now) => ShowTime(now);
    }

    /// <summary>Tombol ⚙️ diklik.</summary>
    public event EventHandler? SettingsRequested;

    public FrameworkElement Root { get; }

    /// <summary>Dashboard baru saja dibuka: baca ulang halaman yang tidak punya event (cuaca, sholat, perangkat).</summary>
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

    /// <summary>Dashboard terlihat (island dibuka) atau tidak.</summary>
    public void SetOnScreen(bool onScreen)
    {
        _onScreen = onScreen;
        UpdateMediaOnScreen();
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
        PaintTabs();

        if (previous is not null)
        {
            previous.Page.Visibility = Visibility.Collapsed;
            Resize(previous, selected: false, animate);
        }

        Resize(tab, selected: true, animate);
        tab.Page.Visibility = Visibility.Visible;
        tab.Refresh?.Invoke();
        UpdateMediaOnScreen();
        if (animate)
        {
            Motion.Pop(tab.Page);
        }
    }

    private void BuildButton(Tab tab)
    {
        var icon = Icon(tab.Glyph, IconSize);
        icon.VerticalAlignment = VerticalAlignment.Center;
        tab.Label = Text(_theme.Secondary, 12, bold: true);
        tab.Label.Text = tab.Name;
        tab.Label.VerticalAlignment = VerticalAlignment.Center;
        // Warna teks ikut tombol (redup, atau terang saat dipilih).
        tab.Label.ClearValue(TextBlock.ForegroundProperty);
        tab.Label.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        tab.WideWidth = Math.Ceiling(TabSize + LabelGap + tab.Label.DesiredSize.Width + 4);

        // Isi rata kiri dengan ikon tepat di tengah lingkaran; saat menyempit, nama terpotong oleh sudut tombol.
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = LabelGap,
            HorizontalAlignment = HorizontalAlignment.Left,
            Margin = new Thickness((TabSize - IconSize) / 2, 0, 0, 0),
        };
        content.Children.Add(icon);
        content.Children.Add(tab.Label);

        var button = Pressable(content, tab.Name, TabSize / 2, tab.Paint.Rest, tab.Paint.Hover, tab.Paint.Pressed, tab.Paint.Foreground);
        button.HorizontalContentAlignment = HorizontalAlignment.Left;
        button.Width = TabSize;
        button.Height = TabSize;
        tab.Button = button;
    }

    private void PaintTabs()
    {
        foreach (var tab in _tabs)
        {
            if (ReferenceEquals(tab, _selected))
            {
                tab.Paint.CopyFrom(_theme.ControlHover, _theme.ControlHover, _theme.Control, _theme.Primary);
            }
            else
            {
                tab.Paint.CopyFrom(_theme.Transparent, _theme.Surface, _theme.Transparent, _theme.Secondary);
            }
        }
    }

    // Menu yang dipilih melebar sampai namanya terlihat; yang lain menyempit jadi lingkaran.
    private static void Resize(Tab tab, bool selected, bool animate)
    {
        var width = selected ? tab.WideWidth : TabSize;
        if (!animate)
        {
            tab.Button!.Width = width;
            tab.Label!.Opacity = selected ? 1 : 0;
            return;
        }

        var grow = new DoubleAnimation
        {
            To = width,
            Duration = TabDuration,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(grow, tab.Button);
        Storyboard.SetTargetProperty(grow, nameof(FrameworkElement.Width));
        var fade = new DoubleAnimation { To = selected ? 1 : 0, Duration = TabDuration };
        Storyboard.SetTarget(fade, tab.Label);
        Storyboard.SetTargetProperty(fade, nameof(UIElement.Opacity));
        var story = new Storyboard { Children = { grow, fade } };
        story.Begin();
    }

    private void UpdateMediaOnScreen() =>
        _media.SetPageOnScreen(_onScreen && ReferenceEquals(_selected?.Page, _media.Page));

    private void ShowTime(DateTime now) => _time.Text = ClockTicker.Time(now);

    private sealed class Tab(string name, string glyph, FrameworkElement page, Action? refresh = null)
    {
        public string Name { get; } = name;

        public string Glyph { get; } = glyph;

        public FrameworkElement Page { get; } = page;

        /// <summary>Dipanggil setiap kali halaman ini mulai terlihat.</summary>
        public Action? Refresh { get; } = refresh;

        public TilePaint Paint { get; } = new();

        public Button? Button { get; set; }

        public TextBlock? Label { get; set; }

        public double WideWidth { get; set; }
    }
}
