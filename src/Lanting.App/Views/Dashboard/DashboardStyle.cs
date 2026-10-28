using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace Lanting.App.Views.Dashboard;

/// <summary>Warna dan bentuk yang dipakai bersama oleh kartu-kartu dashboard.</summary>
internal static class DashboardStyle
{
    public static readonly Color Accent = Color.FromArgb(255, 0x2D, 0xD4, 0xBF);
    public static readonly Color CardColor = Color.FromArgb(255, 0x1C, 0x1C, 0x1E);
    public static readonly Color ButtonColor = Color.FromArgb(255, 0x2C, 0x2C, 0x2E);
    public static readonly Color Muted = Color.FromArgb(255, 0xA0, 0xA0, 0xA0);
    public static readonly Color Dim = Color.FromArgb(255, 0x6E, 0x6E, 0x73);

    /// <summary>Kartu bersudut bulat dengan judul kecil di atas (judul boleh kosong).</summary>
    public static Border Card(string? title, UIElement content)
    {
        var body = new Grid { RowSpacing = 8 };
        body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        body.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        if (title is not null)
        {
            body.Children.Add(Caption(title));
        }

        Grid.SetRow((FrameworkElement)content, title is null ? 0 : 1);
        if (title is null)
        {
            Grid.SetRowSpan((FrameworkElement)content, 2);
        }

        body.Children.Add(content);
        return new Border
        {
            Background = new SolidColorBrush(CardColor),
            CornerRadius = new CornerRadius(16),
            Padding = new Thickness(12),
            Child = body,
        };
    }

    /// <summary>Judul kecil berhuruf kapital, misalnya "KONTROL".</summary>
    public static TextBlock Caption(string text) => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        CharacterSpacing = 60,
        Foreground = new SolidColorBrush(Muted),
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    public static TextBlock Text(double size, Color color, bool bold = false) => new()
    {
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = new SolidColorBrush(color),
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        MaxLines = 1,
    };

    /// <summary>Tombol bulat/kotak berisi ikon, dengan tooltip.</summary>
    public static Button IconButton(string glyph, string label, double size, double cornerRadius)
    {
        var button = new Button
        {
            Content = new FontIcon { Glyph = glyph, FontSize = Math.Round(size * 0.42), Foreground = new SolidColorBrush(Colors.White) },
            Width = size,
            Height = size,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(cornerRadius),
            Background = new SolidColorBrush(ButtonColor),
            BorderThickness = new Thickness(0),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        ToolTipService.SetToolTip(button, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        return button;
    }

    /// <summary>Grid dengan jumlah kolom tetap; anak-anak diisi dari kiri ke kanan, atas ke bawah.</summary>
    public static Grid Tiles(int columns, double spacing, IReadOnlyList<FrameworkElement> items)
    {
        var grid = new Grid { ColumnSpacing = spacing, RowSpacing = spacing, VerticalAlignment = VerticalAlignment.Top };
        for (var c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var r = 0; r < (items.Count + columns - 1) / columns; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (var i = 0; i < items.Count; i++)
        {
            Grid.SetColumn(items[i], i % columns);
            Grid.SetRow(items[i], i / columns);
            grid.Children.Add(items[i]);
        }

        return grid;
    }
}
