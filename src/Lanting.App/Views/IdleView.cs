using Lanting.Core.Island;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lanting.App.Views;

/// <summary>Tampilan saat tidak ada konten: pil kecil berisi jam.</summary>
internal sealed class IdleView : IIslandView
{
    private readonly TextBlock _time = new()
    {
        FontSize = 15,
        FontWeight = FontWeights.SemiBold,
        Foreground = new SolidColorBrush(Colors.White),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    public IdleView(ClockTicker clock)
    {
        _time.Text = ClockTicker.Time(DateTime.Now);
        clock.Tick += (_, now) => _time.Text = ClockTicker.Time(now);
        Compact = new Grid { Children = { _time } };
    }

    public UIElement Compact { get; }

    public double CompactWidth => 150;

    public bool CanShow(IslandContent content) => true;

    public void Show(IslandContent content)
    {
    }
}
