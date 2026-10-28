using Lanting.App.Shell;
using Lanting.Core.Clock;
using Lanting.Core.Weather;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views.Dashboard;

/// <summary>Cuaca saat ini dan seberapa jauh hari ini sudah berjalan.</summary>
internal sealed class WeatherCard
{
    private const double BarWidth = 136;

    private readonly WeatherService _weather;
    private readonly TextBlock _place = Caption("Cuaca");
    private readonly TextBlock _icon = new() { FontSize = 24, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _temperature = Text(26, Colors.White, bold: true);
    private readonly TextBlock _description = Text(11, Muted);
    private readonly TextBlock _dayLabel = Caption("Hari ini");
    private readonly Border _dayFill = new() { Height = 4, CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(Accent), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly TextBlock _dayRemaining = Text(11, Muted);
    private bool _enabled = true;
    private bool _loading;

    public WeatherCard(WeatherService weather)
    {
        _weather = weather;

        var now = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        now.Children.Add(_icon);
        now.Children.Add(_temperature);

        var track = new Grid { Width = BarWidth, Height = 4, HorizontalAlignment = HorizontalAlignment.Left };
        track.Children.Add(new Border { CornerRadius = new CornerRadius(2), Background = new SolidColorBrush(ButtonColor) });
        track.Children.Add(_dayFill);

        var day = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Bottom };
        day.Children.Add(_dayLabel);
        day.Children.Add(track);
        day.Children.Add(_dayRemaining);

        var weatherPanel = new StackPanel { Spacing = 2 };
        weatherPanel.Children.Add(_place);
        weatherPanel.Children.Add(now);
        weatherPanel.Children.Add(_description);

        var content = new Grid();
        content.Children.Add(weatherPanel);
        content.Children.Add(day);
        Root = Card(null, content);
        ShowWeather(null);
    }

    public FrameworkElement Root { get; }

    /// <summary>Dari pengaturan: cuaca dimatikan berarti tidak ada permintaan ke internet.</summary>
    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        ShowWeather(enabled ? _weather.Latest : null);
    }

    /// <summary>Dashboard dibuka: perbarui progres hari, dan cuaca bila sudah tidak segar.</summary>
    public async void Refresh()
    {
        var progress = DayProgress.At(DateTime.Now);
        _dayLabel.Text = $"HARI INI {progress.Percent}%";
        _dayFill.Width = Math.Max(4, BarWidth * progress.Fraction);
        _dayRemaining.Text = $"sisa {(int)progress.Remaining.TotalHours} j {progress.Remaining.Minutes} m";

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
            return;
        }

        _place.Text = report.Place.ToUpperInvariant();
        _icon.Text = Emoji(report.Kind);
        _temperature.Text = $"{Math.Round(report.TemperatureC)}°";
        _description.Text = report is { MaxC: { } max, MinC: { } min }
            ? $"{report.Description} · {Math.Round(max)}°/{Math.Round(min)}°"
            : report.Description;
    }

    // Emoji berwarna dari font Segoe UI Emoji; lebih jelas daripada ikon garis untuk cuaca.
    private static string Emoji(WeatherKind kind) => kind switch
    {
        WeatherKind.Clear => "\u2600\uFE0F", // matahari
        WeatherKind.PartlyCloudy => "\U0001F324\uFE0F", // matahari di balik awan
        WeatherKind.Cloudy => "\u2601\uFE0F", // awan
        WeatherKind.Fog => "\U0001F32B\uFE0F", // kabut
        WeatherKind.Drizzle => "\U0001F326\uFE0F", // matahari dan hujan
        WeatherKind.Rain => "\U0001F327\uFE0F", // awan hujan
        WeatherKind.Snow => "\u2744\uFE0F", // salju
        _ => "\u26C8\uFE0F", // awan petir
    };
}
