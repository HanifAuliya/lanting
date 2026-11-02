using Lanting.App.Shell;
using Lanting.Core.Weather;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views.Dashboard;

/// <summary>
/// Halaman Cuaca. Kiri: cuaca sekarang di atas warna yang sesuai cuacanya (cerah oranye, hujan biru, malam ungu tua),
/// dengan suhu terasa, kelembapan, angin, dan suhu tertinggi/terendah. Kanan: perkiraan beberapa jam ke depan.
/// </summary>
internal sealed class WeatherPage
{
    private const double NowWidth = 252;
    private const int Hours = 6;

    private readonly IslandTheme _theme;
    private readonly WeatherService _weather;
    private readonly TextBlock _place;
    private readonly TextBlock _icon = new() { FontSize = 34, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _temperature;
    private readonly TextBlock _description;
    private readonly TextBlock _feelsLike;
    private readonly TextBlock _humidity;
    private readonly TextBlock _wind;
    private readonly TextBlock _range;
    private readonly GradientStop _glowStart = new() { Offset = 0 };
    private readonly GradientStop _glowEnd = new() { Offset = 1 };
    private readonly List<HourCell> _hours = [];
    private readonly FrameworkElement _hoursPanel;
    private readonly TextBlock _hoursEmpty;
    private WeatherReport? _shown;
    private bool _enabled = true;
    private bool _loading;

    public WeatherPage(IslandTheme theme, WeatherService weather)
    {
        _theme = theme;
        _weather = weather;
        _place = Caption(theme);
        _temperature = Text(theme.Primary, 38, bold: true);
        _temperature.VerticalAlignment = VerticalAlignment.Center;
        _description = Text(theme.Primary, 13, bold: true);
        _description.TextWrapping = TextWrapping.WrapWholeWords;
        _description.MaxLines = 2;
        _description.MaxWidth = 92;
        _feelsLike = Text(theme.Secondary, 11);
        _humidity = Chip(theme, out var humidityChip);
        _wind = Chip(theme, out var windChip);
        _range = Chip(theme, out var rangeChip);

        // Kiri: sekarang.
        var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1, Margin = new Thickness(4, 0, 0, 0) };
        words.Children.Add(_description);
        words.Children.Add(_feelsLike);
        var now = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        now.Children.Add(_icon);
        now.Children.Add(_temperature);
        now.Children.Add(words);

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Bottom };
        chips.Children.Add(humidityChip);
        chips.Children.Add(windChip);
        chips.Children.Add(rangeChip);

        var nowPanel = new Grid();
        nowPanel.Children.Add(new StackPanel { Spacing = 2, Children = { _place, now } });
        nowPanel.Children.Add(chips);

