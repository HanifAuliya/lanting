using Lanting.Core.Island;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace Lanting.App.Views;

/// <summary>Tampilan saat tidak ada konten: pil kecil kosong, dan saat di-hover nama aplikasi.</summary>
internal sealed class IdleView : IIslandView
{
    public IdleView()
    {
        var dot = new Ellipse
        {
            Width = 10,
            Height = 10,
            Fill = new SolidColorBrush(Color.FromArgb(255, 0x2D, 0xD4, 0xBF)),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var title = new TextBlock
        {
            Text = "Lanting",
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        header.Children.Add(dot);
        header.Children.Add(title);

        var expanded = new StackPanel { Margin = new Thickness(28, 24, 28, 24), Spacing = 6 };
        expanded.Children.Add(header);
        expanded.Children.Add(new TextBlock
        {
            Text = "Belum ada lagu yang diputar. Putar sesuatu di Spotify atau YouTube Music.",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 0xA0, 0xA0, 0xA0)),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap,
        });
        expanded.Children.Add(new TextBlock
        {
            Text = "Klik kanan untuk keluar.",
            Foreground = new SolidColorBrush(Color.FromArgb(255, 0x70, 0x70, 0x70)),
            FontSize = 12,
        });

        Compact = new Grid();
        Expanded = expanded;
    }

    public UIElement Compact { get; }

    public UIElement Expanded { get; }

    public double CompactWidth => Shell.IslandShape.Compact.Width;

    public bool CanShow(IslandContent content) => true;

    public void Show(IslandContent content)
    {
    }
}
