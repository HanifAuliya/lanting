using Lanting.App.Shell;
using Lanting.Core.Location;
using Lanting.Core.Prayer;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views.Dashboard;

/// <summary>
/// Halaman Sholat: waktu sholat berikutnya dengan hitung mundurnya di kiri, jadwal hari ini (Imsak sampai Isya) di kanan.
/// Yang berikutnya disorot warna aksen, yang sudah lewat diredupkan.
/// </summary>
internal sealed class PrayerPage
{
    private const double NextWidth = 168;

    private readonly IslandTheme _theme;
    private readonly PrayerModule _prayer;
    private readonly LocationService _location;
    private readonly TextBlock _place;
    private readonly TextBlock _nextName;
    private readonly TextBlock _nextTime;
    private readonly TextBlock _countdown;
    private readonly Grid _columns = new() { ColumnSpacing = 4, Height = 62 };
    private readonly TextBlock _date;
    private readonly List<Column> _cells = [];
    private readonly FrameworkElement _content;
    private readonly FrameworkElement _empty;
    private bool _visible;
    private bool _locating;

    public PrayerPage(IslandTheme theme, PrayerModule prayer, LocationService location, ClockTicker clock)
    {
        _theme = theme;
        _prayer = prayer;
        _location = location;

        // Kiri: berikutnya.
        _place = Caption(theme);
        _nextName = Text(theme.Primary, 15, bold: true);
        _nextTime = Text(theme.Primary, 34, bold: true);
        _countdown = Text(theme.Secondary, 12);
        var nextTop = new StackPanel { Spacing = 2 };
        nextTop.Children.Add(_place);
        nextTop.Children.Add(_nextName);
        var nextPanel = new Grid();
        nextPanel.Children.Add(nextTop);
        var nextBottom = new StackPanel { VerticalAlignment = VerticalAlignment.Bottom };
        nextBottom.Children.Add(_nextTime);
        nextBottom.Children.Add(_countdown);
        nextPanel.Children.Add(nextBottom);
        var nextCard = Card(theme, nextPanel);
        nextCard.Width = NextWidth;

        // Kanan: tujuh kolom jadwal.
        foreach (var _ in Enum.GetValues<PrayerName>())
        {
            var cell = new Column(theme);
            Grid.SetColumn(cell.Root, _columns.ColumnDefinitions.Count);
            _columns.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _columns.Children.Add(cell.Root);
            _cells.Add(cell);
        }

        _date = Caption(theme);
        _date.Margin = new Thickness(6, 0, 0, 0);
        var schedule = new StackPanel { Spacing = 12, VerticalAlignment = VerticalAlignment.Center };
        schedule.Children.Add(_date);
        schedule.Children.Add(_columns);
        var scheduleCard = Card(theme, schedule, padding: 8);
        var content = new Grid { ColumnSpacing = 10 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(nextCard);
        Grid.SetColumn(scheduleCard, 1);
        content.Children.Add(scheduleCard);
        _content = content;

        var emptyText = Text(theme.Secondary, 12);
        emptyText.Text = "Lokasi belum diketahui. Sambungkan internet sebentar, atau pilih kota di Pengaturan > Sholat.";
        emptyText.TextWrapping = TextWrapping.Wrap;
        emptyText.MaxLines = 2;
        emptyText.TextAlignment = TextAlignment.Center;
        emptyText.HorizontalAlignment = HorizontalAlignment.Center;
        emptyText.VerticalAlignment = VerticalAlignment.Center;
        emptyText.Margin = new Thickness(40, 0, 40, 0);
        _empty = Card(theme, emptyText);

        Root = new Grid { Children = { _content, _empty } };
        clock.Tick += (_, _) => Render();
        theme.Changed += (_, _) => Render();
    }

    public FrameworkElement Root { get; }

    /// <summary>Halaman tampil. Bila lokasi belum ada, coba cari dulu.</summary>
    public async void Refresh()
    {
        _visible = true;
        Render();
        if (_location.Current is not null || _locating)
        {
            return;
        }

        _locating = true;
        try
        {
            await _location.GetAsync(CancellationToken.None);
            _prayer.Reschedule();
        }
        catch (Exception ex)
        {
            StartupLog.Write($"Lokasi sholat gagal dicari: {ex.Message}");
        }
        finally
        {
            _locating = false;
        }

        Render();
    }

    private void Render()
    {
        if (!_visible)
        {
            return;
        }

        var schedule = _prayer.Today();
        _content.Visibility = schedule is null ? Visibility.Collapsed : Visibility.Visible;
        _empty.Visibility = schedule is null ? Visibility.Visible : Visibility.Collapsed;
        if (schedule is null)
        {
            return;
        }

        _place.Text = schedule.Location.Place.Split(',')[0].ToUpperInvariant();
        _date.Text = $"JADWAL {ClockTicker.Date(schedule.Now).ToUpperInvariant()}";
        _nextName.Text = schedule.Next.Label;
        _nextTime.Text = ClockTicker.Time(schedule.Next.Time);
        _countdown.Text = Countdown(schedule.Next.Time - schedule.Now);

        for (var i = 0; i < _cells.Count; i++)
        {
            var time = schedule.Today[i];
            var state = time.Time == schedule.Next.Time ? CellState.Next
                : time.Time <= schedule.Now ? CellState.Past
                : CellState.Later;
            _cells[i].Show(time, state);
        }
    }

    private static string Countdown(TimeSpan left)
    {
        var minutes = (int)Math.Ceiling(left.TotalMinutes);
        return minutes switch
        {
            <= 1 => "sebentar lagi",
            < 60 => $"{minutes} menit lagi",
            _ when minutes % 60 == 0 => $"{minutes / 60} jam lagi",
            _ => $"{minutes / 60} jam {minutes % 60} menit lagi",
        };
    }

    private enum CellState
    {
        Past,
        Next,
        Later,
    }

    /// <summary>Satu kolom: nama di atas, jam di bawah, dalam kapsul yang menyala saat ini waktu berikutnya.</summary>
    private sealed class Column
    {
        private readonly IslandTheme _theme;
        private readonly Border _pill;
        private readonly TextBlock _name;
        private readonly TextBlock _time;

        public Column(IslandTheme theme)
        {
            _theme = theme;
            _name = Text(theme.Secondary, 11);
            _name.HorizontalAlignment = HorizontalAlignment.Center;
            _time = Text(theme.Primary, 14, bold: true);
            _time.HorizontalAlignment = HorizontalAlignment.Center;
            var stack = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(_name);
            stack.Children.Add(_time);
            _pill = new Border { CornerRadius = new CornerRadius(14), Child = stack };
            Root = _pill;
        }

        public FrameworkElement Root { get; }

        public void Show(PrayerTime time, CellState state)
        {
            _name.Text = time.Label;
            _time.Text = ClockTicker.Time(time.Time);
            _pill.Background = state == CellState.Next ? _theme.Accent : null;
            _name.Foreground = state == CellState.Next ? _theme.OnAccent : _theme.Secondary;
            _time.Foreground = state == CellState.Next ? _theme.OnAccent : _theme.Primary;
            _pill.Opacity = state == CellState.Past ? 0.45 : 1;
        }
    }
}
