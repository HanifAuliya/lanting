using System.Runtime.InteropServices.WindowsRuntime;
using Lanting.App.Shell;
using Lanting.Core.Island;
using Lanting.Core.Media;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.UI;

namespace Lanting.App.Views;

/// <summary>
/// Tampilan lagu: pil kecil berisi gambar album, judul, dan visualizer;
/// pil besar berisi gambar, judul, artis, nama pemutar, dan tombol sebelumnya / putar / berikutnya.
/// </summary>
internal sealed class MediaView : IIslandView
{
    // Glyph Segoe Fluent Icons (juga ada di Segoe MDL2 Assets untuk Windows 10).
    private const string PlayGlyph = "\uE768";
    private const string PauseGlyph = "\uE769";
    private const string PreviousGlyph = "\uE892";
    private const string NextGlyph = "\uE893";
    private const string MusicGlyph = "\uEC4F";

    private static readonly Color DefaultAccent = Color.FromArgb(255, 0x2D, 0xD4, 0xBF);
    private static readonly Color Muted = Color.FromArgb(255, 0xA0, 0xA0, 0xA0);
    private static readonly Color Dim = Color.FromArgb(255, 0x70, 0x70, 0x70);
    private static readonly Color ArtBackground = Color.FromArgb(255, 0x26, 0x26, 0x26);

    private readonly IMediaControls _controls;
    private readonly Art _compactArt;
    private readonly Art _expandedArt;
    private readonly TextBlock _compactTitle;
    private readonly Visualizer _visualizer;
    private readonly TextBlock _title;
    private readonly TextBlock _artist;
    private readonly TextBlock _app;
    private readonly Button _previous;
    private readonly Button _playPause;
    private readonly FontIcon _playPauseIcon;
    private readonly Button _next;
    private NowPlaying? _song;
    private int _thumbnailVersion;

    public MediaView(IMediaControls controls)
    {
        _controls = controls;

        // Pil kecil (200x34): [gambar] judul ... [visualizer]
        _compactArt = new Art(22, 6);
        _compactTitle = Text(12, Colors.White);
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

        var previous = _song;
        _song = song;

        if (previous?.Title != song.Title || previous?.Artist != song.Artist)
        {
            _compactTitle.Text = song.Title;
            _title.Text = song.Title;
            _artist.Text = song.Artist;
            _artist.Visibility = string.IsNullOrEmpty(song.Artist) ? Visibility.Collapsed : Visibility.Visible;
            _app.Text = song.AppName;

            // Lagu baru: teks masuk dengan animasi. Play/pause saja tidak perlu.
            Motion.Pop(_compactTitle);
            Motion.Pop(_title, _artist, _app);
        }

        _visualizer.SetPlaying(song.IsPlaying);
        Motion.FadeTo(_visualizer.Root, song.IsPlaying ? 1f : 0.5f);

        _playPauseIcon.Glyph = song.IsPlaying ? PauseGlyph : PlayGlyph;
        _playPause.IsEnabled = song.CanPlayPause;
        _previous.IsEnabled = song.CanPrevious;
        _next.IsEnabled = song.CanNext;

        if (!ReferenceEquals(song.Thumbnail, previous?.Thumbnail) || previous is null)
        {
            _ = LoadThumbnailAsync(song.Thumbnail);
        }
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
                var bitmap = new BitmapImage { DecodePixelType = DecodePixelType.Logical, DecodePixelWidth = 160 };
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
        _expandedArt.SetImage(image);
        _visualizer.SetColor(accent is { } c ? Color.FromArgb(255, c.R, c.G, c.B) : DefaultAccent);
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

    /// <summary>
    /// Kotak gambar album bersudut bulat. Gambar baru crossfade di atas yang lama (dua lapis yang bergantian),
    /// dengan ikon not balok di bawahnya saat gambar tidak ada.
    /// </summary>
    private sealed class Art
    {
        private readonly Border[] _layers = new Border[2];
        private readonly FontIcon _placeholder;
        private int _front;

        public Art(double size, double cornerRadius)
        {
            _placeholder = new FontIcon
            {
                Glyph = MusicGlyph,
                FontSize = Math.Round(size * 0.45),
                Foreground = new SolidColorBrush(Muted),
            };
            var root = new Grid
            {
                Width = size,
                Height = size,
                CornerRadius = new CornerRadius(cornerRadius),
                Background = new SolidColorBrush(ArtBackground),
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
