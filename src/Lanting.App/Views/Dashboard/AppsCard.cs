using System.Diagnostics;
using Lanting.App.Shell;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views.Dashboard;

/// <summary>Pintasan aplikasi Windows yang sering dipakai.</summary>
internal sealed class AppsCard
{
    private const double ButtonSize = 34;

    private static readonly (string Label, string Glyph, string File, string? Arguments)[] Apps =
    [
        ("Explorer", "\uEC50", "explorer.exe", null),
        ("Unduhan", "\uE896", "explorer.exe", "shell:Downloads"),
        ("Kalkulator", "\uE8EF", "calc.exe", null),
        ("Notepad", "\uE70B", "notepad.exe", null),
        ("Task Manager", "\uE9D9", "taskmgr.exe", null),
        ("Setelan", "\uE713", "ms-settings:", null),
    ];

    public AppsCard()
    {
        var tiles = new List<FrameworkElement>();
        foreach (var (label, glyph, file, arguments) in Apps)
        {
            var button = IconButton(glyph, label, ButtonSize, 10);
            button.Click += (_, _) => Launch(label, file, arguments);

            var caption = Text(9, Muted);
            caption.Text = label;
            caption.HorizontalAlignment = HorizontalAlignment.Center;

            var tile = new StackPanel { Spacing = 3 };
            tile.Children.Add(button);
            tile.Children.Add(caption);
            tiles.Add(tile);
        }

        Root = Card("Aplikasi", Tiles(3, 6, tiles));
    }

    public FrameworkElement Root { get; }

    private static void Launch(string label, string file, string? arguments)
    {
        try
        {
            Process.Start(new ProcessStartInfo(file) { Arguments = arguments ?? string.Empty, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StartupLog.Write($"{label} gagal dibuka: {ex.Message}");
        }
    }
}
