using System.Diagnostics;
using Lanting.App.Shell;
using Microsoft.UI.Xaml;
using static Lanting.App.Views.Dashboard.DashboardStyle;

namespace Lanting.App.Views.Dashboard;

/// <summary>Halaman Aplikasi: pintasan aplikasi Windows yang sering dipakai.</summary>
internal sealed class AppsPage
{
    private static readonly (string Label, string Glyph, string File, string? Arguments)[] Apps =
    [
        ("File Explorer", "\uEC50", "explorer.exe", null),
        ("Unduhan", "\uE896", "explorer.exe", "shell:Downloads"),
        ("Kalkulator", "\uE8EF", "calc.exe", null),
        ("Notepad", "\uE70B", "notepad.exe", null),
        ("Task Manager", "\uE9D9", "taskmgr.exe", null),
        ("Setelan Windows", "\uE713", "ms-settings:", null),
    ];

    public AppsPage(IslandTheme theme)
    {
        // Ubin aplikasi tidak pernah berganti warna, jadi satu TilePaint bisa dipakai bersama.
        var paint = new TilePaint();
        paint.Use(theme, on: false);
        theme.Changed += (_, _) => paint.Use(theme, on: false);

        var tiles = new List<FrameworkElement>();
        foreach (var (label, glyph, file, arguments) in Apps)
        {
            var tile = Tile(glyph, label, paint);
            tile.Click += (_, _) => Launch(label, file, arguments);
            tiles.Add(tile);
        }

        Root = Tiles(3, 8, tiles);
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
