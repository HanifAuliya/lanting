using Lanting.Core.Battery;
using Lanting.Core.Island;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lanting.App.Views;

/// <summary>
/// Pemberitahuan baterai: tersambung/terputus tampil sebentar di pil kecil,
/// baterai lemah/kritis tampil di pil notifikasi yang lebih besar.
/// </summary>
internal sealed class BatteryView : IIslandView
{
    private static readonly Color Accent = Color.FromArgb(255, 0x2D, 0xD4, 0xBF);
    private static readonly Color Amber = Color.FromArgb(255, 0xFB, 0xBF, 0x24);
    private static readonly Color Red = Color.FromArgb(255, 0xF8, 0x71, 0x71);
    private static readonly Color Muted = Color.FromArgb(255, 0xA0, 0xA0, 0xA0);

    private readonly FontIcon _compactDevice = Icon(14);
    private readonly TextBlock _compactName = Text(12, Colors.White);
    private readonly FontIcon _compactBattery = Icon(14);
    private readonly TextBlock _compactPercent = Text(12, Colors.White);

    private readonly FontIcon _noticeDevice = Icon(20);
    private readonly TextBlock _noticeTitle = Text(14, Colors.White);
    private readonly TextBlock _noticeDetail = Text(12, Muted);
    private readonly FontIcon _noticeBattery = Icon(22);

    private readonly FontIcon _expandedDevice = Icon(28);
    private readonly TextBlock _expandedName = Text(16, Colors.White);
    private readonly TextBlock _expandedStatus = Text(13, Muted);
    private readonly TextBlock _expandedPercent = Text(34, Colors.White);

    public BatteryView()
    {
        // Pil kecil: [ikon] nama ... [baterai] 80%
        var compact = new Grid { Margin = new Thickness(14, 0, 14, 0), ColumnSpacing = 8 };
        compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        compact.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        compact.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Place(compact, _compactDevice, 0);
        Place(compact, _compactName, 1);
        Place(compact, _compactBattery, 2);
        Place(compact, _compactPercent, 3);
        Compact = compact;

        // Pil notifikasi (360x64): [ikon] judul / nama · persen ... [baterai]
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        _noticeTitle.FontWeight = FontWeights.SemiBold;
        text.Children.Add(_noticeTitle);
        text.Children.Add(_noticeDetail);
        var notice = new Grid { Margin = new Thickness(22, 0, 24, 0), ColumnSpacing = 14 };
        notice.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        notice.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        notice.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Place(notice, _noticeDevice, 0);
        Place(notice, text, 1);
        Place(notice, _noticeBattery, 2);
        Notification = notice;

        // Pil besar (saat di-hover): ikon + nama + status, persen besar di kanan.
        var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 4 };
        _expandedName.FontWeight = FontWeights.SemiBold;
        info.Children.Add(_expandedName);
        info.Children.Add(_expandedStatus);
        _expandedPercent.FontWeight = FontWeights.SemiBold;
        var expanded = new Grid { Margin = new Thickness(28, 0, 32, 0), ColumnSpacing = 16 };
        expanded.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        expanded.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        expanded.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Place(expanded, _expandedDevice, 0);
        Place(expanded, info, 1);
        Place(expanded, _expandedPercent, 2);
        Expanded = expanded;
    }

    public UIElement Compact { get; }

    public UIElement Expanded { get; }

    public UIElement? Notification { get; }

    public double CompactWidth => 260;

    public bool CanShow(IslandContent content) => content.Payload is BatteryNotice;

    public void Show(IslandContent content)
    {
        if (content.Payload is not BatteryNotice notice)
        {
            return;
        }

        var device = notice.Device;
        var color = notice.Kind switch
        {
            BatteryNoticeKind.Critical => Red,
            BatteryNoticeKind.Low => Amber,
            BatteryNoticeKind.Connected => Accent,
            _ => Muted,
        };
        var brush = new SolidColorBrush(color);
        var deviceGlyph = DeviceGlyph(device.Kind);
        var batteryGlyph = BatteryGlyph(device.Percent);
        var percent = device.Percent is { } p ? $"{p}%" : string.Empty;
        var status = notice.Kind switch
        {
            BatteryNoticeKind.Connected => "Tersambung",
            BatteryNoticeKind.Disconnected => "Terputus",
            BatteryNoticeKind.Low => "Baterai lemah",
            _ => "Baterai hampir habis",
        };

        _compactDevice.Glyph = deviceGlyph;
        _compactDevice.Foreground = brush;
        _compactName.Text = device.Name;
        _compactBattery.Glyph = batteryGlyph;
        _compactBattery.Foreground = brush;
        _compactPercent.Text = percent;

        _noticeDevice.Glyph = deviceGlyph;
        _noticeDevice.Foreground = brush;
        _noticeTitle.Text = status;
        _noticeDetail.Text = percent.Length == 0 ? device.Name : $"{device.Name} · {percent}";
        _noticeBattery.Glyph = batteryGlyph;
        _noticeBattery.Foreground = brush;

        _expandedDevice.Glyph = deviceGlyph;
        _expandedDevice.Foreground = brush;
        _expandedName.Text = device.Name;
        _expandedStatus.Text = status;
        _expandedPercent.Text = percent;
        _expandedPercent.Foreground = brush;

        Motion.Pop(_compactName, _compactPercent);
        Motion.Pop(_noticeTitle, _noticeDetail);
    }

    // Segoe Fluent Icons / MDL2: Battery0 (U+E850) sampai Battery10 (U+E85A).
    private static string BatteryGlyph(int? percent) =>
        Glyph(0xE850 + (int)Math.Round(Math.Clamp(percent ?? 0, 0, 100) / 10.0));

    private static string DeviceGlyph(BatteryDeviceKind kind) => kind switch
    {
        BatteryDeviceKind.Keyboard => Glyph(0xE765),
        BatteryDeviceKind.Mouse => Glyph(0xE962),
        BatteryDeviceKind.Headphones => Glyph(0xE7F6),
        BatteryDeviceKind.Gamepad => Glyph(0xE7FC),
        _ => Glyph(0xE702),
    };

    private static string Glyph(int codePoint) => char.ConvertFromUtf32(codePoint);

    private static FontIcon Icon(double size) => new() { FontSize = size, VerticalAlignment = VerticalAlignment.Center };

    private static TextBlock Text(double size, Color color) => new()
    {
        FontSize = size,
        Foreground = new SolidColorBrush(color),
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        MaxLines = 1,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static void Place(Grid grid, FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        grid.Children.Add(element);
    }
}
