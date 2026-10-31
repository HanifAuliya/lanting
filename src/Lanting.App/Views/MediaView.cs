using System.Runtime.InteropServices.WindowsRuntime;
using Lanting.App.Shell;
using Lanting.App.Views.Dashboard;
using Lanting.Core.Island;
using Lanting.Core.Media;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views;

/// <summary>
/// Tampilan lagu: pil kecil berisi gambar album, judul (atau baris lirik), dan visualizer;
/// <see cref="Page"/> (halaman Musik di dashboard) berisi gambar besar, judul, artis, lirik/nama pemutar,
/// tombol sebelumnya / putar / berikutnya, dan bar progres lagu, di atas cahaya lembut berwarna gambar albumnya.
/// </summary>
internal sealed class MediaView : IIslandView
{
    // Glyph Segoe Fluent Icons (juga ada di Segoe MDL2 Assets untuk Windows 10).
    private const string PlayGlyph = "\uE768";
    private const string PauseGlyph = "\uE769";
    private const string PreviousGlyph = "\uE892";
    private const string NextGlyph = "\uE893";
    private const string MusicGlyph = "\uEC4F";

    // Pil kecil lebih lebar saat lagu punya lirik, supaya satu baris lirik muat.
    private const double LyricsCompactWidth = 340;

    // Lebar tiga tombol: 38 + 10 + 44 + 10 + 38. Kolom lirik mulai tepat setelahnya, sejajar dengan bar progres.
    private const double ControlsWidth = 140;


    private static readonly Color DefaultAccent = Color.FromArgb(255, 0xE6, 0xE6, 0xE6);

    private readonly IMediaControls _controls;
    private readonly IslandTheme _theme;
    private readonly Art _compactArt;
    private readonly Art _pageArt;
    private readonly TextBlock _compactTitle;
    private readonly Visualizer _visualizer;
    private readonly TextBlock _title;
    private readonly TextBlock _artist;
    private readonly TextBlock _subline;
    private readonly TextBlock _lyricPrevious;
    private readonly TextBlock _lyricCurrent;
    private readonly TextBlock _lyricNext;
    private readonly FrameworkElement _lyricsColumn;
    private readonly FrameworkElement _info;
    private readonly Button _previous;
    private readonly Button _playPause;
    private readonly FontIcon _playPauseIcon;
    private readonly Button _next;
    private readonly SolidColorBrush _lyricBrush = new(DefaultAccent);
    private readonly GradientStop _glowStart = new() { Offset = 0 };
    private readonly GradientStop _glowEnd = new() { Offset = 1 };
    private readonly FrameworkElement _songPanel;
    private readonly FrameworkElement _emptyPanel;
    private readonly TextBlock _elapsed;
    private readonly TextBlock _total;
    private readonly Border _progressFill;
    private readonly FrameworkElement _progressRow;
    private readonly DispatcherQueueTimer _progressTimer;
    private PlaybackProgress? _progress;
    private bool _pageOnScreen;
    private Color _artColor = DefaultAccent;
    private NowPlaying? _song;
    private int _thumbnailVersion;

