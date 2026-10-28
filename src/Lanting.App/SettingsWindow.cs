using System.Diagnostics;
using Lanting.App.Shell;
using Lanting.Core.Settings;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinUIEx;

namespace Lanting.App;

/// <summary>
/// Jendela Pengaturan. Seperti <see cref="IslandWindow"/>, tampilannya dibangun lewat kode (bukan XAML).
/// Setiap perubahan langsung disimpan dan langsung berlaku; tidak ada tombol "Simpan".
/// </summary>
internal sealed partial class SettingsWindow : Window
{
    // Warna kartu Fluent (CardBackgroundFillColorDefault / CardStrokeColorDefault) untuk tema gelap dan terang.
    // Sengaja ditulis sendiri: di build Release, Application.Current.Resources["CardBackground..."] tidak ketemu
    // dan membuat aplikasi crash saat Pengaturan dibuka (build Debug tidak terkena).
    private static readonly Color CardDark = Color.FromArgb(0x0D, 0xFF, 0xFF, 0xFF);
    private static readonly Color CardLight = Color.FromArgb(0xB3, 0xFF, 0xFF, 0xFF);
    private static readonly Color CardStrokeDark = Color.FromArgb(0x19, 0x00, 0x00, 0x00);
    private static readonly Color CardStrokeLight = Color.FromArgb(0x0F, 0x00, 0x00, 0x00);

    private readonly SettingsStore _store;

    public SettingsWindow(SettingsStore store)
    {
        _store = store;
        Title = "Pengaturan Lanting";
        SystemBackdrop = new MicaBackdrop();
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "lanting.ico"));
        this.SetWindowSize(520, 640);
        this.CenterOnScreen();
        Content = BuildContent(store.Current);
    }

    private ScrollViewer BuildContent(LantingSettings settings)
    {
        var page = new StackPanel { Margin = new Thickness(28, 24, 28, 28), Spacing = 4, MaxWidth = 640 };
        page.Children.Add(new TextBlock { Text = "Pengaturan", FontSize = 28, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });

        page.Children.Add(Section("Island"));
        page.Children.Add(Toggle(
            "Tampilkan pil saat tidak ada lagu",
            "Pil hitam kecil tetap ada di atas layar walau tidak ada yang diputar.",
            settings.ShowWhenIdle,
            on => _store.Update(s => s with { ShowWhenIdle = on })));
        page.Children.Add(Toggle(
            "Sembunyikan saat layar penuh",
            "Island hilang selama game, video, atau presentasi layar penuh.",
            settings.HideInFullscreen,
            on => _store.Update(s => s with { HideInFullscreen = on })));

        page.Children.Add(Section("Musik"));
        page.Children.Add(Toggle(
            "Tampilkan lirik",
            "Lirik dari lrclib.net. Judul dan artis lagu dikirim ke sana untuk dicari.",
            settings.ShowLyrics,
            on => _store.Update(s => s with { ShowLyrics = on })));
        page.Children.Add(Toggle(
            "Visualizer bergerak",
            "Bar kecil yang bergerak saat lagu diputar. Memakai sedikit CPU (sekitar 0,5%).",
            settings.ShowVisualizer,
            on => _store.Update(s => s with { ShowVisualizer = on })));

        page.Children.Add(Section("Dashboard"));
        page.Children.Add(Toggle(
            "Tampilkan cuaca",
            "Lokasi kira-kira dari alamat internet (ipwho.is), cuaca dari Open-Meteo. Diperbarui paling sering 30 menit sekali.",
            settings.ShowWeather,
            on => _store.Update(s => s with { ShowWeather = on })));

        page.Children.Add(Section("Baterai Bluetooth"));
        page.Children.Add(Toggle(
            "Pemberitahuan baterai",
            "Keyboard, mouse, dan perangkat Bluetooth lain: tersambung, terputus, dan baterai lemah.",
            settings.BatteryNotifications,
            on => _store.Update(s => s with { BatteryNotifications = on })));

        var threshold = new ComboBox { MinWidth = 100, VerticalAlignment = VerticalAlignment.Center };
        foreach (var percent in LantingSettings.LowBatteryChoices)
        {
            threshold.Items.Add($"{percent}%");
        }

        threshold.SelectedIndex = Math.Max(0, LantingSettings.LowBatteryChoices.ToList().IndexOf(settings.LowBatteryPercent));
        threshold.SelectionChanged += (_, _) =>
        {
            if (threshold.SelectedIndex >= 0)
            {
                var percent = LantingSettings.LowBatteryChoices[threshold.SelectedIndex];
                _store.Update(s => s with { LowBatteryPercent = percent });
            }
        };
        page.Children.Add(Row("Peringatan baterai lemah", "Beri tahu saat baterai turun ke angka ini.", threshold));

        page.Children.Add(Section("Umum"));
        page.Children.Add(Toggle(
            "Mulai bersama Windows",
            "Lanting langsung berjalan setelah login.",
            AutoStart.IsEnabled,
            AutoStart.Set));

        var version = typeof(App).Assembly.GetName().Version?.ToString(3);
        var footer = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 24, 0, 0) };
        footer.Children.Add(new TextBlock { Text = $"Lanting {version}", VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 });
        footer.Children.Add(new HyperlinkButton { Content = "GitHub", NavigateUri = new Uri("https://github.com/HanifAuliya/lanting") });
        var openLog = new HyperlinkButton { Content = "Buka folder log" };
        openLog.Click += (_, _) => Process.Start(new ProcessStartInfo
        {
            FileName = Path.GetDirectoryName(StartupLog.FilePath)!,
            UseShellExecute = true,
        });
        footer.Children.Add(openLog);
        page.Children.Add(footer);

        return new ScrollViewer { Content = page };
    }

    private static TextBlock Section(string title) => new()
    {
        Text = title,
        FontSize = 14,
        FontWeight = FontWeights.SemiBold,
        Margin = new Thickness(0, 20, 0, 4),
    };

    private static Border Toggle(string title, string description, bool value, Action<bool> changed)
    {
        // Teks On/Off dikosongkan; judul baris sudah menjelaskan.
        var toggle = new ToggleSwitch { IsOn = value, OnContent = string.Empty, OffContent = string.Empty, MinWidth = 0 };
        toggle.Toggled += (_, _) => changed(toggle.IsOn);
        return Row(title, description, toggle);
    }

    private static bool Dark => Application.Current.RequestedTheme == ApplicationTheme.Dark;

    // Satu baris pengaturan: judul dan penjelasan di kiri, kontrol di kanan, dalam kartu.
    private static Border Row(string title, string description, FrameworkElement control)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(new TextBlock { Text = description, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap });

        var grid = new Grid { ColumnSpacing = 16 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(text);
        control.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);

        return new Border
        {
            Child = grid,
            Padding = new Thickness(16, 12, 16, 12),
            CornerRadius = new CornerRadius(6),
            Background = new SolidColorBrush(Dark ? CardDark : CardLight),
            BorderBrush = new SolidColorBrush(Dark ? CardStrokeDark : CardStrokeLight),
            BorderThickness = new Thickness(1),
        };
    }
}