        var glow = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1), GradientStops = { _glowStart, _glowEnd } },
        };
        var nowCard = new Grid
        {
            Width = NowWidth,
            CornerRadius = new CornerRadius(18),
            Background = theme.Surface,
            Children = { glow, new Border { Padding = new Thickness(14), Child = nowPanel } },
        };

        // Kanan: per jam.
        var columns = new Grid { ColumnSpacing = 2 };
        for (var i = 0; i < Hours; i++)
        {
            var cell = new HourCell(theme);
            columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(cell.Root, i);
            columns.Children.Add(cell.Root);
            _hours.Add(cell);
        }

        _hoursPanel = columns;
        _hoursEmpty = Text(theme.Secondary, 12);
        _hoursEmpty.HorizontalAlignment = HorizontalAlignment.Center;
        _hoursEmpty.VerticalAlignment = VerticalAlignment.Center;
        var hoursBody = new Grid { Children = { columns, _hoursEmpty } };
        var hoursPanel = new Grid { RowSpacing = 8 };
        hoursPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        hoursPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        hoursPanel.Children.Add(Caption(theme, "Beberapa jam ke depan"));
        Grid.SetRow(hoursBody, 1);
        hoursPanel.Children.Add(hoursBody);
        var hoursCard = Card(theme, hoursPanel, padding: 12);

        var root = new Grid { ColumnSpacing = 10 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(nowCard);
        Grid.SetColumn(hoursCard, 1);
        root.Children.Add(hoursCard);
        Root = root;

        theme.Changed += (_, _) => ShowWeather(_shown);
        ShowWeather(null);
    }

    public FrameworkElement Root { get; }

    /// <summary>Dari pengaturan: cuaca dimatikan berarti tidak ada permintaan ke internet.</summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        ShowWeather(enabled ? _weather.Latest : null);
    }

    /// <summary>Halaman tampil: perbarui cuaca bila sudah tidak segar.</summary>
    public async void Refresh()
    {
        if (!_enabled || _loading)
        {
            return;
        }

        _loading = true;
        try
        {
            ShowWeather(await _weather.GetAsync(CancellationToken.None));
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Cuaca gagal diambil: {ex.Message}");
            ShowWeather(null);
        }
        finally
        {
            _loading = false;
        }
    }

    private void ShowWeather(WeatherReport? report)
    {
        _shown = report;
        if (report is null)
        {
            _place.Text = "CUACA";
            _icon.Text = Emoji(WeatherKind.Cloudy, isDay: true);
            _temperature.Text = "--°";
            _description.Text = _enabled ? "Belum tersedia" : "Dimatikan";
            _feelsLike.Text = _enabled ? "Perlu internet" : "di Pengaturan";
            _humidity.Text = "-";
            _wind.Text = "-";
            _range.Text = "-";
            _hoursPanel.Visibility = Visibility.Collapsed;
            _hoursEmpty.Visibility = Visibility.Visible;
            _hoursEmpty.Text = _enabled ? "Menunggu data cuaca" : "Cuaca dimatikan";
            Tint(Colors.Gray);
            return;
        }

        _place.Text = report.Place.Split(',')[0].ToUpperInvariant();
        _icon.Text = Emoji(report.Kind, report.IsDay);
        _temperature.Text = Degrees(report.TemperatureC);
        _description.Text = report.Description;
        _feelsLike.Text = report.FeelsLikeC is { } feels ? $"Terasa {Degrees(feels)}" : string.Empty;
        _humidity.Text = report.Humidity is { } humidity ? $"\U0001F4A7 {humidity}%" : "-";
        _wind.Text = report.WindKmh is { } wind ? $"\U0001F4A8 {Math.Round(wind)} km/j" : "-";
        _range.Text = report is { MaxC: { } max, MinC: { } min } ? $"↑{Degrees(max)} ↓{Degrees(min)}" : "-";
        Tint(Mood(report.Kind, report.IsDay));

        var hours = report.Hourly.Take(Hours).ToList();
        _hoursPanel.Visibility = hours.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        _hoursEmpty.Visibility = hours.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        _hoursEmpty.Text = "Perkiraan per jam belum ada";
        for (var i = 0; i < _hours.Count; i++)
        {
            _hours[i].Show(i < hours.Count ? hours[i] : null);
        }
    }

    // Warna latar kartu "sekarang", memudar ke kanan bawah.
    private void Tint(Color color)
    {
        var alpha = (byte)(_theme.IsDark ? 0x66 : 0x55);
        _glowStart.Color = Color.FromArgb(alpha, color.R, color.G, color.B);
        _glowEnd.Color = Color.FromArgb(0x08, color.R, color.G, color.B);
    }

    private static Color Mood(WeatherKind kind, bool isDay) => (kind, isDay) switch
    {
        (WeatherKind.Clear or WeatherKind.PartlyCloudy, true) => Color.FromArgb(255, 0xF5, 0x9E, 0x0B),
        (WeatherKind.Clear or WeatherKind.PartlyCloudy, false) => Color.FromArgb(255, 0x63, 0x66, 0xF1),
        (WeatherKind.Drizzle or WeatherKind.Rain, _) => Color.FromArgb(255, 0x38, 0x8B, 0xFD),
        (WeatherKind.Thunderstorm, _) => Color.FromArgb(255, 0xA8, 0x55, 0xF7),
        (WeatherKind.Snow, _) => Color.FromArgb(255, 0x7D, 0xD3, 0xFC),
        _ => Color.FromArgb(255, 0x94, 0xA3, 0xB8),
    };

    private static string Degrees(double value) => $"{Math.Round(value)}°";

    private static TextBlock Chip(IslandTheme theme, out Border chip)
    {
        var text = Text(theme.Primary, 11, bold: true);
        chip = new Border
        {
            Background = theme.Control,
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(8, 3, 8, 4),
            Child = text,
        };
        return text;
    }

    // Emoji berwarna dari font Segoe UI Emoji; lebih jelas daripada ikon garis untuk cuaca.
    internal static string Emoji(WeatherKind kind, bool isDay) => kind switch
    {
        WeatherKind.Clear => isDay ? "☀️" : "\U0001F319", // matahari / bulan sabit
        WeatherKind.PartlyCloudy => isDay ? "\U0001F324️" : "☁️", // matahari di balik awan / awan
        WeatherKind.Cloudy => "☁️", // awan
        WeatherKind.Fog => "\U0001F32B️", // kabut
        WeatherKind.Drizzle => "\U0001F326️", // matahari dan hujan
        WeatherKind.Rain => "\U0001F327️", // awan hujan
        WeatherKind.Snow => "❄️", // salju
        _ => "⛈️", // awan petir
    };

    /// <summary>Satu kolom per jam: jam, ikon, suhu.</summary>
    private sealed class HourCell
    {
        private readonly TextBlock _time;
        private readonly TextBlock _icon = new() { FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly TextBlock _temperature;

        public HourCell(IslandTheme theme)
        {
            _time = Text(theme.Secondary, 11);
            _time.HorizontalAlignment = HorizontalAlignment.Center;
            _temperature = Text(theme.Primary, 13, bold: true);
            _temperature.HorizontalAlignment = HorizontalAlignment.Center;
            var stack = new StackPanel { Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(_time);
            stack.Children.Add(_icon);
            stack.Children.Add(_temperature);
            Root = stack;
        }

        public FrameworkElement Root { get; }

        public void Show(HourlyWeather? hour)
        {
            Root.Visibility = hour is null ? Visibility.Collapsed : Visibility.Visible;
            if (hour is null)
            {
                return;
            }

            _time.Text = ClockTicker.Time(hour.Time);
            _icon.Text = Emoji(hour.Kind, hour.IsDay);
            _temperature.Text = Degrees(hour.TemperatureC);
        }
    }
}