    public MediaView(IMediaControls controls, IslandTheme theme)
    {
        _controls = controls;
        _theme = theme;

        // Pil kecil: [gambar] judul ... [visualizer]
        _compactArt = new Art(theme, 22, 6);
        _compactTitle = Text(theme.Primary, 12);
        _compactTitle.VerticalAlignment = VerticalAlignment.Center;
        _visualizer = new Visualizer();

        var compact = new Grid { Margin = new Thickness(7, 6, 14, 6), ColumnSpacing = 8 };
        compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Place(compact, _compactArt.Root, 0, 0);
        Place(compact, _compactTitle, 0, 1);
        Place(compact, _visualizer.Root, 0, 2);
        Compact = compact;

        // Halaman Musik: [gambar besar] judul / artis / pemutar | lirik ... tombol.
        _pageArt = new Art(theme, PageHeight - 28, 8);
        _title = Text(theme.Primary, 16, bold: true);
        _artist = Text(theme.Secondary, 13);
        _subline = Text(theme.Secondary, 12);
        _subline.Margin = new Thickness(0, 4, 0, 0);

        var info = new StackPanel { Spacing = 1 };
        info.Children.Add(_title);
        info.Children.Add(_artist);
        info.Children.Add(_subline);
        _info = info;

        // Kolom lirik: baris sebelumnya dan berikutnya lebih kecil, baris sekarang berwarna album dan boleh dua baris.
        _lyricPrevious = Text(theme.Secondary, 11);
        _lyricPrevious.FontWeight = FontWeights.SemiBold;
        _lyricCurrent = Text(_lyricBrush, 13, bold: true);
        _lyricCurrent.FontWeight = FontWeights.Bold;
        _lyricCurrent.TextWrapping = TextWrapping.WrapWholeWords;
        _lyricCurrent.MaxLines = 2;
        _lyricNext = Text(theme.Secondary, 11);
        _lyricNext.FontWeight = FontWeights.SemiBold;
        var lyrics = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        lyrics.Children.Add(_lyricPrevious);
        lyrics.Children.Add(_lyricCurrent);
        lyrics.Children.Add(_lyricNext);
        lyrics.Visibility = Visibility.Collapsed;
        _lyricsColumn = lyrics;

        var top = new Grid { ColumnSpacing = 14 };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ControlsWidth) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Place(top, info, 0, 0);
        Place(top, lyrics, 0, 1);
        Grid.SetColumnSpan(info, 2);

