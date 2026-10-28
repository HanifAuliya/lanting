using Lanting.App.Shell;
using Lanting.Core.Clock;
using Lanting.Core.Weather;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views.Dashboard;

/// <summary>Halaman Cuaca: cuaca saat ini di kiri, tanggal dan seberapa jauh hari ini sudah berjalan di kanan.</summary>
internal sealed class WeatherPage
{
    private const double BarWidth = 191;

    private readonly WeatherService _weather;
    private readonly TextBlock _place;
    private readonly TextBlock _icon = new() { FontSize = 30, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _temperature;
    private readonly TextBlock _description;
    private readonly TextBlock _range;
    private readonly TextBlock _date;
    private readonly TextBlock _percent;
    private readonly Border _dayFill;
    private readonly TextBlock _dayRemaining;
    private bool _enabled = true;
    private bool _loading;

    public WeatherPage(IslandTheme theme, WeatherService weather)
    {
        _weather = weather;
        _place = Caption(theme);
        _temperature = Text(theme.Primary, 34, bold: true);
        _description = Text(theme.Primary, 12);
        _range = Text(theme.Secondary, 12);
        _date = Caption(theme);
        _percent = Text(theme.Primary, 34, bold: true);
        _dayRemaining = Text(theme.Secondary, 12);

        // Kiri: cuaca.
        var now = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        now.Children.Add(_icon);
        now.Children.Add(_temperature);
        var weatherText = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Bottom };
        weatherText.Children.Add(_description);
        weatherText.Children.Add(_range);
        var weatherPanel = new Grid();
        weatherPanel.Children.Add(new StackPanel { Spacing = 2, Children = { _place, now } });
        weatherPanel.Children.Add(weatherText);

        // Kanan: hari ini.
        _dayFill = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = theme.Accent, HorizontalAlignment = HorizontalAlignment.Left };
        var track = new Grid { Width = BarWidth, Height = 6, HorizontalAlignment = HorizontalAlignment.Left };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(3), Background = theme.Control });
        track.Children.Add(_dayFill);
        var percentLine = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        percentLine.Children.Add(_percent);
        var passed = Text(theme.Secondary, 12);
        passed.Text = "hari ini berlalu";
        passed.VerticalAlignment = VerticalAlignment.Bottom;
        passed.Margin = new Thickness(0, 0, 0, 8);
        percentLine.Children.Add(passed);
        var dayBottom = new StackPanel { Spacing = 7, VerticalAlignment = VerticalAlignment.Bottom };
        dayBottom.Children.Add(track);
        dayBottom.Children.Add(_dayRemaining);
        var dayPanel = new Grid();
        dayPanel.Children.Add(new StackPanel { Spacing = 2, Children = { _date, percentLine } });
        dayPanel.Children.Add(dayBottom);

        var root = new Grid { ColumnSpacing = 10 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var weatherCard = Card(theme, weatherPanel);
        var dayCard = Card(theme, dayPanel);
        Grid.SetColumn(dayCard, 1);
        root.Children.Add(weatherCard);
        root.Children.Add(dayCard);
        Root = root;
        ShowWeather(null);
    }

    public FrameworkElement Root { get; }

    /// <summary>Dari pengaturan: cuaca dimatikan berarti tidak ada permintaan ke internet.</summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        ShowWeather(enabled ? _weather.Latest : null);
    }

    /// <summary>Halaman tampil: perbarui hari ini, dan cuaca bila sudah tidak segar.</summary>
    public async void Refresh()
    {
        var now = DateTime.Now;
        var progress = DayProgress.At(now);
        _date.Text = ClockTicker.Date(now).ToUpperInvariant();
        _percent.Text = $"{progress.Percent}%";
        _dayFill.Width = Math.Max(6, BarWidth * progress.Fraction);
        _dayRemaining.Text = $"Sisa {(int)progress.Remaining.TotalHours} jam {progress.Remaining.Minutes} menit";

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
        if (report is null)
        {
            _place.Text = "CUACA";
            _icon.Text = Emoji(WeatherKind.Cloudy);
            _temperature.Text = "--°";
            _description.Text = _enabled ? "Belum tersedia" : "Dimatikan di Pengaturan";
            _range.Text = _enabled ? "Perlu internet" : string.Empty;
            return;
        }

        _place.Text = report.Place.ToUpperInvariant();
        _icon.Text = Emoji(report.Kind);
        _temperature.Text = $"{Math.Round(report.TemperatureC)}°";
        _description.Text = report.Description;
        _range.Text = report is { MaxC: { } max, MinC: { } min }
            ? $"Tertinggi {Math.Round(max)}° · terendah {Math.Round(min)}°"
            : string.Empty;
    }

    // Emoji berwarna dari font Segoe UI Emoji; lebih jelas daripada ikon garis untuk cuaca.
    private static string Emoji(WeatherKind kind) => kind switch
    {
        WeatherKind.Clear => "☀️", // matahari
        WeatherKind.PartlyCloudy => "\U0001F324️", // matahari di balik awan
        WeatherKind.Cloudy => "☁️", // awan
        WeatherKind.Fog => "\U0001F32B️", // kabut
        WeatherKind.Drizzle => "\U0001F326️", // matahari dan hujan
        WeatherKind.Rain => "\U0001F327️", // awan hujan
        WeatherKind.Snow => "❄️", // salju
        _ => "⛈️", // awan petir
    };
}
