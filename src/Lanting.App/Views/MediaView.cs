using System.Runtime.InteropServices.WindowsRuntime;
using Lanting.App.Shell;
using Lanting.Core.Island;
using Lanting.Core.Media;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace Lanting.App.Views;

/// <summary>
/// Tampilan lagu: pil kecil berisi gambar album, judul, dan status putar;
/// pil besar berisi gambar, judul, artis, nama pemutar, dan tombol sebelumnya / putar / berikutnya.
/// </summary>
internal sealed class MediaView : IIslandView
{
    // Glyph Segoe Fluent Icons (juga ada di Segoe MDL2 Assets untuk Windows 10).
    private const string PlayGlyph = "";
    private const string PauseGlyph = "";
    private const string PreviousGlyph = "";
    private const string NextGlyph = "";
    private const string MusicGlyph = "";

    private static readonly Color Accent = Color.FromArgb(255, 0x2D, 0xD4, 0xBF);
    private static readonly Color Muted = Color.FromArgb(255, 0xA0, 0xA0, 0xA0);
    private static readonly Color Dim = Color.FromArgb(255, 0x70, 0x70, 0x70);
    private static readonly Color ArtBackground = Color.FromArgb(255, 0x26, 0x26, 0x26);

    private readonly IMediaControls _controls;
    private readonly Art _compactArt;
    private readonly Art _expandedArt;
    private readonly TextBlock _compactTitle;
    private readonly FontIcon _compactStatus;
    private readonly TextBlock _title;
    private readonly TextBlock _artist;
    private readonly TextBlock _app;
    private readonly Button _previous;
    private readonly Button _playPause;
    private readonly FontIcon _playPauseIcon;
    private readonly Button _next;
    private byte[]? _thumbnail;
    private int _thumbnailVersion;

    public MediaView(IMediaControls controls)
    {
        _controls = controls;

        // Pil kecil (200x34): [gambar] judul ... [status]
        _compactArt = new Art(22, 6);
        _compactTitle = Text(12, Colors.White);
        _compactTitle.VerticalAlignment = VerticalAlignment.Center;
        _compactStatus = new FontIcon { FontSize = 12, VerticalAlignment = VerticalAlignment.Center };

        var compact = new Grid { Margin = new Thickness(7, 6, 14, 6), ColumnSpacing = 8 };
        compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Place(compact, _compactArt.Root, 0, 0);
        Place(compact, _compactTitle, 0, 1);
        Place(compact, _compactStatus, 0, 2);
        Compact = compact;

        // Pil besar (400x160).
        _expandedArt = new Art(72, 14);
        _title = Text(16, Colors.White);
        _title.FontWeight = FontWeights.SemiBold;
        _artist = Text(13, Muted);
        _app = Text(11, Dim);
        _app.Margin = new Thickness(0, 2, 0, 0);

        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        info.Children.Add(_title);
        info.Children.Add(_artist);
        info.Children.Add(_app);

        _playPauseIcon = new FontIcon { Glyph = PauseGlyph, FontSize = 20 };
        _previous = ControlButton(new FontIcon { Glyph = PreviousGlyph, FontSize = 16 }, "Lagu sebelumnya", () => _controls.PreviousAsync());
        _playPause = ControlButton(_playPauseIcon, "Putar / jeda", () => _controls.PlayPauseAsync());
        _next = ControlButton(new FontIcon { Glyph = NextGlyph, FontSize = 16 }, "Lagu berikutnya", () => _controls.NextAsync());

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 12,
        };
        buttons.Children.Add(_previous);
        buttons.Children.Add(_playPause);
        buttons.Children.Add(_next);

        var expanded = new Grid { Margin = new Thickness(24, 18, 24, 12), ColumnSpacing = 14, RowSpacing = 6 };
        expanded.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        expanded.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        expanded.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        expanded.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        Place(expanded, _expandedArt.Root, 0, 0);
        Place(expanded, info, 0, 1);
        Place(expanded, buttons, 1, 0);
        Grid.SetColumnSpan(buttons, 2);
        Expanded = expanded;
    }

    public UIElement Compact { get; }

    public UIElement Expanded { get; }

    public bool CanShow(IslandContent content) => content.Payload is NowPlaying;

    public void Show(IslandContent content)
    {
        if (content.Payload is not NowPlaying song)
        {
            return;
        }

        _compactTitle.Text = song.Title;
        _compactStatus.Glyph = song.IsPlaying ? MusicGlyph : PauseGlyph;
        _compactStatus.Foreground = new SolidColorBrush(song.IsPlaying ? Accent : Muted);

        _title.Text = song.Title;
        _artist.Text = song.Artist;
        _artist.Visibility = string.IsNullOrEmpty(song.Artist) ? Visibility.Collapsed : Visibility.Visible;
        _app.Text = song.AppName;

        _playPauseIcon.Glyph = song.IsPlaying ? PauseGlyph : PlayGlyph;
        _playPause.IsEnabled = song.CanPlayPause;
        _previous.IsEnabled = song.CanPrevious;
        _next.IsEnabled = song.CanNext;

        if (!ReferenceEquals(song.Thumbnail, _thumbnail))
        {
            _thumbnail = song.Thumbnail;
            _ = LoadThumbnailAsync(song.Thumbnail);
        }
    }

    private async Task LoadThumbnailAsync(byte[]? bytes)
    {
        var version = ++_thumbnailVersion;
        ImageSource? image = null;
        if (bytes is not null)
        {
            try
            {
                // Batasi ukuran decode: gambar YouTube bisa 1280x720 dan memakan memori beberapa MB.
                var bitmap = new BitmapImage { DecodePixelType = DecodePixelType.Logical, DecodePixelWidth = 160 };
                using var stream = new InMemoryRandomAccessStream();
                await stream.WriteAsync(bytes.AsBuffer());
                stream.Seek(0);
                await bitmap.SetSourceAsync(stream);
                image = bitmap;
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
        _expandedArt.SetImage(image);
    }

    private static TextBlock Text(double size, Color color) => new()
    {
        FontSize = size,
        Foreground = new SolidColorBrush(color),
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        MaxLines = 1,
    };

    private static Button ControlButton(FontIcon icon, string label, Func<Task> action)
    {
        icon.Foreground = new SolidColorBrush(Colors.White);
        var button = new Button
        {
            Content = icon,
            Width = 44,
            Height = 36,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(18),
            Background = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
        };
        ToolTipService.SetToolTip(button, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        button.Click += async (_, _) => await action();
        return button;
    }

    private static void Place(Grid grid, FrameworkElement element, int row, int column)
    {
        Grid.SetRow(element, row);
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }

    /// <summary>Kotak gambar album bersudut bulat, dengan ikon not balok saat gambar belum ada.</summary>
    private sealed class Art
    {
        private readonly Border _border;
        private readonly FontIcon _placeholder;

        public Art(double size, double cornerRadius)
        {
            _placeholder = new FontIcon
            {
                Glyph = MusicGlyph,
                FontSize = Math.Round(size * 0.45),
                Foreground = new SolidColorBrush(Muted),
            };
            _border = new Border
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(cornerRadius),
                Background = new SolidColorBrush(ArtBackground),
                VerticalAlignment = VerticalAlignment.Center,
                Child = _placeholder,
            };
        }

        public FrameworkElement Root => _border;

        public void SetImage(ImageSource? image)
        {
            _border.Background = image is null
                ? new SolidColorBrush(ArtBackground)
                : new ImageBrush { ImageSource = image, Stretch = Stretch.UniformToFill };
            _placeholder.Visibility = image is null ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
