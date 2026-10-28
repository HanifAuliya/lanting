using Lanting.App.Battery;
using Lanting.Core.Battery;
using Lanting.Core.Settings;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views.Dashboard;

/// <summary>
/// Halaman Perangkat: keyboard, mouse, dan perangkat Bluetooth yang tersambung beserta sisa baterainya
/// (bila perangkatnya melaporkan ke Windows), ditambah pengaturan notifikasi baterai.
/// </summary>
internal sealed class DevicesPage
{
    private const double SettingsWidth = 176;

    private static readonly Color Amber = Color.FromArgb(255, 0xF5, 0x9E, 0x0B);
    private static readonly Color Red = Color.FromArgb(255, 0xEF, 0x44, 0x44);

    private readonly IslandTheme _theme;
    private readonly BatteryModule _battery;
    private readonly InputDeviceWatcher _inputs;
    private readonly SettingsStore _settings;
    private readonly DispatcherQueue _dispatcher;
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly FrameworkElement _empty;
    private readonly Toggle _lowToggle;
    private readonly Toggle _connectionToggle;
    private readonly TextBlock _threshold;
    private bool _visible;

    public DevicesPage(IslandTheme theme, BatteryModule battery, InputDeviceWatcher inputs, SettingsStore settings)
    {
        _theme = theme;
        _battery = battery;
        _inputs = inputs;
        _settings = settings;
        _dispatcher = DispatcherQueue.GetForCurrentThread();

        // Kiri: daftar perangkat.
        var emptyText = Text(theme.Secondary, 12);
        emptyText.Text = "Tidak ada keyboard, mouse, atau perangkat Bluetooth yang tersambung";
        emptyText.TextWrapping = TextWrapping.Wrap;
        emptyText.MaxLines = 2;
        emptyText.TextAlignment = TextAlignment.Center;
        emptyText.HorizontalAlignment = HorizontalAlignment.Center;
        emptyText.VerticalAlignment = VerticalAlignment.Center;
        emptyText.Margin = new Thickness(16, 0, 16, 0);
        _empty = emptyText;

        var scroll = new ScrollViewer
        {
            Content = _list,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        var listCard = Card(theme, new Grid { Children = { scroll, _empty } }, padding: 6);

        // Kanan: notifikasi.
        _lowToggle = new Toggle(theme, "Baterai lemah", on => _settings.Update(s => s with { BatteryNotifications = on }));
        _connectionToggle = new Toggle(theme, "Sambung / putus", on => _settings.Update(s => s with { ConnectionNotifications = on }));
        _threshold = Text(theme.Primary, 12, bold: true);
        _threshold.VerticalAlignment = VerticalAlignment.Center;
        var minus = RoundButton(theme, "\uE738", "Turunkan batas", 24, 10);
        var plus = RoundButton(theme, "\uE710", "Naikkan batas", 24, 10);
        minus.Click += (_, _) => StepThreshold(-1);
        plus.Click += (_, _) => StepThreshold(+1);
        var stepper = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, HorizontalAlignment = HorizontalAlignment.Right };
        stepper.Children.Add(minus);
        stepper.Children.Add(_threshold);
        stepper.Children.Add(plus);

        var settingsPanel = new StackPanel { Spacing = 4 };
        settingsPanel.Children.Add(Caption(theme, "Notifikasi"));
        settingsPanel.Children.Add(_lowToggle.Root);
        settingsPanel.Children.Add(_connectionToggle.Root);
        settingsPanel.Children.Add(Row(theme, "Batas lemah", stepper));
        var settingsCard = Card(theme, settingsPanel, padding: 12);
        settingsCard.Width = SettingsWidth;

        var root = new Grid { ColumnSpacing = 10 };
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        root.Children.Add(listCard);
        Grid.SetColumn(settingsCard, 1);
        root.Children.Add(settingsCard);
        Root = root;

        _battery.DevicesChanged += (_, _) => _dispatcher.TryEnqueue(ShowDevices);
        _inputs.Changed += (_, _) => _dispatcher.TryEnqueue(ShowDevices);
        _settings.Changed += (_, s) => _dispatcher.TryEnqueue(() => ShowSettings(s));
        theme.Changed += (_, _) => ShowDevices();
        ShowSettings(settings.Current);
    }

    public FrameworkElement Root { get; }

    /// <summary>Halaman tampil: mulai memantau perangkat (sekali saja) dan tampilkan daftar terbaru.</summary>
    public void Refresh()
    {
        _visible = true;
        _inputs.Start();
        ShowDevices();
    }

    private void StepThreshold(int direction)
    {
        var choices = LantingSettings.LowBatteryChoices;
        var index = Math.Clamp(IndexOf(choices, _settings.Current.LowBatteryPercent) + direction, 0, choices.Count - 1);
        var percent = choices[index];
        _settings.Update(s => s with { LowBatteryPercent = percent });
    }

    private void ShowSettings(LantingSettings settings)
    {
        _lowToggle.Set(settings.BatteryNotifications);
        _connectionToggle.Set(settings.ConnectionNotifications);
        _threshold.Text = $"{settings.LowBatteryPercent}%";
    }

