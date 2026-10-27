using Microsoft.UI.Xaml.Markup;

namespace Lanting.App.Shell;

/// <summary>
/// Mode diagnosis (LANTING_XAML_DIAG=1): coba parse potongan-potongan XAML dari IslandWindow satu per satu
/// supaya ketahuan bagian mana yang membuat "XAML parsing failed". Hanya dipakai di CI.
/// </summary>
internal static class XamlDiagnostics
{
    private const string Ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";

    public static bool IsEnabled => Environment.GetEnvironmentVariable("LANTING_XAML_DIAG") == "1";

    public static void Run()
    {
        Try("canvas", $"<Canvas {Ns} Background=\"Transparent\" />");
        Try("canvas-flyout", $"<Canvas {Ns}><Canvas.ContextFlyout><MenuFlyout><MenuFlyoutItem Text=\"Keluar\" /></MenuFlyout></Canvas.ContextFlyout></Canvas>");
        Try("grid", $"<Grid {Ns} Width=\"400\" Height=\"160\" Background=\"Black\" />");
        Try("stackpanel", $"<StackPanel {Ns} Margin=\"28,24\" Spacing=\"6\" />");
        Try("ellipse", $"<Ellipse {Ns} Width=\"10\" Height=\"10\" Fill=\"#2DD4BF\" VerticalAlignment=\"Center\" />");
        Try("textblock", $"<TextBlock {Ns} Text=\"Lanting\" Foreground=\"White\" FontSize=\"20\" FontWeight=\"SemiBold\" TextWrapping=\"Wrap\" />");
        Try("named", $"<Canvas {Ns} x:Name=\"Root\"><Grid x:Name=\"Pill\" /></Canvas>");
    }

    private static void Try(string name, string xaml)
    {
        try
        {
            _ = XamlReader.Load(xaml);
            StartupLog.Write($"XAML-DIAG {name}: OK");
        }
        catch (Exception ex)
        {
            StartupLog.Write($"XAML-DIAG {name}: GAGAL {ex.GetType().Name} 0x{ex.HResult:X8} {ex.Message}");
        }
    }
}
