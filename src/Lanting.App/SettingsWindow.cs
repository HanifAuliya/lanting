using System.Diagnostics;
using Lanting.App.Shell;
using Lanting.Core.Location;
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

    // Urutannya sama dengan IslandBackground.
    private static readonly string[] BackgroundNames = ["Transparan", "Solid", "Hitam pekat"];

    private readonly SettingsStore _store;
    private readonly LocationService _location;
    private CancellationTokenSource? _search;

    public SettingsWindow(SettingsStore store, LocationService location)
    {
        _store = store;
        _location = location;
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

        var background = new ComboBox { MinWidth = 150, VerticalAlignment = VerticalAlignment.Center };
        foreach (var name in BackgroundNames)
        {
            background.Items.Add(name);
        }

        background.SelectedIndex = (int)settings.Background;
        background.SelectionChanged += (_, _) =>
        {
            if (background.SelectedIndex >= 0)
            {
                var chosen = (IslandBackground)background.SelectedIndex;
                _store.Update(s => s with { Background = chosen });
            }
        };
        page.Children.Add(Row("Latar island", "Terang atau gelap mengikuti tema Windows (Setelan > Personalisasi > Warna).", background));

        page.Children.Add(Toggle(
            "Tampilkan pil saat tidak ada lagu",
            "Pil kecil berisi jam tetap ada di atas layar walau tidak ada yang diputar.",
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
            "Cuaca dari Open-Meteo untuk kota di bawah. Diperbarui paling sering 30 menit sekali.",
            settings.ShowWeather,
            on => _store.Update(s => s with { ShowWeather = on })));

        page.Children.Add(Section("Sholat dan lokasi"));
        page.Children.Add(CityRow(settings));
        page.Children.Add(Toggle(
            "Notifikasi waktu sholat",
            "Island menampilkan \"Waktunya Dzuhur\" dan seterusnya saat masuk waktu sholat. Jadwal dihitung di laptop dengan kriteria Kemenag.",
            settings.PrayerNotifications,
            on => _store.Update(s => s with { PrayerNotifications = on })));

        page.Children.Add(Section("Baterai perangkat"));
        page.Children.Add(Toggle(
            "Baterai lemah dan hampir habis",
            "Untuk perangkat yang melaporkan baterai ke Windows (biasanya Bluetooth). Banyak dongle 2.4 GHz tidak melaporkannya.",
            settings.BatteryNotifications,
            on => _store.Update(s => s with { BatteryNotifications = on })));
        page.Children.Add(Toggle(
            "Tersambung dan terputus",
            "Pemberitahuan singkat saat perangkat Bluetooth tersambung atau terputus.",
            settings.ConnectionNotifications,
            on => _store.Update(s => s with { ConnectionNotifications = on })));

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

    /// <summary>
    /// Kota untuk cuaca dan jadwal sholat: ketik nama kota lalu pilih dari daftar, atau kembali ke otomatis.
    /// Pencarian menunggu sebentar setelah berhenti mengetik supaya tidak mengirim permintaan tiap huruf.
    /// </summary>
    private Border CityRow(LantingSettings settings)
    {
        var description = new TextBlock { FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap };
        void ShowChoice(GeoLocation? city) => description.Text = city is null
            ? $"Otomatis dari alamat internet{(_location.Current is { } auto ? $" (sekarang: {auto.Place})" : string.Empty)}."
            : $"Dipilih: {city.Place}.";
        ShowChoice(settings.City);

        var results = new List<GeoLocation>();
        var box = new AutoSuggestBox { PlaceholderText = "Cari kota...", Width = 200, QueryIcon = new SymbolIcon(Symbol.Find) };
        box.TextChanged += async (sender, args) =>
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput)
            {
                return;
            }

            _search?.Cancel();
            var search = _search = new CancellationTokenSource();
            try
            {
                await Task.Delay(400, search.Token);
                var found = await _location.SearchAsync(sender.Text, search.Token);
                results.Clear();
                results.AddRange(found);
                sender.ItemsSource = found.Count > 0 ? found.Select(c => c.Place).ToList() : ["Kota tidak ditemukan"];
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                StartupLog.Write($"Cari kota gagal: {ex.Message}");
                sender.ItemsSource = new[] { "Gagal mencari (cek internet)" };
            }
        };
        box.SuggestionChosen += (sender, args) =>
        {
            var city = results.FirstOrDefault(c => c.Place == args.SelectedItem as string);
            if (city is null)
            {
                return;
            }

            _store.Update(s => s with { City = city });
            ShowChoice(city);
            sender.Text = string.Empty;
        };

        var automatic = new Button { Content = "Otomatis" };
        automatic.Click += (_, _) =>
        {
            _store.Update(s => s with { City = null });
            ShowChoice(null);
        };

        var controls = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        controls.Children.Add(box);
        controls.Children.Add(automatic);
        return Row("Kota", description, controls);
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
    private static Border Row(string title, string description, FrameworkElement control) =>
        Row(title, new TextBlock { Text = description, FontSize = 12, Opacity = 0.7, TextWrapping = TextWrapping.Wrap }, control);

    private static Border Row(string title, TextBlock description, FrameworkElement control)
    {
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 2 };
        text.Children.Add(new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap });
        text.Children.Add(description);

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
