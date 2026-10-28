using Lanting.App.QuickToggles;
using Lanting.App.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views.Dashboard;

/// <summary>Halaman Kontrol: Wi-Fi, Bluetooth, suara, mikrofon, layar tetap menyala, mode gelap.</summary>
internal sealed class TogglesPage
{
    private readonly IslandTheme _theme;
    private readonly List<Item> _items = [];

    public TogglesPage(IslandTheme theme, IEnumerable<IQuickToggle> toggles)
    {
        _theme = theme;
        var tiles = new List<FrameworkElement>();
        foreach (var toggle in toggles)
        {
            var item = new Item(toggle, new TilePaint());
            item.Paint.Use(theme, on: false);
            item.Button = Tile(toggle.Glyph, toggle.Label, item.Paint);
            item.Button.Click += async (_, _) => await FlipAsync(item);
            _items.Add(item);
            tiles.Add(item.Button);
        }

        Root = Tiles(3, 8, tiles);
        theme.Changed += (_, _) =>
        {
            foreach (var item in _items)
            {
                item.Paint.Use(theme, item.On == true);
            }
        };
    }

    public FrameworkElement Root { get; }

    /// <summary>Baca ulang status semua kontrol (dipanggil saat dashboard dibuka).</summary>
    public async void Refresh()
    {
        foreach (var item in _items)
        {
            Show(item, await ReadAsync(item.Toggle));
        }
    }

    private async Task FlipAsync(Item item)
    {
        var current = await ReadAsync(item.Toggle);
        if (current is null)
        {
            Show(item, null);
            return;
        }

        item.Button!.IsEnabled = false;
        try
        {
            Show(item, await item.Toggle.WriteAsync(!current.Value));
        }
        catch (Exception ex)
        {
            StartupLog.Write($"{item.Toggle.Label} gagal diubah: {ex.Message}");
            Show(item, await ReadAsync(item.Toggle));
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

    // null: tidak tersedia di PC ini (misalnya tanpa Bluetooth), ubin dibuat redup dan tidak bisa diklik.
    private void Show(Item item, bool? on)
    {
        item.On = on;
        item.Paint.Use(_theme, on == true);
        item.Button!.IsEnabled = on is not null;
        item.Button.Opacity = on is null ? 0.45 : 1;
    }

    private sealed class Item(IQuickToggle toggle, TilePaint paint)
    {
        public IQuickToggle Toggle { get; } = toggle;

        public TilePaint Paint { get; } = paint;

        public Button? Button { get; set; }

        public bool? On { get; set; }
    }
}
