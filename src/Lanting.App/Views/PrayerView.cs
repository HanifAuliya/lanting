using Lanting.Core.Island;
using Lanting.Core.Prayer;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views;

/// <summary>Notifikasi "Waktunya Dzuhur" saat masuk waktu sholat.</summary>
internal sealed class PrayerView : IIslandView
{
    /// <summary>Ikon bulan sabit (Segoe Fluent Icons), juga dipakai di menu Sholat.</summary>
    public const string Glyph = "\uE708";

    private readonly TextBlock _compactText;
    private readonly TextBlock _title;
    private readonly TextBlock _detail;
    private readonly TextBlock _time;

    public PrayerView(IslandTheme theme)
    {
        // Pil kecil: [bulan] Waktunya Dzuhur
        var compactIcon = Icon(Glyph, 13);
        compactIcon.Foreground = theme.Accent;
        _compactText = Text(theme.Primary, 12, bold: true);
        _compactText.VerticalAlignment = VerticalAlignment.Center;
        var compact = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        compact.Children.Add(compactIcon);
        compact.Children.Add(_compactText);
        Compact = compact;

        // Pil notifikasi: [bulan] Waktunya Dzuhur / Jakarta ... 11:40
        var icon = Icon(Glyph, 18);
        icon.Foreground = theme.OnAccent;
        var badge = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18),
            Background = theme.Accent,
            Child = icon,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _title = Text(theme.Primary, 14, bold: true);
        _detail = Text(theme.Secondary, 12);
        _time = Text(theme.Primary, 20, bold: true);
        _time.FontWeight = FontWeights.SemiBold;
        _time.VerticalAlignment = VerticalAlignment.Center;

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        text.Children.Add(_title);
        text.Children.Add(_detail);

        var notice = new Grid { Margin = new Thickness(16, 0, 22, 0), ColumnSpacing = 12 };
        notice.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        notice.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        notice.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        notice.Children.Add(badge);
        Grid.SetColumn(text, 1);
        notice.Children.Add(text);
        Grid.SetColumn(_time, 2);
        notice.Children.Add(_time);
        Notification = notice;
    }

    public UIElement Compact { get; }

    public UIElement? Notification { get; }

    public double CompactWidth => 220;

    public bool CanShow(IslandContent content) => content.Payload is PrayerNotice;

    public void Show(IslandContent content)
    {
        if (content.Payload is not PrayerNotice notice)
        {
            return;
        }

        _compactText.Text = $"Waktunya {notice.Time.Label}";
        _title.Text = $"Waktunya {notice.Time.Label}";
        _detail.Text = notice.Place;
        _time.Text = ClockTicker.Time(notice.Time.Time);
        Motion.Pop(_title, _detail, _time);
    }
}
