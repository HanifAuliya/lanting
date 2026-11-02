using System.Numerics;
using Lanting.App.Shell;
using Lanting.Core.Location;
using Lanting.Core.Prayer;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views.Dashboard;

/// <summary>
/// Halaman Sholat. Kiri: sholat berikutnya dengan cincin yang terisi seiring waktu berjalan dari sholat sebelumnya,
/// dan hitung mundurnya. Kanan: jadwal hari ini (Imsak sampai Isya) dengan emoji suasana tiap waktu;
/// yang berikutnya disorot warna aksen, yang sudah lewat diredupkan.
/// </summary>
internal sealed class PrayerPage
{
    private const double NextWidth = 176;
    private const float Ring = 54;

    private readonly IslandTheme _theme;
    private readonly PrayerModule _prayer;
    private readonly LocationService _location;
    private readonly TextBlock _place;
    private readonly TextBlock _nextIcon = new() { FontSize = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _nextName;
    private readonly TextBlock _nextTime;
    private readonly TextBlock _countdown;
    private readonly TextBlock _date;
    private readonly CompositionEllipseGeometry _progress;
    private readonly CompositionColorBrush _trackBrush;
    private readonly CompositionColorBrush _progressBrush;
    private readonly Grid _columns = new() { ColumnSpacing = 3 };
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

        // Kiri: berikutnya, dengan cincin progres.
        _place = Caption(theme);
        _nextName = Text(theme.Secondary, 13, bold: true);
        _nextTime = Text(theme.Primary, 28, bold: true);
        _countdown = Text(theme.Secondary, 12);

        var ringHost = new Grid { Width = Ring, Height = Ring, VerticalAlignment = VerticalAlignment.Center };
        var compositor = ElementCompositionPreview.GetElementVisual(ringHost).Compositor;
        _trackBrush = compositor.CreateColorBrush();
        _progressBrush = compositor.CreateColorBrush();
        var track = compositor.CreateEllipseGeometry();
        track.Center = new Vector2(Ring / 2, Ring / 2);
        track.Radius = new Vector2((Ring / 2) - 3, (Ring / 2) - 3);
        _progress = compositor.CreateEllipseGeometry();
        _progress.Center = track.Center;
        _progress.Radius = track.Radius;
        _progress.TrimEnd = 0;
        var trackShape = compositor.CreateSpriteShape(track);
        trackShape.StrokeBrush = _trackBrush;
        trackShape.StrokeThickness = 5;
        var progressShape = compositor.CreateSpriteShape(_progress);
        progressShape.StrokeBrush = _progressBrush;
        progressShape.StrokeThickness = 5;
        progressShape.StrokeStartCap = CompositionStrokeCap.Round;
        progressShape.StrokeEndCap = CompositionStrokeCap.Round;
        var rings = compositor.CreateShapeVisual();
        rings.Size = new Vector2(Ring, Ring);
        rings.Shapes.Add(trackShape);
        rings.Shapes.Add(progressShape);
        ElementCompositionPreview.SetElementChildVisual(ringHost, rings);
        ringHost.Children.Add(_nextIcon);

        var nextWords = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        nextWords.Children.Add(_nextName);
        nextWords.Children.Add(_nextTime);
        var nextRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
        nextRow.Children.Add(ringHost);
        nextRow.Children.Add(nextWords);

        var nextPanel = new Grid();
        nextPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        nextPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        nextPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        nextPanel.Children.Add(_place);
        nextRow.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(nextRow, 1);
        nextPanel.Children.Add(nextRow);
        Grid.SetRow(_countdown, 2);
        nextPanel.Children.Add(_countdown);
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
        _date.Margin = new Thickness(4, 0, 0, 0);
        var schedule = new Grid { RowSpacing = 8 };
        schedule.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        schedule.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        schedule.Children.Add(_date);
        Grid.SetRow(_columns, 1);
        schedule.Children.Add(_columns);
        var scheduleCard = Card(theme, schedule, padding: 10);

        var content = new Grid { ColumnSpacing = 10 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(nextCard);
        Grid.SetColumn(scheduleCard, 1);
        content.Children.Add(scheduleCard);
        _content = content;

        var emptyText = Text(theme.Secondary, 12);
        emptyText.Text = "Lokasi belum diketahui. Sambungkan internet sebentar, atau pilih kota di Pengaturan > Sholat dan lokasi.";
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
        Render(animate: true);
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

        Render(animate: true);
    }

    /// <summary>Emoji suasana tiap waktu.</summary>
    internal static string Emoji(PrayerName name) => name switch
    {
        PrayerName.Imsak => "\U0001F319", // bulan sabit
        PrayerName.Subuh => "\U0001F304", // fajar di pegunungan
        PrayerName.Terbit => "\U0001F305", // matahari terbit
        PrayerName.Dzuhur => "☀️", // matahari
        PrayerName.Ashar => "\U0001F324️", // matahari di balik awan
        PrayerName.Maghrib => "\U0001F307", // senja di kota
        _ => "\U0001F303", // malam berbintang
    };

    private void Render() => Render(animate: false);

    private void Render(bool animate)
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
        _date.Text = ClockTicker.Date(schedule.Now).ToUpperInvariant();
        _nextIcon.Text = Emoji(schedule.Next.Name);
        _nextName.Text = schedule.Next.Label;
        _nextTime.Text = ClockTicker.Time(schedule.Next.Time);
        _countdown.Text = Countdown(schedule.Next.Time - schedule.Now);

        // Cincin: seberapa jauh waktu sudah berjalan dari sholat sebelumnya ke sholat berikutnya.
        _trackBrush.Color = _theme.Control.Color;
        _progressBrush.Color = _theme.EyeColor;
        var previous = Previous(schedule);
        var span = schedule.Next.Time - previous;
        var fraction = span > TimeSpan.Zero ? (float)Math.Clamp((schedule.Now - previous) / span, 0.02, 1) : 0f;
        if (animate)
        {
            var fill = _progress.Compositor.CreateScalarKeyFrameAnimation();
            fill.InsertKeyFrame(0f, 0f);
            fill.InsertKeyFrame(1f, fraction, _progress.Compositor.CreateCubicBezierEasingFunction(new(0.2f, 0.8f), new(0.3f, 1f)));
            fill.Duration = TimeSpan.FromMilliseconds(700);
            _progress.StartAnimation(nameof(CompositionGeometry.TrimEnd), fill);
        }
        else
        {
            _progress.TrimEnd = fraction;
        }

        for (var i = 0; i < _cells.Count; i++)
        {
            var time = schedule.Today[i];
            var state = time.Time == schedule.Next.Time ? CellState.Next
                : time.Time <= schedule.Now ? CellState.Past
                : CellState.Later;
            _cells[i].Show(time, state);
        }
    }

