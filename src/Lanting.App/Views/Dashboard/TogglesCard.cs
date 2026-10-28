using Lanting.App.QuickToggles;
using Lanting.App.Shell;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views.Dashboard;

/// <summary>Kontrol cepat: Wi-Fi, Bluetooth, suara, mikrofon, layar tetap menyala, mode gelap.</summary>
internal sealed class TogglesCard
{
    private const double ButtonSize = 34;

    private readonly List<(IQuickToggle Toggle, Button Button)> _items = [];
    private readonly SolidColorBrush _onBrush = new(Accent);
    private readonly SolidColorBrush _offBrush = new(ButtonColor);
    private readonly SolidColorBrush _onGlyph = new(Colors.Black);
    private readonly SolidColorBrush _offGlyph = new(Colors.White);

    public TogglesCard(IEnumerable<IQuickToggle> toggles)
    {
        var buttons = new List<FrameworkElement>();
        foreach (var toggle in toggles)
        {
            var button = IconButton(toggle.Glyph, toggle.Label, ButtonSize, ButtonSize / 2);
            button.Click += async (_, _) => await FlipAsync(toggle, button);
            _items.Add((toggle, button));
            buttons.Add(button);
        }

        Root = Card("Kontrol", Tiles(3, 7, buttons));
    }

    public FrameworkElement Root { get; }

    /// <summary>Baca ulang status semua tombol (dipanggil saat dashboard dibuka).</summary>
    public async void Refresh()
    {
        foreach (var (toggle, button) in _items)
        {
            Show(button, await ReadAsync(toggle));
        }
    }

    private async Task FlipAsync(IQuickToggle toggle, Button button)
    {
        var current = await ReadAsync(toggle);
        if (current is null)
        {
            return;
        }

        button.IsEnabled = false;
        try
        {
            Show(button, await toggle.WriteAsync(!current.Value));
        }
        catch (Exception ex)
        {
            StartupLog.Write($"{toggle.Label} gagal diubah: {ex.Message}");
            Show(button, await ReadAsync(toggle));
        }
    }

    private static async Task<bool?> ReadAsync(IQuickToggle toggle)
    {
        try
        {
            return await toggle.ReadAsync();
        }
        catch (Exception ex)
        {
            StartupLog.Write($"{toggle.Label} gagal dibaca: {ex.Message}");
            return null;
        }
    }

    private void Show(Button button, bool? on)
    {
        button.IsEnabled = on is not null;
        button.Background = on == true ? _onBrush : _offBrush;
        ((FontIcon)button.Content).Foreground = on == true ? _onGlyph : _offGlyph;
        button.Opacity = on is null ? 0.4 : 1;
    }
}