        _playPauseIcon = Icon(PauseGlyph, 18);
        _playPauseIcon.HorizontalAlignment = HorizontalAlignment.Center;
        _previous = RoundButton(theme, PreviousGlyph, "Lagu sebelumnya", 38, 16);
        // Tombol utama memakai warna album, sama dengan bar progres dan lirik.
        _playPause = Pressable(_playPauseIcon, "Putar / jeda", 22, theme.Control, theme.ControlHover, theme.ControlPressed, theme.Primary);
        _playPause.Width = 44;
        _playPause.Height = 44;
        _next = RoundButton(theme, NextGlyph, "Lagu berikutnya", 38, 16);
        _previous.Click += async (_, _) => await _controls.PreviousAsync();
        _playPause.Click += async (_, _) => await _controls.PlayPauseAsync();
        _next.Click += async (_, _) => await _controls.NextAsync();

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, VerticalAlignment = VerticalAlignment.Bottom };
        buttons.Children.Add(_previous);
        buttons.Children.Add(_playPause);
        buttons.Children.Add(_next);

        // Bar progres di kanan tombol: "1:23 ━━━━━━━━ 3:45". Disembunyikan bila pemutar tidak memberi durasi.
        _elapsed = Text(theme.Secondary, 11);
        _total = Text(theme.Secondary, 11);
        _elapsed.VerticalAlignment = VerticalAlignment.Center;
        _total.VerticalAlignment = VerticalAlignment.Center;
        _progressFill = new Border { CornerRadius = new CornerRadius(2), Background = _lyricBrush, HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
        var track = new Grid
        {
            Height = 4,
            CornerRadius = new CornerRadius(2),
            Background = theme.ControlHover,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { _progressFill },
        };
        track.SizeChanged += (_, _) => ShowProgress();

        var progressRow = new Grid { ColumnSpacing = 8, VerticalAlignment = VerticalAlignment.Center };
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        progressRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Place(progressRow, _elapsed, 0, 0);
        Place(progressRow, track, 0, 1);
        Place(progressRow, _total, 0, 2);
        progressRow.Visibility = Visibility.Collapsed;
        _progressRow = progressRow;

        var bottomRow = new Grid { ColumnSpacing = 14, VerticalAlignment = VerticalAlignment.Bottom };
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        bottomRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Place(bottomRow, buttons, 0, 0);
        Place(bottomRow, progressRow, 0, 1);

        _progressTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
        _progressTimer.IsRepeating = false;
        _progressTimer.Tick += (_, _) => ShowProgress();

        // Dua baris grid supaya lirik dua baris terpotong, bukan menimpa tombol.
        var details = new Grid { RowSpacing = 6 };
        details.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Place(details, top, 0, 0);
        Place(details, bottomRow, 1, 0);

        var song = new Grid { ColumnSpacing = 16 };
        song.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        song.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Place(song, _pageArt.Root, 0, 0);
        Place(song, details, 0, 1);
        _songPanel = song;

        var empty = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Spacing = 6 };
        var emptyIcon = Icon(MusicGlyph, 26);
        emptyIcon.Foreground = theme.Tertiary;
        empty.Children.Add(emptyIcon);
        var emptyTitle = Text(theme.Primary, 13, bold: true);
        emptyTitle.Text = "Tidak ada lagu yang diputar";
        emptyTitle.HorizontalAlignment = HorizontalAlignment.Center;
        empty.Children.Add(emptyTitle);
        var emptyHint = Text(theme.Secondary, 12);
        emptyHint.Text = "Putar lagu di Spotify, YouTube, atau pemutar lain";
        emptyHint.HorizontalAlignment = HorizontalAlignment.Center;
        empty.Children.Add(emptyHint);
        _emptyPanel = empty;

        // Cahaya lembut warna gambar album, dari kiri (tempat gambar) memudar ke kanan.
        var glow = new Border
        {
            CornerRadius = new CornerRadius(18),
            Background = new LinearGradientBrush
            {
                StartPoint = new Point(0, 0.5),
                EndPoint = new Point(1, 0.5),
                GradientStops = { _glowStart, _glowEnd },
            },
        };
        var card = Card(theme, new Grid { Children = { song, empty } });
        card.Background = null;
        Page = new Grid
        {
            CornerRadius = new CornerRadius(18),
            Background = theme.Surface,
            Children = { glow, card },
        };

        theme.Changed += (_, _) => ApplyColor();
        ShowEmpty();
    }

    public UIElement Compact { get; }

    /// <summary>Halaman Musik di dashboard.</summary>
    public FrameworkElement Page { get; }

    public double CompactWidth => _song?.HasLyrics == true ? LyricsCompactWidth : IslandShape.Compact.Width;

    public bool CanShow(IslandContent content) => content.Payload is NowPlaying;

    public void SetCompactOnScreen(bool onScreen) => _visualizer.SetOnScreen(onScreen);

    /// <summary>Dari pengaturan: visualizer bergerak atau tidak.</summary>
    public void SetVisualizerEnabled(bool enabled) => _visualizer.SetEnabled(enabled);

    /// <summary>Halaman Musik mulai/berhenti terlihat; bar progres hanya berdetak selama terlihat.</summary>
    public void SetPageOnScreen(bool onScreen)
    {
        _pageOnScreen = onScreen;
        ShowProgress();
    }

    /// <summary>Tidak ada lagu (pemutar ditutup): halaman Musik menampilkan keadaan kosong.</summary>
    public void ShowEmpty()
    {
        _song = null;
        _songPanel.Visibility = Visibility.Collapsed;
        _emptyPanel.Visibility = Visibility.Visible;
        _visualizer.SetPlaying(false);
        _progress = null;
        ShowProgress();
        _artColor = DefaultAccent;
        ApplyColor(glow: false);
    }

    public void Show(IslandContent content)
    {
        if (content.Payload is not NowPlaying song)
        {
            return;
        }

        var previous = _song;
        _song = song;
        _songPanel.Visibility = Visibility.Visible;
        _emptyPanel.Visibility = Visibility.Collapsed;

        var newTrack = previous?.Title != song.Title || previous?.Artist != song.Artist;
        if (newTrack)
        {
            _title.Text = song.Title;
            _artist.Text = song.Artist;
            _artist.Visibility = string.IsNullOrEmpty(song.Artist) ? Visibility.Collapsed : Visibility.Visible;

            // Lagu baru: teks masuk dengan animasi. Play/pause saja tidak perlu.
            Motion.Pop(_title, _artist);
        }

        if (song.HasLyrics != (previous?.HasLyrics ?? false))
        {
            StartupLog.Write(song.HasLyrics ? "Media: lirik tersedia." : "Media: tanpa lirik.");
        }

        // Baris lirik menggantikan judul di pil kecil dan nama pemutar di halaman Musik.
        var compactText = song.Lyric ?? song.Title;
        if (newTrack || compactText != _compactTitle.Text)
        {
            _compactTitle.Text = compactText;
            Motion.Pop(_compactTitle);
        }

        if (newTrack || song.AppName != _subline.Text)
        {
            _subline.Text = song.AppName;
            Motion.Pop(_subline);
        }

        ShowLyrics(song, newTrack);

        _visualizer.SetPlaying(song.IsPlaying);
        Motion.FadeTo(_visualizer.Root, song.IsPlaying ? 1f : 0.5f);

        _playPauseIcon.Glyph = song.IsPlaying ? PauseGlyph : PlayGlyph;
        _playPause.IsEnabled = song.CanPlayPause;
        _previous.IsEnabled = song.CanPrevious;
        _next.IsEnabled = song.CanNext;
        _previous.Opacity = song.CanPrevious ? 1 : 0.4;
        _next.Opacity = song.CanNext ? 1 : 0.4;

        _progress = song.Progress;
        ShowProgress();

        if (!ReferenceEquals(song.Thumbnail, previous?.Thumbnail) || previous is null)
        {
            _ = LoadThumbnailAsync(song.Thumbnail);
        }
    }

    /// <summary>Kolom lirik muncul hanya bila lagu punya lirik; tanpa lirik, info lagu memakai seluruh lebar.</summary>
    private void ShowLyrics(NowPlaying song, bool newTrack)
    {
        var visible = song.HasLyrics;
        if (visible != (_lyricsColumn.Visibility == Visibility.Visible))
        {
            _lyricsColumn.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumnSpan(_info, visible ? 1 : 2);
        }

        if (!visible)
        {
            return;
        }

        // Sebelum baris pertama (intro), baris sekarang ditandai not balok.
        var current = song.Lyric ?? MediaModule.InstrumentalLyric;
        if (newTrack || current != _lyricCurrent.Text)
        {
            _lyricPrevious.Text = song.PreviousLyric ?? string.Empty;
            _lyricCurrent.Text = current;
            _lyricNext.Text = song.NextLyric ?? string.Empty;
            Motion.Pop(_lyricsColumn);
        }
    }

    /// <summary>Detak berikutnya jatuh tepat saat detik berganti, hanya bila lagu diputar dan halaman terlihat.</summary>
    private void ShowProgress()
    {
        _progressTimer.Stop();
        if (_progress is not { } progress)
        {
            _progressRow.Visibility = Visibility.Collapsed;
            return;
        }

        _progressRow.Visibility = Visibility.Visible;
        var position = progress.PositionAt(DateTimeOffset.UtcNow);
        _elapsed.Text = Minutes(position);
        _total.Text = Minutes(progress.Duration);
        if (_progressFill.Parent is FrameworkElement track)
        {
            _progressFill.Width = track.ActualWidth * (position / progress.Duration);
        }

        if (_pageOnScreen && progress.IsPlaying && position < progress.Duration)
        {
            _progressTimer.Interval = TimeSpan.FromMilliseconds(1000 - (position.TotalMilliseconds % 1000) + 20);
            _progressTimer.Start();
        }
    }

    /// <summary>"3:07", atau "1:02:07" untuk yang lebih dari satu jam.</summary>
    private static string Minutes(TimeSpan time) =>
        time.TotalHours >= 1 ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}" : $"{(int)time.TotalMinutes}:{time.Seconds:00}";

    private void ApplyColor(bool glow = true)
    {
        _visualizer.SetColor(_artColor);
        var accent = _theme.Readable(_artColor);
        _lyricBrush.Color = accent;

        var alpha = (byte)(!glow ? 0 : _theme.IsDark ? 0x55 : 0x40);
        _glowStart.Color = Color.FromArgb(alpha, _artColor.R, _artColor.G, _artColor.B);
        _glowEnd.Color = Color.FromArgb(0, _artColor.R, _artColor.G, _artColor.B);
    }

    private async Task LoadThumbnailAsync(byte[]? bytes)
    {
        var version = ++_thumbnailVersion;
        ImageSource? image = null;
        Rgb? accent = null;
        if (bytes is not null)
        {
            try
            {
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer());

                // Batasi ukuran decode: gambar YouTube bisa 1280x720 dan memakan memori beberapa MB.
                stream.Seek(0);
                var bitmap = new BitmapImage { DecodePixelType = DecodePixelType.Logical, DecodePixelWidth = 240 };
                await bitmap.SetSourceAsync(stream);
                image = bitmap;

                stream.Seek(0);
                accent = await PickAccentAsync(stream);
            }
            catch (Exception ex)
            {
                StartupLog.Write($"Media: gambar album gagal dibaca: {ex.Message}");
            }
        }

        // Lagu sudah berganti lagi selama decode: biarkan pemanggilan yang lebih baru yang menang.
        if (version != _thumbnailVersion)
        {
            return;
        }

        _compactArt.SetImage(image);
        _pageArt.SetImage(image);
        _artColor = accent is { } c ? Color.FromArgb(255, c.R, c.G, c.B) : DefaultAccent;
        ApplyColor(glow: _song is not null);
    }

    // Gambar diperkecil jadi 24x24 piksel dulu; untuk memilih warna itu sudah cukup dan sangat murah.
    private static async Task<Rgb?> PickAccentAsync(IRandomAccessStream stream)
    {
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var transform = new BitmapTransform { ScaledWidth = 24, ScaledHeight = 24, InterpolationMode = BitmapInterpolationMode.Linear };
        var data = await decoder.GetPixelDataAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Ignore,
            transform,
            ExifOrientationMode.IgnoreExifOrientation,
            ColorManagementMode.DoNotColorManage);
        return ArtworkColor.Pick(data.DetachPixelData());
    }

    private static void Place(Grid grid, FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    /// <summary>
    /// Kotak gambar album bersudut bulat. Gambar baru crossfade di atas yang lama (dua lapis yang bergantian),
    /// dengan ikon not balok di bawahnya saat gambar tidak ada.
    /// </summary>
    private sealed class Art
    {
        private readonly Border[] _layers = new Border[2];
        private readonly FontIcon _placeholder;
        private int _front;

        public Art(IslandTheme theme, double size, double cornerRadius)
        {
            _placeholder = new FontIcon
            {
                Glyph = MusicGlyph,
                FontSize = Math.Round(size * 0.4),
                Foreground = theme.Tertiary,
            };
            var root = new Grid
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(cornerRadius),
                Background = theme.Control,
                VerticalAlignment = VerticalAlignment.Center,
            };
            root.Children.Add(_placeholder);
            for (var i = 0; i < _layers.Length; i++)
            {
                _layers[i] = new Border { CornerRadius = new CornerRadius(cornerRadius) };
                ElementCompositionPreview.GetElementVisual(_layers[i]).Opacity = 0f;
                root.Children.Add(_layers[i]);
            }

            Root = root;
        }

        public FrameworkElement Root { get; }

        public void SetImage(ImageSource? image)
        {
            var outgoing = _layers[_front];
            _front ^= 1;
            var incoming = _layers[_front];

            incoming.Background = image is null ? null : new ImageBrush { ImageSource = image, Stretch = Stretch.UniformToFill };
            Canvas.SetZIndex(incoming, 2);
            Canvas.SetZIndex(outgoing, 1);
            ElementCompositionPreview.GetElementVisual(incoming).Opacity = 0f;
            Motion.FadeTo(incoming, 1f, 260);
            Motion.FadeTo(outgoing, 0f, 260);
            Motion.FadeTo(_placeholder, image is null ? 1f : 0f, 260);
        }
    }
}