    // Sholat wajib terakhir yang sudah masuk; sebelum Subuh berarti Isya kemarin.
    private static DateTime Previous(PrayerSchedule schedule)
    {
        var passed = schedule.Today.LastOrDefault(t => t.IsPrayer && t.Time <= schedule.Now);
        if (passed is not null)
        {
            return passed.Time;
        }

        var isya = schedule.Today.Last(t => t.Name == PrayerName.Isya).Time;
        return isya.AddDays(-1);
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

    /// <summary>Satu kolom: emoji, nama, jam, dalam kapsul yang menyala saat ini waktu berikutnya.</summary>
    private sealed class Column
    {
        private readonly IslandTheme _theme;
        private readonly Border _pill;
        private readonly TextBlock _icon = new() { FontSize = 15, HorizontalAlignment = HorizontalAlignment.Center };
        private readonly TextBlock _name;
        private readonly TextBlock _time;

        public Column(IslandTheme theme)
        {
            _theme = theme;
            _name = Text(theme.Secondary, 10.5);
            _name.HorizontalAlignment = HorizontalAlignment.Center;
            _name.TextTrimming = TextTrimming.None;
            _time = Text(theme.Primary, 13, bold: true);
            _time.HorizontalAlignment = HorizontalAlignment.Center;
            var stack = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
            stack.Children.Add(_icon);
            stack.Children.Add(_name);
            stack.Children.Add(_time);
            _pill = new Border { CornerRadius = new CornerRadius(14), Child = stack };
            Root = _pill;
        }

        public FrameworkElement Root { get; }

        public void Show(PrayerTime time, CellState state)
        {
            _icon.Text = Emoji(time.Name);
            _name.Text = time.Label;
            _time.Text = ClockTicker.Time(time.Time);
            _pill.Background = state == CellState.Next ? _theme.Accent : null;
            _name.Foreground = state == CellState.Next ? _theme.OnAccent : _theme.Secondary;
            _time.Foreground = state == CellState.Next ? _theme.OnAccent : _theme.Primary;
            _pill.Opacity = state == CellState.Past ? 0.4 : 1;
        }
    }
}
