using Lanting.App.QuickToggles;
using Lanting.Core.Island;
using Lanting.Core.Weather;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views.Dashboard;

/// <summary>
/// Isi island saat dibuka (hover): tanggal, jam, tombol Pengaturan, lalu kartu musik, cuaca + hari,
/// kontrol cepat, dan aplikasi. Berukuran <see cref="Shell.IslandShape.Expanded"/>.
/// </summary>
internal sealed class DashboardView
{
    private readonly MediaView _media;
    private readonly WeatherCard _weather;
    private readonly TogglesCard _toggles;
    private readonly TextBlock _date = Text(12, Muted);
    private readonly TextBlock _time = Text(20, Colors.White, bold: true);

    public DashboardView(MediaView media, WeatherService weather, IEnumerable<IQuickToggle> toggles, ClockTicker clock)
    {
        _media = media;
        _weather = new WeatherCard(weather);
        _toggles = new TogglesCard(toggles);
        var apps = new AppsCard();

        // Baris atas: tanggal | jam | Pengaturan.
        var settings = IconButton("\uE713", "Pengaturan", 32, 16);
        settings.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Colors.Transparent);
        settings.HorizontalAlignment = HorizontalAlignment.Right;
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        _date.VerticalAlignment = VerticalAlignment.Center;
        _time.HorizontalAlignment = HorizontalAlignment.Center;
        _time.VerticalAlignment = VerticalAlignment.Center;

        var header = new Grid { Height = 36 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Place(header, _date, 0);
        Place(header, _time, 1);
        Place(header, settings, 2);

        // Kartu-kartu: musik | cuaca + hari | kontrol | aplikasi.
        var cards = new Grid { ColumnSpacing = 10 };
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(250) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(160) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
        cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Place(cards, Card(null, media.Card), 0);
        Place(cards, _weather.Root, 1);
        Place(cards, _toggles.Root, 2);
        Place(cards, apps.Root, 3);

        var root = new Grid { Margin = new Thickness(18, 8, 18, 16), RowSpacing = 8 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(header);
        Grid.SetRow(cards, 1);
        root.Children.Add(cards);
        Root = root;

        ShowTime(DateTime.Now);
        clock.Tick += (_, now) => ShowTime(now);
    }

    /// <summary>Tombol ⚙️ diklik.</summary>
    public event EventHandler? SettingsRequested;

    public FrameworkElement Root { get; }

    /// <summary>Dashboard baru saja dibuka: baca ulang hal-hal yang tidak punya event (cuaca, kontrol, progres hari).</summary>
    public void OnOpened()
    {
        ShowTime(DateTime.Now);
        _weather.Refresh();
        _toggles.Refresh();
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

    private void ShowTime(DateTime now)
    {
        _date.Text = ClockTicker.Date(now);
        _time.Text = ClockTicker.Time(now);
    }

    private static void Place(Grid grid, FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }
}
