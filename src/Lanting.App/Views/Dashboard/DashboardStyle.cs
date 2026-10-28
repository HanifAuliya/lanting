using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Lanting.App.Views.Dashboard;

/// <summary>Bentuk-bentuk yang dipakai bersama oleh halaman dashboard. Warnanya selalu dari <see cref="IslandTheme"/>.</summary>
internal static class DashboardStyle
{
    /// <summary>Lebar dan tinggi area halaman (di bawah baris menu).</summary>
    public const double PageWidth = 528;

    public const double PageHeight = 148;

    /// <summary>Kartu bersudut bulat.</summary>
    public static Border Card(IslandTheme theme, UIElement content, double padding = 14) => new()
    {
        Background = theme.Surface,
        CornerRadius = new CornerRadius(18),
        Padding = new Thickness(padding),
        Child = content,
    };

    /// <summary>Judul kecil berhuruf kapital, misalnya "JAKARTA".</summary>
    public static TextBlock Caption(IslandTheme theme, string text = "") => new()
    {
        Text = text.ToUpperInvariant(),
        FontSize = 10,
        FontWeight = FontWeights.SemiBold,
        CharacterSpacing = 60,
        Foreground = theme.Secondary,
        TextTrimming = TextTrimming.CharacterEllipsis,
        MaxLines = 1,
    };

    public static TextBlock Text(Brush brush, double size, bool bold = false) => new()
    {
        FontSize = size,
        FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        Foreground = brush,
        TextTrimming = TextTrimming.CharacterEllipsis,
        TextWrapping = TextWrapping.NoWrap,
        MaxLines = 1,
    };

    public static FontIcon Icon(string glyph, double size) => new() { Glyph = glyph, FontSize = size };

    /// <summary>
    /// Tombol dengan warna sendiri untuk diam / disorot / ditekan. Warna bawaan WinUI diganti lewat
    /// "lightweight styling" (kunci sumber daya di <see cref="FrameworkElement.Resources"/> tombol itu sendiri),
    /// jadi tidak ada pencarian sumber daya tema dari kode (itu yang dulu crash di build Release).
    /// Isi tombol (ikon, teks) mewarisi <paramref name="foreground"/> bila tidak diberi warna sendiri.
    /// </summary>
    public static Button Pressable(
        UIElement content,
        string label,
        double cornerRadius,
        Brush rest,
        Brush hover,
        Brush pressed,
        Brush foreground)
    {
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(0),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(cornerRadius),
            Background = rest,
            Foreground = foreground,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Stretch,
        };
        button.Resources["ButtonBackgroundPointerOver"] = hover;
        button.Resources["ButtonBackgroundPressed"] = pressed;
        button.Resources["ButtonBackgroundDisabled"] = rest;
        button.Resources["ButtonForegroundPointerOver"] = foreground;
        button.Resources["ButtonForegroundPressed"] = foreground;
        button.Resources["ButtonForegroundDisabled"] = foreground;
        button.Resources["ButtonBorderBrushPointerOver"] = rest;
        button.Resources["ButtonBorderBrushPressed"] = rest;
        button.Resources["ButtonBorderBrushDisabled"] = rest;
        ToolTipService.SetToolTip(button, label);
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, label);
        return button;
    }

    /// <summary>Tombol ikon bulat tanpa latar (latar muncul saat disorot).</summary>
    public static Button RoundButton(IslandTheme theme, string glyph, string label, double size, double iconSize)
    {
        var icon = Icon(glyph, iconSize);
        icon.HorizontalAlignment = HorizontalAlignment.Center;
        icon.VerticalAlignment = VerticalAlignment.Center;
        var button = Pressable(icon, label, size / 2, theme.Transparent, theme.ControlHover, theme.ControlPressed, theme.Primary);
        button.Width = size;
        button.Height = size;
        return button;
    }

    /// <summary>
    /// Ubin kotak (kontrol cepat, aplikasi): ikon di kiri atas, nama di kiri bawah.
    /// Warnanya dari <paramref name="paint"/> supaya bisa berganti (misalnya kontrol yang menyala).
    /// </summary>
    public static Button Tile(string glyph, string label, TilePaint paint)
    {
        var icon = Icon(glyph, 18);
        icon.HorizontalAlignment = HorizontalAlignment.Left;
        icon.VerticalAlignment = VerticalAlignment.Top;

        var name = Text(paint.Foreground, 12, bold: true);
        name.Text = label;
        name.VerticalAlignment = VerticalAlignment.Bottom;

        var content = new Grid { Margin = new Thickness(12, 11, 10, 10) };
        content.Children.Add(icon);
        content.Children.Add(name);

        var tile = Pressable(content, label, 14, paint.Rest, paint.Hover, paint.Pressed, paint.Foreground);
        tile.HorizontalAlignment = HorizontalAlignment.Stretch;
        tile.VerticalAlignment = VerticalAlignment.Stretch;
        return tile;
    }

    /// <summary>Grid dengan jumlah kolom tetap dan baris sama tinggi; diisi dari kiri ke kanan, atas ke bawah.</summary>
    public static Grid Tiles(int columns, double spacing, IReadOnlyList<FrameworkElement> items)
    {
        var grid = new Grid { ColumnSpacing = spacing, RowSpacing = spacing };
        for (var c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        for (var r = 0; r < (items.Count + columns - 1) / columns; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
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

/// <summary>
/// Kuas milik satu ubin. Warnanya disalin dari kuas tema; dipakai untuk ubin yang warnanya berganti
/// (menyala/mati), karena warna saat disorot tidak bisa diganti lagi setelah tombol tampil, hanya warnanya.
/// </summary>
internal sealed class TilePaint
{
    public SolidColorBrush Rest { get; } = new();

    public SolidColorBrush Hover { get; } = new();

    public SolidColorBrush Pressed { get; } = new();

    public SolidColorBrush Foreground { get; } = new();

    public void CopyFrom(SolidColorBrush rest, SolidColorBrush hover, SolidColorBrush pressed, SolidColorBrush foreground)
    {
        Rest.Color = rest.Color;
        Hover.Color = hover.Color;
        Pressed.Color = pressed.Color;
        Foreground.Color = foreground.Color;
    }

    /// <summary>Warna ubin biasa, atau warna aksen bila <paramref name="on"/>.</summary>
    public void Use(IslandTheme theme, bool on)
    {
        if (on)
        {
            CopyFrom(theme.Accent, theme.AccentHover, theme.Accent, theme.OnAccent);
        }
        else
        {
            CopyFrom(theme.Control, theme.ControlHover, theme.ControlPressed, theme.Primary);
        }
    }
}
