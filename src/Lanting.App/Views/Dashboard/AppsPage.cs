using System.Diagnostics;
using System.Numerics;
using Lanting.App.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views.Dashboard;

/// <summary>
/// Halaman Aplikasi: pintasan aplikasi Windows berjajar seperti dock, dengan ikon asli aplikasinya
/// (dibaca dari Windows sekali, saat halaman pertama kali dibuka). Ikon sedikit membesar saat disorot.
/// </summary>
internal sealed class AppsPage
{
    private const double IconSize = 40;

    private static readonly string Windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
    private static readonly string System32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
    private static readonly string Downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    // Nama, ikon cadangan (bila ikon asli gagal dibaca), file yang dijalankan, argumen, dan file/folder sumber ikon.
    private static readonly (string Label, string Glyph, string File, string? Arguments, string IconPath)[] Apps =
    [
        ("File Explorer", "\uEC50", "explorer.exe", null, Path.Combine(Windows, "explorer.exe")),
        ("Unduhan", "\uE896", "explorer.exe", "shell:Downloads", Downloads),
        ("Kalkulator", "\uE8EF", "calc.exe", null, Path.Combine(System32, "calc.exe")),
        ("Notepad", "\uE70B", "notepad.exe", null, Path.Combine(System32, "notepad.exe")),
        ("Task Manager", "\uE9D9", "taskmgr.exe", null, Path.Combine(System32, "Taskmgr.exe")),
        ("Setelan", "\uE713", "ms-settings:", null, Path.Combine(Windows, "ImmersiveControlPanel", "SystemSettings.exe")),
    ];

    private readonly List<(Border Slot, string IconPath)> _icons = [];
    private bool _iconsLoaded;

    public AppsPage(IslandTheme theme)
    {
        var dock = new Grid { ColumnSpacing = 4, VerticalAlignment = VerticalAlignment.Center };
        foreach (var (label, glyph, file, arguments, iconPath) in Apps)
        {
            var fallback = Icon(glyph, 22);
            fallback.Foreground = theme.Primary;
            var slot = new Border
            {
                Width = IconSize,
                Height = IconSize,
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = fallback,
            };
            _icons.Add((slot, iconPath));

            var name = Text(theme.Primary, 11);
            name.Text = label;
            name.HorizontalAlignment = HorizontalAlignment.Center;
            var content = new StackPanel { Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            content.Children.Add(slot);
            content.Children.Add(name);

            var tile = Pressable(content, label, 16, theme.Transparent, theme.ControlHover, theme.Control, theme.Primary);
            tile.HorizontalAlignment = HorizontalAlignment.Stretch;
            tile.Height = 100;
            tile.Click += (_, _) => Launch(label, file, arguments);
            tile.PointerEntered += (_, _) => Lift(slot, 1.14f);
            tile.PointerExited += (_, _) => Lift(slot, 1f);

            Grid.SetColumn(tile, dock.ColumnDefinitions.Count);
            dock.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            dock.Children.Add(tile);
        }

        Root = Card(theme, dock, padding: 10);
    }

    public FrameworkElement Root { get; }

    /// <summary>Halaman tampil: ikon asli dibaca sekali.</summary>
    public async void Refresh()
    {
        if (_iconsLoaded)
        {
            return;
        }

        _iconsLoaded = true;
        foreach (var (slot, path) in _icons)
        {
            try
            {
                using var thumbnail = Directory.Exists(path)
                    ? await (await StorageFolder.GetFolderFromPathAsync(path)).GetThumbnailAsync(ThumbnailMode.SingleItem, 64)
                    : await (await StorageFile.GetFileFromPathAsync(path)).GetThumbnailAsync(ThumbnailMode.SingleItem, 64);
                var image = new BitmapImage { DecodePixelWidth = 64 };
                await image.SetSourceAsync(thumbnail);
                slot.Child = new Image { Source = image, Width = IconSize, Height = IconSize };
            }
            catch (Exception ex)
            {
                // Ikon garis cadangan tetap dipakai.
                StartupLog.Write($"Ikon {Path.GetFileName(path)} gagal dibaca: {ex.Message}");
            }
        }
    }

    // Ikon membesar (atau kembali) dengan pegas kecil.
    private static void Lift(UIElement element, float scale)
    {
        var visual = ElementCompositionPreview.GetElementVisual(element);
        visual.CenterPoint = new Vector3((float)(IconSize / 2), (float)IconSize, 0);
        var spring = visual.Compositor.CreateSpringVector3Animation();
        spring.FinalValue = new Vector3(scale, scale, 1);
        spring.DampingRatio = 0.55f;
        spring.Period = TimeSpan.FromMilliseconds(45);
        visual.StartAnimation(nameof(visual.Scale), spring);
    }

    private static void Launch(string label, string file, string? arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file) { Arguments = arguments ?? string.Empty, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StartupLog.Write($"{label} gagal dibuka: {ex.Message}");
        }
    }
}
