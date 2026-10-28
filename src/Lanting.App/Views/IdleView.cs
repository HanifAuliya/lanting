using Lanting.Core.Island;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lanting.App.Views;

/// <summary>Tampilan saat tidak ada konten: pil kecil berisi jam dan tanggal singkat ("13:48  Rab 28").</summary>
internal sealed class IdleView : IIslandView
{
    private readonly TextBlock _time;
    private readonly TextBlock _date;

    public IdleView(ClockTicker clock, IslandTheme theme)
    {
        _time = new TextBlock
        {
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            Foreground = theme.Primary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        _date = new TextBlock
        {
            FontSize = 12,
            Foreground = theme.Secondary,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Show(DateTime.Now);
        clock.Tick += (_, now) => Show(now);

        var line = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        line.Children.Add(_time);
        line.Children.Add(_date);
        Compact = new Grid { Children = { line } };
    }

    public UIElement Compact { get; }

    public double CompactWidth => 150;

    public bool CanShow(IslandContent content) => true;

    public void Show(IslandContent content)
    {
    }

    private void Show(DateTime now)
    {
        _time.Text = ClockTicker.Time(now);
        _date.Text = ClockTicker.ShortDate(now);
    }
}