    private void ShowDevices()
    {
        if (!_visible)
        {
            return;
        }

        var devices = DeviceList.Merge(_inputs.Devices, _battery.Devices);
        _list.Children.Clear();
        foreach (var device in devices)
        {
            _list.Children.Add(DeviceRow(device));
        }

        _empty.Visibility = devices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Grid DeviceRow(DeviceEntry device)
    {
        var icon = Icon(DeviceGlyph(device.Kind), 15);
        icon.Foreground = _theme.Primary;
        var badge = new Border
        {
            Width = 32,
            Height = 32,
            CornerRadius = new CornerRadius(16),
            Background = _theme.Control,
            Child = icon,
        };

        var name = Text(_theme.Primary, 13, bold: true);
        name.Text = device.Name;
        var detail = Text(_theme.Secondary, 11);
        var connection = device.Connection switch
        {
            DeviceConnection.Bluetooth => "Bluetooth",
            DeviceConnection.Wireless => "Dongle 2.4 GHz",
            _ => "Kabel USB",
        };
        detail.Text = !device.Connected ? $"{connection} · terputus"
            : device.Percent is null && device.Connection != DeviceConnection.Cable ? $"{connection} · baterai tidak dilaporkan"
            : connection;
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(name);
        text.Children.Add(detail);

        var row = new Grid { Height = 44, ColumnSpacing = 10, Padding = new Thickness(6, 0, 10, 0), Opacity = device.Connected ? 1 : 0.55 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(badge);
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        if (device.Percent is { } percent)
        {
            var level = BatteryLevel(percent);
            Grid.SetColumn(level, 2);
            row.Children.Add(level);
        }

        return row;
    }

    // Baterai kecil berisi sesuai persen, lalu angkanya. Kuning saat lemah, merah saat hampir habis.
    private StackPanel BatteryLevel(int percent)
    {
        var low = _settings.Current.LowBatteryPercent;
        Brush fill = percent <= Math.Min(10, low / 2) ? new SolidColorBrush(Red)
            : percent <= low ? new SolidColorBrush(Amber)
            : _theme.Primary;

        var body = new Grid
        {
            Width = 24,
            Height = 12,
            CornerRadius = new CornerRadius(3),
            BorderBrush = _theme.Secondary,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(1.5),
        };
        body.Children.Add(new Border
        {
            Width = Math.Max(2, 19 * percent / 100.0),
            CornerRadius = new CornerRadius(1.5),
            Background = fill,
            HorizontalAlignment = HorizontalAlignment.Left,
        });
        var tip = new Border { Width = 2, Height = 5, CornerRadius = new CornerRadius(0, 1, 1, 0), Background = _theme.Secondary, VerticalAlignment = VerticalAlignment.Center };
        var value = Text(_theme.Primary, 12, bold: true);
        value.Text = $"{percent}%";
        value.VerticalAlignment = VerticalAlignment.Center;

        var level = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        level.Children.Add(value);
        level.Children.Add(new Border { Width = 6 });
        level.Children.Add(body);
        level.Children.Add(tip);
        return level;
    }

    private static string DeviceGlyph(BatteryDeviceKind kind) => kind switch
    {
        BatteryDeviceKind.Keyboard => "\uE765",
        BatteryDeviceKind.Mouse => "\uE962",
        BatteryDeviceKind.Headphones => "\uE7F6",
        BatteryDeviceKind.Gamepad => "\uE7FC",
        _ => "\uE772",
    };

    private static int IndexOf(IReadOnlyList<int> list, int value)
    {
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i] == value)
            {
                return i;
            }
        }

        return 0;
    }

    private static Grid Row(IslandTheme theme, string label, FrameworkElement control)
    {
        var text = Text(theme.Primary, 12);
        text.Text = label;
        text.VerticalAlignment = VerticalAlignment.Center;
        control.VerticalAlignment = VerticalAlignment.Center;
        var row = new Grid { Height = 28 };
        row.Children.Add(text);
        control.HorizontalAlignment = HorizontalAlignment.Right;
        row.Children.Add(control);
        return row;
    }

    /// <summary>Saklar kecil: kapsul berwarna aksen saat menyala, dengan bulatan yang bergeser.</summary>
    private sealed class Toggle
    {
        private readonly IslandTheme _theme;
        private readonly TilePaint _paint = new();
        private readonly Border _knob;
        private bool _on;

        public Toggle(IslandTheme theme, string label, Action<bool> changed)
        {
            _theme = theme;
            _knob = new Border
            {
                Width = 12,
                Height = 12,
                CornerRadius = new CornerRadius(6),
                Background = _paint.Foreground,
                Margin = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
            };
            var button = Pressable(new Grid { Children = { _knob } }, label, 10, _paint.Rest, _paint.Hover, _paint.Pressed, _paint.Foreground);
            button.Width = 36;
            button.Height = 20;
            button.Click += (_, _) => changed(!_on);
            theme.Changed += (_, _) => Paint();
            Root = Row(theme, label, button);
            Paint();
        }

        public FrameworkElement Root { get; }

        public void Set(bool on)
        {
            _on = on;
            Paint();
        }

        private void Paint()
        {
            _paint.Use(_theme, _on);
            if (!_on)
            {
                // Mati: bulatan abu-abu di atas kapsul redup.
                _paint.Foreground.Color = _theme.Secondary.Color;
            }

            _knob.HorizontalAlignment = _on ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        }
    }
}
